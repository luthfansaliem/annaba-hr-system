# 30 — Panduan Pengguna HR System

Panduan singkat per peran. Nama menu ditulis seperti di aplikasi (bahasa Indonesia; nama Inggrisnya di dalam kurung). Hal yang meminta **konfirmasi kata sandi** (data sensitif, payroll, dokumen) akan menampilkan dialognya sendiri; itu normal.

## 1. Semua karyawan

| Yang ingin dilakukan | Caranya |
|---|---|
| Melihat ringkasan hari ini | **HR Saya** (My HR): status absen, sisa cuti, pengajuan yang menunggu, tugas, pengumuman, slip gaji terbaru |
| Absen masuk/pulang | **Absensi Saya** (My Attendance) → pilih *Di kantor / Dari rumah / Klien-perjalanan dinas* → **Absen masuk**. Jam diambil dari server. Bila perusahaan menetapkan lokasi kerja dan Anda memilih "Di kantor", peramban akan meminta izin lokasi; menolak tetap bisa absen, tetapi tercatat "Lokasi belum terkonfirmasi" |
| Lupa absen | **Absensi Saya** → *Koreksi* → isi jam dan alasan → menunggu atasan |
| Lembur | **Absensi Saya** → *Lembur* → diajukan **sebelum** hari-H, maks. 4 jam/hari dan 18 jam/minggu |
| Mengajukan cuti | **Cuti Saya** (My Leave) → **Ajukan cuti**. Saldo berkurang saat disetujui. Jenis cuti tertentu perlu persetujuan atasan **lalu** HR; statusnya terlihat di daftar |
| Membatalkan cuti | **Cuti Saya** → *Batalkan* (yang masih menunggu, atau yang disetujui tetapi belum dimulai) |
| Slip gaji | **Slip Gaji Saya** (My Payslips) → *Lihat* / *PDF*. Slip muncul setelah penggajian dikunci; Anda mendapat notifikasi dan e-mail tanpa nominal |
| Rekap pajak tahunan | **Slip Gaji Saya** → *Rekap {tahun} (PDF)*. Untuk SPT; bukti potong resmi 1721-A1 tetap dari perusahaan lewat e-Bupot |
| Klaim pengeluaran | **Klaim Saya** (My Claims) → **Klaim baru** → kategori, tanggal (maks. 90 hari ke belakang), jumlah, keterangan, bukti (PDF/PNG/JPEG ≤ 4 MB). Yang disetujui dibayar bersama slip gaji berikutnya, bebas pajak |
| Surat keterangan kerja | **Surat Keterangan Kerja** (Employment Letters) → **Minta surat** → tulis keperluannya → setelah disetujui HR, unduh PDF bernomor. Surat tidak memuat gaji |
| Mengubah data diri | **Profil HR Saya** (My HR Profile). Kontak berubah langsung; nama, status pernikahan, rekening, NPWP, NIK lewat pengajuan yang disetujui HR |
| Mengunduh data saya | **Profil HR Saya** → *Ekspor data saya* (hak akses data, UU PDP) |
| Mengatur notifikasi | Lonceng di kanan atas → *Preferensi*: pilih per kategori, di aplikasi dan/atau e-mail. Pesan keamanan akun selalu dikirim |

## 2. Atasan (Manager)

| Yang ingin dilakukan | Caranya |
|---|---|
| Memutuskan semua pengajuan | **Kotak Persetujuan** (Approvals Inbox): cuti, koreksi absen, lembur, klaim. Centang beberapa → **Setujui terpilih**, atau **Tolak** (alasan wajib; terlihat oleh karyawan). Anda tidak bisa memutuskan pengajuan sendiri |
| Melihat tim hari ini | **Tim Saya** (My Team): hadir / cuti / belum absen, cuti 30 hari ke depan, kontrak yang berakhir ≤ 60 hari |
| Cuti / tidak di tempat | **Delegasi Persetujuan** (Delegate Approvals): tunjuk pengganti untuk rentang tanggal; pengganti menerima notifikasi dan dapat memutuskan |
| Pengajuan tidak diputuskan | Setelah beberapa hari HR menerima e-mail eskalasi; HR dapat memutuskan |

## 3. HR Admin

| Yang ingin dilakukan | Caranya |
|---|---|
| Menambah karyawan | **Karyawan** (Employees) → **Tambah Karyawan**. Pilih perusahaan bila lebih dari satu; tautkan akun masuk agar karyawan bisa memakai layanan mandiri. Checklist onboarding dibuat otomatis |
| Impor massal | **Karyawan** → *Impor*: CSV dengan `full_name, hire_date, job_title` (opsional `company, department, manager_no, religion, bpjs_kes_no, bpjs_tk_no, …`). *Dry-run* dulu; satu baris salah = tidak ada yang tersimpan |
| Mutasi / promosi | Detail karyawan → *Ubah jabatan* (riwayat efektif-tanggal; tidak menimpa) |
| Data terlindungi | Detail karyawan → tab *Data terlindungi*: NIK, NPWP, rekening, No. BPJS (terenkripsi; setiap kali ditampilkan tercatat di audit) |
| Dokumen | Detail karyawan → tab *Dokumen*: unggah, **versi baru** (ikon ganti), riwayat versi. Bila pemindai virus aktif, berkas terinfeksi atau yang tidak bisa dipindai ditolak |
| Mengakhiri hubungan kerja | Detail karyawan → *Akhiri*: akun dinonaktifkan dan sesi dicabut pada tanggal efektif; checklist offboarding dibuat |
| Jenis cuti & libur | **Pengaturan Cuti** (Leave Setup): kuota, carry-over, *Persetujuan HR mulai (hari)* untuk approval dua tingkat, libur nasional/cuti bersama |
| Shift & lokasi | **Shift** dan **Lokasi Kerja** (Work Locations). Lokasi + radius membuat absen "di kantor" di luar lokasi ditandai (tidak ditolak) |
| Menutup absensi bulan | **Laporan Absensi** → **Tutup bulan** (wajib sebelum payroll bila ada pekerja harian atau potongan alpa). *Laporan lembur* tersedia sebagai Excel |
| Perusahaan | **Perusahaan** (Companies): nama, NPWP, alamat; satu perusahaan utama |
| Surat keterangan kerja | Disetujui dari **Kotak Persetujuan**; nomor `SKK/tahun/urut` dibuat otomatis |
| Pengumuman & kebijakan | **Pengumuman** (Announcements): target departemen, wajib-konfirmasi, laporan siapa yang sudah membaca |
| Penghapusan data (PDP) | Lihat doc 26; anonimisasi karyawan yang sudah lama keluar tidak mengubah angka payroll |

## 4. Payroll Officer dan Payroll Approver (dua orang berbeda)

1. **Sebelum run pertama / saat regulasi berubah**: **Aturan Penggajian** (Payroll Rules) → periksa tarif BPJS, PTKP, TER → *Versi baru* berlaku mulai tanggal 1 bulan yang belum dikunci, dengan nomor peraturan sebagai alasan.
2. **Kompensasi** (Compensation): gaji pokok/tunjangan atau *upah harian* per karyawan, status PTKP, kepesertaan BPJS. **Komponen Gaji Berulang** (Recurring Pay Items) untuk tunjangan/potongan tiap bulan. **Pinjaman** (Loans) untuk kasbon.
3. **Penggajian** (Pay Runs) → **Penggajian baru** (per perusahaan, per bulan) → tambah bonus/THR/potongan → **Hitung**. Baca setiap peringatan dan daftar "tidak ikut run". Klaim yang disetujui, cicilan pinjaman, komponen berulang, dan selisih back-date masuk otomatis.
4. **Approver** (orang lain): periksa total terhadap bulan lalu → **Setujui** → **Kunci**. Setelah dikunci angka tidak bisa diubah (aplikasi dan database menolak); koreksi lewat baris penyesuaian di run berikutnya.
5. Unduh **File bank**, **Register**, **Laporan BPJS**, **Jurnal** (centang *Sebagai Excel* bila perlu) → bayar → **Tandai dibayar**.
6. Laporan: **Biaya per departemen** dan **Ringkasan pajak tahunan** di halaman Penggajian.

## 5. Administrator sistem

- **Cek Kesiapan Go-Live** (Go-Live Check): daftar yang dijawab sistem sendiri (dua super admin, MFA akun berhak tinggi, maker–checker, HTTPS, e-mail, antivirus, monitoring, kelengkapan data payroll) dan yang harus dikonfirmasi manusia (validasi konsultan pajak, pentest, latihan pemulihan, dokumen PDP, pelatihan). **Merah = jangan go-live.**
- **Webhook**: beri tahu sistem lain saat ada kejadian HR (skema: doc 29). **OpenAPI**: `/api/openapi/v1.json` saat masuk, salinan di `docs/openapi-v1.json`.
- Monitoring: `GET /health/metrics` dengan token; aturan alert di `ops/prometheus-alerts.yml` (doc 24).

## 6. Pertanyaan yang sering muncul

- **"Slip gaji saya belum ada."** Slip terbit saat penggajian *dikunci*, bukan saat dihitung.
- **"Klaim saya sudah disetujui tapi belum dibayar."** Dibayar pada penggajian berikutnya; statusnya menjadi *Dibayar* saat penggajian itu dikunci.
- **"Kenapa diminta kata sandi lagi?"** Data sensitif dan payroll meminta konfirmasi ulang bila sudah lebih dari beberapa menit sejak konfirmasi terakhir.
- **"Saya atasan, tapi tombol Setujui hilang."** Jenis cuti itu perlu HR setelah Anda; status "Disetujui atasan · menunggu HR".
- **"Absen saya bertanda Lokasi belum terkonfirmasi."** Anda memilih "Di kantor" tanpa berbagi lokasi atau di luar radius. Bukan pelanggaran otomatis; HR yang menilai.
