"use client";

import { Suspense, useState } from "react";
import { useSearchParams } from "next/navigation";
import { BadgeCheck, Banknote, FileSearch, KeyRound, ShieldAlert } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { AuthField, AuthInput } from "@/components/auth/auth";
import { messageOf, post } from "@/lib/api/client";
import { fmtDate } from "@/lib/format";
import { t } from "@/i18n";

type Result = { valid: boolean; kind: string | null; company: string | null; reference: string | null; holder: string | null; issuedOn: string | null; amountMatches: boolean | null };

/** Public page: anyone holding a printed employment letter or payslip checks its verification code here. No account needed; amounts are never shown. */
function Verify() {
  const [code, setCode] = useState(useSearchParams().get("code") ?? ""); const [amount, setAmount] = useState("");
  const [busy, setBusy] = useState(false); const [err, setErr] = useState(""); const [res, setRes] = useState<Result | null>(null);
  async function check(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setRes(null); setBusy(true);
    const n = Number(amount.replace(/[^\d]/g, ""));
    try { setRes(await post<Result>("/api/verify", { code, amount: amount.trim() && n > 0 ? n : null }, { noRoute: true })); } catch (x) { setErr(messageOf(x)); } finally { setBusy(false); }
  }
  return (
    <AuthFrame>
      <AuthHeading icon={<FileSearch size={26} />} title={t("Check a document")} sub={t("Enter the verification code printed at the bottom of the employment letter or payslip.")} />
      <form onSubmit={check} noValidate>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <AuthField id="code" label={t("Verification code")}><AuthInput id="code" icon={<KeyRound size={18} />} value={code} onChange={(e) => setCode(e.target.value)} placeholder="XXXX-XXXX-XXXX" autoComplete="off" spellCheck={false} /></AuthField>
        <AuthField id="amount" label={t("Take-home pay on the payslip (optional)")}><AuthInput id="amount" icon={<Banknote size={18} />} inputMode="numeric" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="9.338.650" autoComplete="off" /></AuthField>
        <button className="signin" type="submit" disabled={busy || code.trim().length < 12}>{busy ? t("Checking…") : t("Check")}</button>
      </form>
      {res && !res.valid && <div className="form-alert err" role="alert" style={{ marginTop: 16 }}><ShieldAlert size={16} aria-hidden /> {t("This code is not known. The document may not be genuine, or the code was mistyped.")}</div>}
      {res?.valid && (
        <div className="form-alert ok" role="status" style={{ marginTop: 16, display: "block" }}>
          <p style={{ margin: 0, fontWeight: 600 }}><BadgeCheck size={16} aria-hidden /> {res.kind === "payslip" ? t("Genuine payslip") : t("Genuine employment letter")}</p>
          <dl className="kvs" style={{ marginTop: 8 }}>
            <div className="kv"><dt>{t("Issued by")}</dt><dd>{res.company}</dd></div>
            <div className="kv"><dt>{res.kind === "payslip" ? t("Period") : t("Number")}</dt><dd>{res.reference}</dd></div>
            <div className="kv"><dt>{t("Issued to")}</dt><dd>{res.holder}</dd></div>
            {res.issuedOn && <div className="kv"><dt>{t("Issued on")}</dt><dd>{fmtDate(res.issuedOn)}</dd></div>}
            {res.amountMatches !== null && <div className="kv"><dt>{t("Take-home pay")}</dt><dd>{res.amountMatches ? t("Matches our records") : t("Does NOT match our records")}</dd></div>}
          </dl>
          <p style={{ margin: "8px 0 0", fontSize: 12 }}>{t("Compare these details with the paper in front of you. If they differ, the document has been altered.")}</p>
        </div>
      )}
    </AuthFrame>
  );
}
export default function VerifyDocumentPage() { return <Suspense><Verify /></Suspense>; }
