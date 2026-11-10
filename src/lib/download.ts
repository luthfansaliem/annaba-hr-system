/** Downloads a file from the API through fetch (the session cookie is sent and a refusal becomes a readable error instead of a blank tab). */
export async function download(path: string): Promise<void> {
  const res = await fetch(path, { credentials: "same-origin", cache: "no-store" });
  if (!res.ok) {
    let msg = `Download failed (${res.status}).`;
    try { msg = (await res.json()).error?.message ?? msg; } catch { /* not JSON */ }
    throw new Error(msg);
  }
  const a = document.createElement("a"); a.href = URL.createObjectURL(await res.blob());
  a.download = res.headers.get("content-disposition")?.match(/filename="?([^";]+)/)?.[1] ?? "download.csv"; a.click(); URL.revokeObjectURL(a.href);
}
