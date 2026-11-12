# 38 — Rencana kerja menuju setara Talenta (paket lengkap / enterprise)

Disusun 12 November 2026 atas permintaan Luthfan: daftar semua pekerjaan sampai Annaba HRIS setara dengan Mekari Talenta paket lengkap (Essential + Plus + Talenta 360 + modul tambahan), dikerjakan bertahap tanpa menunggu konfirmasi per tahap; pengujian dan validasi bersama dilakukan setelah semua tahap selesai. Dasar perbandingannya ada di `37-talenta-comparison.md`.

Semua tahap dikerjakan di branch `feature/talenta-parity` (satu pull request ke `main`). Produksi tidak di-deploy sampai validasi bersama.

## Tahap dan isinya

Status: ⏳ belum · 🔨 sedang · ✅ selesai di branch (lihat tabel status di bawah).

| Tahap | Isi | Padanan Talenta |
|---|---|---|
| **10. Metode pajak** | Gross, gross-up (tunjangan PPh), nett (ditanggung perusahaan); PPh 26 untuk WP luar negeri dengan tarif P3B; laporan, slip, jurnal, data 1721-A1 ikut | Perhitungan pajak (Essential) |
| **11. Struktur gaji dan benefit** | Grade dan rentang gaji, perencanaan kenaikan gaji massal dengan persetujuan; kebijakan klaim per kategori dengan plafon dan saldo per karyawan; cash advance dan pelunasannya; bunga pinjaman; flexible benefit berbasis poin/saldo; penarikan gaji yang sudah diperoleh (EWA internal, dipotong di pay run) | Struktur gaji, Compensation Management, Reimbursement, Cash Advance, Pinjaman, Flexible Benefit, EWA |
| **12. Administrasi HR** | Persetujuan bertingkat untuk semua jenis pengajuan; custom fields karyawan; template surat (generator dokumen bernomor); formulir dan survei; HR helpdesk (tiket); kalender perusahaan (ulang tahun, hari libur, acara); direktori karyawan; cabang dan SBU di bawah perusahaan | Multi level approval, Custom fields, Dokumen HR, Form & survei, HR Helpdesk, Kalender, Direktori, SBU/multi cabang |
| **13. Absensi lanjutan** | Jam istirahat; izin per jam; kebijakan block leave; on-call; task dan timesheet per proyek; penyusun jadwal shift (roster mingguan, pola rotasi); mode kios/tablet absensi; kunjungan lapangan dengan riwayat lokasi (live tracking) | Istirahat, Hourly leave, Block leave, On-call, Task & timesheet, Shift planner, Talenta Tab Portal, Live Tracking |
| **14. Aplikasi karyawan** | Aplikasi web terpasang (PWA) dengan notifikasi push dan kunci PIN untuk slip gaji; tampilan seluler khusus untuk absen dan pengajuan | Aplikasi mobile Talenta |
| **15. Kinerja** | KPI dan OKR dengan hierarki target dan progres; siklus review (self, atasan, rekan/360°); pustaka kompetensi dan penilaian kompetensi; kalibrasi; matriks 9-box | Performance review, KPI & OKR, Goal hierarchy, Competency review |
| **16. Pengembangan talenta** | Individual Development Plan; rencana suksesi; talent pool dan direktori talenta | IDP, Succession plan, Talent directory |
| **17. Rekrutmen** | Manpower planning per unit dan anggaran; permintaan tenaga kerja dengan persetujuan; lowongan dan halaman karier publik; pipeline kandidat (ATS) dengan tahap yang bisa diatur; unggah CV; asesmen/tes termasuk tes kepribadian; jadwal wawancara; surat penawaran; kandidat diterima menjadi karyawan dengan onboarding | Manpower planning, Rekrutmen (ATS), Asesmen, MBTI |
| **18. LMS** | Kursus dan materi, kuis dan nilai, penugasan ke karyawan, progres, sertifikat otomatis | Learning Management System |
| **19. Analitik lanjutan** | Dasbor kinerja, lembur, dan turnover; deteksi karyawan berisiko keluar (berbasis aturan); pembuat laporan kustom (pilih data, kolom, filter, kelompok) dan pivot | Insight+, Custom dashboard, Pivotable report, Employee at Risk |
| **20. Integrasi** | Kunci API untuk sistem lain dengan cakupan izin; ekspor jurnal format aplikasi akuntansi umum; format file transfer beberapa bank | Public API, Integrasi ERP/akuntansi, Payroll disbursement (sebagian) |
| **21. Asisten AI** | Asisten tanya-jawab kebijakan HR dan data milik sendiri, memakai API model bahasa bila kuncinya diisi (mati secara bawaan) | Talenta AI / AI insight |

## Yang tidak bisa diselesaikan hanya dengan kode

Disiapkan sejauh mungkin di tahap terkait, tetapi butuh kontrak, sertifikasi, atau pihak ketiga:

- **e-Bupot langsung ke DJP**: harus lewat PJAP resmi (seperti Klikpajak) atau API DJP bagi wajib pajak tertentu.
- **Transfer gaji otomatis**: butuh API bank korporat (perjanjian dengan bank).
- **Tanda tangan elektronik tersertifikasi**: butuh PSrE berinduk Kominfo.
- **Pengenalan wajah dan AI liveness yang andal**: butuh layanan biometrik (atau model on-device) dan kajian PDP data biometrik.
- **Integrasi job portal** (JobStreet, LinkedIn, Glints): butuh kemitraan/API masing-masing.
- **EWA dengan dana pihak ketiga**: butuh penyedia pembiayaan; yang dibangun adalah versi internal (dibayar perusahaan, dipotong di pay run).
- **Aplikasi di Play Store / App Store**: butuh akun pengembang dan proses rilis; yang dibangun adalah PWA.

## Status

Panduan pengguna (doc 36 dan PDF-nya) diperbarui sekali di akhir, setelah semua tahap.

| Tahap | Status | Catatan |
|---|---|---|
| 10 | ✅ | Gross/gross-up/nett per karyawan, PPh 26 + tarif P3B, tarif PPh 26 di versi aturan; pay run, slip (layar + PDF), register, jurnal, ringkasan pajak, Data 1721-A1 ikut. Tes: 4 kasus hitung tangan + uji semua gaji 4–80 jt × 3 status termasuk Desember, 1 tes API ujung ke ujung. Aturan dicatat untuk konsultan (doc 31) |
| 11 | ✅ | Grade gaji (rentang min/tengah/maks, posisi tiap karyawan), peninjauan gaji massal (disiapkan → diajukan → disetujui orang lain → diterapkan sebagai periode gaji baru), kebijakan klaim per kategori/grade dengan plafon tahunan dan per klaim serta kategori benefit sendiri (bisa kena pajak), saldo klaim karyawan, uang muka dinas (minta → setuju → dibayarkan → pertanggungjawaban → selisih lewat pay run), tarik gaji lebih awal (maks. 50% gaji yang sudah diperoleh, dipotong di pay run bulan itu), bunga flat dan tenor pinjaman. Tes API: 4 skenario ujung ke ujung + sapuan otorisasi |
| 12 | ✅ | Persetujuan bertingkat yang bisa diatur kini juga untuk lembur dan koreksi absen (atasan → atasan dari atasan → HR, per ambang jam); field tambahan karyawan (teks/angka/tanggal/pilihan/ya-tidak, terlihat atau bisa diubah karyawan); template surat dengan placeholder, nomor per awalan per tahun, PDF berkode verifikasi, terlihat di Surat Saya; formulir dan survei (anonim atau tidak, per departemen, hasil agregat); Tanya HR (tiket dengan percakapan dan catatan internal); kalender perusahaan (acara, libur, ulang tahun tanpa tahun lahir, ulang tahun kerja, cuti satu departemen); direktori karyawan; cabang dan unit bisnis. Tes API: 6 skenario + sapuan otorisasi. Uang muka dinas belum memakai rantai persetujuan bertingkat (keputusan langsung atasan/pengelola klaim) |
| 13–21 | ⏳ | |
