/** Reads a picked file as base64 (without the data: prefix), for JSON uploads. */
export const toBase64 = (file: File) => new Promise<string>((resolve, reject) => { const r = new FileReader(); r.onload = () => resolve(String(r.result).split(",")[1] ?? ""); r.onerror = () => reject(r.error); r.readAsDataURL(file); });
