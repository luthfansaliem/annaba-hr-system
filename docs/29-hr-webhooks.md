# 29 — HR Webhooks: skema payload & cara memverifikasi

Dokumen untuk tim integrasi (ERP, akuntansi, intranet). Pengaturan alamat dilakukan di **HR → Webhooks** (izin `hrconfig.manage`). Rute API untuk mengelolanya ada di [doc 28](28-hr-api-routes.md).

## 1. Prinsip

- **Hanya id dan kode.** Payload tidak membawa data pribadi atau nominal gaji. Penerima mengambil detail lewat API bila perlu.
- **Dikirim sekali per kejadian per alamat, minimal-satu-kali (at-least-once).** Penerima harus *idempoten*: gunakan `id` (atau header `X-Annaba-Delivery`) untuk membuang duplikat.
- Event dibuat **dalam transaksi yang sama** dengan perubahannya: ada event ⇔ perubahan terjadi. Tidak ada event untuk perubahan yang dibatalkan.
- Hanya `https://` ke alamat **publik** (alamat privat/loopback/link-local ditolak, termasuk bila DNS mengarah ke sana saat menyambung).

## 2. Amplop (semua event)

```json
{
  "id": "0198c7a2-5c1e-7b4a-9f33-2d6e5a1f0c11",
  "type": "leave.approved",
  "createdAt": "2026-11-10T03:21:45.120+00:00",
  "data": { }
}
```

| Field | Tipe | Keterangan |
|---|---|---|
| `id` | uuid | Id kejadian; **sama** pada setiap percobaan ulang. Kunci idempotensi. |
| `type` | string | Salah satu event di §3. |
| `createdAt` | ISO-8601 | Waktu kejadian dibuat (bukan waktu pengiriman). |
| `data` | objek | Isi per event (§3). Field baru dapat ditambahkan kapan saja; abaikan field yang tidak dikenal. |

Header setiap permintaan (`POST`, `Content-Type: application/json`, `User-Agent: AnnabaHR-Webhooks/1.0`):

| Header | Isi |
|---|---|
| `X-Annaba-Event` | `type` event |
| `X-Annaba-Delivery` | id pengiriman (unik per percobaan-berantai; sama untuk percobaan ulang) |
| `X-Annaba-Timestamp` | detik Unix saat pengiriman ini ditandatangani |
| `X-Annaba-Signature` | `v1=` + HMAC-SHA256 heksadesimal huruf kecil |

## 3. Event

Semua `*Id` adalah uuid, tanggal berformat `yyyy-MM-dd`.

### `employee.created`
| Field | Keterangan |
|---|---|
| `employeeId` | id karyawan |
| `employeeNo` | nomor karyawan |
| `legalEntityId` | perusahaan (badan hukum) tempat ia bekerja |
| `hireDate` | tanggal masuk |

### `employee.terminated`
| Field | Keterangan |
|---|---|
| `employeeId`, `employeeNo` | karyawan |
| `lastDay` | hari kerja terakhir |

### `leave.approved` / `leave.rejected`
| Field | Keterangan |
|---|---|
| `requestId` | id pengajuan cuti |
| `employeeId`, `employeeNo` | karyawan |
| `leaveType` | kode jenis cuti (mis. `ANNUAL`, `SICK`) |
| `startDate`, `endDate` | rentang cuti |
| `days` | jumlah hari (boleh `0.5`) |

### `payroll.locked`
Pay run **dikunci** (angka final; slip gaji terbit).
| Field | Keterangan |
|---|---|
| `payRunId` | id pay run |
| `legalEntityId` | perusahaan pemilik pay run |
| `year`, `month` | periode |
| `employees` | jumlah karyawan dalam pay run |

### `payroll.paid`
Pay run ditandai **sudah dibayar**.
| Field | Keterangan |
|---|---|
| `payRunId`, `legalEntityId`, `year`, `month` | seperti di atas |

### `ping`
Dikirim oleh tombol **Send test**; bukan kejadian bisnis.
| Field | Keterangan |
|---|---|
| `message` | teks tetap `Test event from Annaba HR` |

Contoh lengkap:

```json
{
  "id": "0198c7a2-8d40-7e10-8a52-6c0d3b7f9e21",
  "type": "payroll.locked",
  "createdAt": "2026-11-10T03:40:02.771+00:00",
  "data": { "payRunId": "0198c7a2-8c3f-7a91-b5de-1f2a3c4d5e6f", "legalEntityId": "0198c79e-1111-7222-8333-444455556666", "year": 2026, "month": 10, "employees": 142 }
}
```

## 4. Memverifikasi tanda tangan

Tanda tangan = `HMAC-SHA256(secret, "<X-Annaba-Timestamp>.<badan permintaan mentah>")`, heksadesimal huruf kecil, diawali `v1=`. **Hitung atas byte badan mentah** (sebelum di-parse JSON). Rahasia ditampilkan **sekali** saat webhook dibuat atau saat "New secret" (keduanya memerlukan konfirmasi kata sandi); setelah diganti, pengiriman berikutnya memakai rahasia baru, jadi ganti di sisi penerima lebih dulu atau terima keduanya sebentar.

Langkah penerima:
1. Ambil `X-Annaba-Timestamp`; **tolak bila selisihnya dari jam Anda > 5 menit** (anti-replay).
2. Hitung HMAC atas `timestamp + "." + badan mentah`.
3. Bandingkan dengan `X-Annaba-Signature` memakai perbandingan **waktu-konstan**.
4. Buang bila `id` sudah pernah diproses; balas `2xx` secepatnya (proses berat di antrean Anda sendiri).

Node.js:
```js
import { createHmac, timingSafeEqual } from "node:crypto";
export function verify(rawBody, headers, secret, toleranceSec = 300) {
  const ts = Number(headers["x-annaba-timestamp"]);
  if (!Number.isFinite(ts) || Math.abs(Date.now() / 1000 - ts) > toleranceSec) return false;
  const expected = "v1=" + createHmac("sha256", secret).update(`${ts}.${rawBody}`).digest("hex");
  const got = headers["x-annaba-signature"] ?? "";
  return got.length === expected.length && timingSafeEqual(Buffer.from(got), Buffer.from(expected));
}
```

Python:
```python
import hmac, hashlib, time
def verify(raw_body: bytes, headers: dict, secret: str, tolerance=300) -> bool:
    ts = int(headers["X-Annaba-Timestamp"])
    if abs(time.time() - ts) > tolerance: return False
    mac = hmac.new(secret.encode(), f"{ts}.".encode() + raw_body, hashlib.sha256).hexdigest()
    return hmac.compare_digest(headers.get("X-Annaba-Signature", ""), "v1=" + mac)
```

C#:
```csharp
static bool Verify(string rawBody, string timestamp, string signature, string secret)
{
    if (!long.TryParse(timestamp, out var ts) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts) > 300) return false;
    using var h = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
    var expected = "v1=" + Convert.ToHexString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{ts}.{rawBody}"))).ToLowerInvariant();
    return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(signature), System.Text.Encoding.UTF8.GetBytes(expected));
}
```

## 5. Pengiriman ulang & status

- Balasan **2xx** = berhasil. Selain itu (atau gagal sambung/timeout) dicoba ulang: **1 mnt → 5 mnt → 30 mnt → 2 jam → 6 jam**, total 6 percobaan, lalu status **dead** (tidak dicoba lagi; terlihat di HR → Webhooks → Deliveries sebagai "Failed"; daftar menampilkan 50 pengiriman terakhir).
- Webhook yang dimatikan/dihapus membuat pengiriman tertunda menjadi dead.
- **Send test** mengirim event `ping` (lihat §3) lewat antrean dan percobaan ulang yang sama, untuk mencoba alamat dan kode tanda tangan Anda. Ping dikirim ke alamat itu terlepas dari langganan event-nya.
- Urutan **tidak dijamin** antar event; gunakan `createdAt` bila urutan penting.
- Beberapa instance API aman: setiap pengiriman di-lease dengan satu `UPDATE` bersyarat, sehingga tidak terkirim ganda oleh dua instance pada saat yang sama (duplikat tetap mungkin setelah timeout; lihat idempotensi di §1).

## 6. Rencana perubahan

Skema bersifat **aditif**: field baru boleh muncul tanpa pemberitahuan; perubahan yang merusak (hapus/ubah arti field) akan memakai prefiks tanda tangan baru (`v2=`) atau nama event baru, dan diumumkan di dokumen ini.
