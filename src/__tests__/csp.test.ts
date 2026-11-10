import { describe, expect, it } from "vitest";
import { buildCsp, config } from "@/middleware";

describe("buildCsp", () => {
  it("allows scripts only from this origin and the request nonce", () => {
    const csp = buildCsp("abc123", false);
    expect(csp).toContain("script-src 'self' 'nonce-abc123' 'strict-dynamic'");
    expect(csp).not.toContain("unsafe-eval");
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).toContain("object-src 'none'");
  });
  it("adds unsafe-eval only in development", () => {
    expect(buildCsp("n", true)).toContain("'unsafe-eval'");
  });
});

describe("middleware matcher", () => {
  it("sends every /api path to the API, also one that looks like a static file", () => {
    // the asset rule alone would skip /api/openapi/v1.json and the web server would answer 404
    expect(config.matcher.map((m) => m.source)).toContain("/api/:path*");
  });
});
