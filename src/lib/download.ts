import { askReauth, csrfToken } from "@/lib/api/client";

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

/** Same as {@link download} for a file the API builds from a posted request (a report export). */
export async function downloadPost(path: string, body: unknown, fallbackName = "export.csv"): Promise<void> {
  const res = await fetch(path, { method: "POST", credentials: "same-origin", cache: "no-store", headers: { "Content-Type": "application/json", "X-CSRF-Token": await csrfToken().catch(() => "") }, body: JSON.stringify(body) });
  if (!res.ok) { let msg = `Download failed (${res.status}).`; try { msg = (await res.json()).error?.message ?? msg; } catch { /* not JSON */ } throw new Error(msg); }
  const a = document.createElement("a"); a.href = URL.createObjectURL(await res.blob());
  a.download = res.headers.get("content-disposition")?.match(/filename="?([^";]+)/)?.[1] ?? fallbackName; a.click(); URL.revokeObjectURL(a.href);
}
