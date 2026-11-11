#!/usr/bin/env bash
# Outside-in security checks that need no account: what an anonymous visitor (or a scanner) can see and do.
# It only sends harmless, unauthenticated requests, so it is safe against staging and production. It is a smoke test, not a penetration test:
# it catches a deployment that lost a protection (a header, the sign-in wall, the proxy key), not a clever attacker. See docs/33-hr-security-self-assessment.md.
#
#   ./scripts/security-smoke.sh https://hr.example.com            the public address of the web application
#   API_DIRECT=http://localhost:5180 ./scripts/security-smoke.sh http://localhost:3100     also checks that the API refuses callers that bypass the web application
#
# Exit code 0 only when every check passes.
set -uo pipefail

BASE="${1:?usage: security-smoke.sh <base-url>}"; BASE="${BASE%/}"; API_DIRECT="${API_DIRECT:-}"
pass=0; fail=0
ok()  { pass=$((pass + 1)); printf '  ok    %s\n' "$1"; }
bad() { fail=$((fail + 1)); printf '  FAIL  %s\n        %s\n' "$1" "$2"; }
status()  { curl -sk -o /dev/null -m 20 -w '%{http_code}' "$@"; }
headers() { curl -sk -o /dev/null -m 20 -D - "$@" | tr -d '\r'; }
body()    { curl -sk -m 20 "$@"; }
has_header() { printf '%s\n' "$1" | grep -qi "^$2:"; }
expect_status() { local name="$1" want="$2"; shift 2; local got; got="$(status "$@")"; if [[ " $want " == *" $got "* ]]; then ok "$name ($got)"; else bad "$name" "expected $want, got $got"; fi; }

echo "Security smoke test against $BASE"
echo "1. Pages are served with browser protections"
H="$(headers "$BASE/login")"
for h in X-Content-Type-Options X-Frame-Options Referrer-Policy Permissions-Policy; do has_header "$H" "$h" && ok "$h is set" || bad "$h is set" "header missing on /login"; done
if printf '%s\n' "$H" | grep -qiE '^content-security-policy(-report-only)?:.*script-src'; then
  printf '%s\n' "$H" | grep -qi '^content-security-policy:.*script-src' && ok "script policy (CSP) is enforced" || bad "script policy (CSP) is enforced" "only Content-Security-Policy-Report-Only is sent: set CSP_MODE=enforce"
else bad "script policy (CSP) is present" "no Content-Security-Policy with script-src on /login"; fi
printf '%s\n' "$H" | grep -qiE "^content-security-policy:.*frame-ancestors 'none'" && ok "the application cannot be framed" || bad "the application cannot be framed" "frame-ancestors 'none' missing"
has_header "$H" "X-Powered-By" && bad "no framework banner" "X-Powered-By is sent" || ok "no framework banner"
if [[ "$BASE" == https://* ]]; then
  has_header "$H" "Strict-Transport-Security" && ok "HSTS is set" || bad "HSTS is set" "Strict-Transport-Security missing"
  plain="http://${BASE#https://}"; code="$(status "$plain/login")"; loc="$(headers "$plain/login" | grep -i '^location:' | head -1)"
  if [[ "$code" =~ ^30[1278]$ && "$loc" == *https://* ]] || [[ "$code" == "000" ]]; then ok "plain http redirects to https or is closed ($code)"; else bad "plain http redirects to https or is closed" "got $code"; fi
else echo "  skip  HSTS and https redirect (not an https address)"; fi

echo "2. Nothing answers without signing in"
for p in /api/auth/me /api/users /api/hr/employees /api/hr/payroll/runs /api/hr/me/payslips /api/audit /api/openapi/v1.json /api/scim-tokens; do expect_status "GET $p needs a session" "401" "$BASE$p"; done
expect_status "SCIM needs a provisioning token" "401" "$BASE/api/scim/v2/Users"
expect_status "SCIM refuses a made-up token" "401" -H "Authorization: Bearer scim_not-a-real-token-0123456789" "$BASE/api/scim/v2/Users"
expect_status "metrics are not reachable from outside" "404" "$BASE/health/metrics"
expect_status "an unknown API route does not exist" "401 404" "$BASE/api/does-not-exist"
expect_status "changing state without a session is refused" "401 403" -X POST -H 'Content-Type: application/json' -d '{}' "$BASE/api/hr/employees"

echo "3. Errors do not explain the inside"
E="$(body "$BASE/api/auth/me")"
[[ "$E" == *'"error"'* && "$E" == *'"code"'* ]] && ok "errors use the one envelope" || bad "errors use the one envelope" "got: ${E:0:120}"
if printf '%s' "$E$(body "$BASE/api/hr/employees/not-a-guid")$(body -X POST -H 'Content-Type: application/json' -d '{not json' "$BASE/api/auth/login")" | grep -qiE 'stack ?trace|at [A-Za-z]+\.[A-Za-z]+\(|Npgsql|Microsoft\.AspNetCore|System\.[A-Z][a-z]+Exception'; then bad "no stack traces or component names in errors" "an error body names internals"; else ok "no stack traces or component names in errors"; fi

echo "4. Sign-in does not help a guesser"
a="$(body -X POST -H 'Content-Type: application/json' -H "Origin: $BASE" -d '{"email":"nobody-'$RANDOM'@example.invalid","password":"wrong-password-1"}' "$BASE/api/auth/login")"
b="$(body -X POST -H 'Content-Type: application/json' -H "Origin: $BASE" -d '{"email":"someone-'$RANDOM'@example.invalid","password":"wrong-password-2"}' "$BASE/api/auth/login")"
strip() { printf '%s' "$1" | sed -E 's/"requestId":"[^"]*"//'; }
[[ -n "$a" && "$(strip "$a")" == "$(strip "$b")" ]] && ok "a wrong password and an unknown address get the same answer" || bad "a wrong password and an unknown address get the same answer" "answers differ: ${a:0:100} / ${b:0:100}"
expect_status "sign-in from another site is refused" "400 401 403" -X POST -H 'Content-Type: application/json' -H 'Origin: https://evil.example' -d '{"email":"x@example.invalid","password":"x"}' "$BASE/api/auth/login"
C="$(headers -H 'Origin: https://evil.example' "$BASE/api/settings/public")"
has_header "$C" "Access-Control-Allow-Origin" && bad "other sites cannot read the API (no CORS)" "Access-Control-Allow-Origin is sent" || ok "other sites cannot read the API (no CORS)"

echo "5. Public endpoints give away nothing"
V="$(body -X POST -H 'Content-Type: application/json' -H "Origin: $BASE" -d '{"code":"0000-0000-0000"}' "$BASE/api/verify")"
[[ "$V" == *'"valid":false'* && "$V" != *'"holder":"'* ]] && ok "an unknown document code says only 'not known'" || bad "an unknown document code says only 'not known'" "got: ${V:0:160}"
P="$(body "$BASE/api/settings/public")"
printf '%s' "$P" | grep -qiE 'secret|apikey|api_key|pepper|hash"|token"|privatekey|connectionstring' && bad "public settings hold no secrets" "a secret-looking key is present" || ok "public settings hold no secrets"
T="$(curl -sk -m 20 -X TRACE -H 'X-Smoke-Probe: echo-me-back' -w '\n%{http_code}' "$BASE/login")"
if [[ "$T" == *echo-me-back* || "${T##*$'\n'}" == "200" ]]; then bad "TRACE does not echo the request" "status ${T##*$'\n'}"; else ok "TRACE does not echo the request (${T##*$'\n'})"; fi
expect_status "source maps and environment files are not served" "404" "$BASE/.env"
expect_status "the repository is not served" "404" "$BASE/.git/config"

if [[ -n "$API_DIRECT" ]]; then
  echo "6. The API refuses callers that bypass the web application ($API_DIRECT)"
  expect_status "no proxy key: refused" "401 403 404" "${API_DIRECT%/}/api/settings/public"
  expect_status "wrong proxy key: refused" "401 403 404" -H 'X-Internal-Proxy-Key: guess' "${API_DIRECT%/}/api/settings/public"
  expect_status "metrics without the scrape token do not exist" "404" "${API_DIRECT%/}/health/metrics"
fi

echo
echo "SECURITY SMOKE $([[ $fail -eq 0 ]] && echo OK || echo FAILED) · $(date -u +%FT%TZ) · $BASE · $pass passed · $fail failed"
[[ $fail -eq 0 ]]
