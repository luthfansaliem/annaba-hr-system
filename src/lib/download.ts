import { askReauth } from "@/lib/api/client";

/**
 * Downloads a file from the API through fetch (the session cookie is sent and a refusal becomes a readable error instead of a blank tab).
 * When the server asks for a recent password check it shows the usual confirmation and tries once more.
 */
export async function download(path: string, retried = false): Promise<void> {
  const res = await fetch(path, { credentials: "same-origin", cache: "no-store" });
  if (!res.ok) {
    let msg = `Download failed (${res.status}).`; let code = "";
    try { const body = await res.json(); msg = body.error?.message ?? msg; code = body.error?.code ?? ""; } catch { /* not JSON */ }
    if (code === "reauth_required" && !retried && (await askReauth())) return download(path, true);
    throw new Error(msg);
  }
  const a = document.createElement("a"); a.href = URL.createObjectURL(await res.blob());
  a.download = res.headers.get("content-disposition")?.match(/filename="?([^";]+)/)?.[1] ?? "download.csv"; a.click(); URL.revokeObjectURL(a.href);
}
