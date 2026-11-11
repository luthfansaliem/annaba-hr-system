# 30 — Panduan Pengguna HR System

Panduan singkat per peran. Nama menu ditulis seperti di aplikasi (bahasa Indonesia; nama Inggrisnya di dalam kurung). Hal yang meminta **konfirmasi kata sandi** (data sensitif, payroll, dokumen) akan menampilkan dialognya sendiri; itu normal.

## 0. Susunan menu

Menu mengikuti pekerjaan HR, dari atas ke bawah: **Beranda** · **HR Saya** (absen, cuti, slip gaji, klaim, surat, profil, pengumuman milik sendiri) · **Persetujuan** · lalu satu grup per modul: **Karyawan**, **Kehadiran**, **Cuti**, **Penggajian**, **Klaim** · **Pengaturan HR** · **Administrasi** (pengguna, peran, audit, pengaturan sistem) · **Akun Saya**. Setiap orang hanya melihat grup yang boleh ia buka; karyawan biasa melihat Beranda, HR Saya, dan Akun Saya.

Setiap grup modul dibuka oleh halaman **Analitik**-nya, dan daftar utama tiap modul (Karyawan, Laporan Absensi, Semua Cuti, Pay Run, Klaim Biaya) memuat ringkasan empat angka di atas tabel dengan tautan *Lihat analitik*:

| Analitik | Isinya | Siapa yang melihat |
|---|---|---|
| Karyawan | jumlah karyawan 12 bulan, masuk dan keluar per bulan, per departemen, masa kerja, status kerja, jenis kelamin, kontrak yang segera berakhir | yang boleh melihat data karyawan |
| Kehadiran | hadir, terlambat, cuti, dan belum masuk hari ini; kehadiran 30 hari (dengan bagian yang terlambat); tingkat tepat waktu; tempat bekerja; keterlambatan per departemen; jam lembur disetujui 6 bulan | yang boleh melihat absensi semua orang |
| Cuti | sedang cuti, menunggu keputusan, hari cuti per bulan, per jenis cuti dan departemen, tingkat persetujuan, rata-rata waktu keputusan | yang boleh melihat semua cuti |
| Penggajian | bruto, gaji bersih, PPh 21, dan total biaya perusahaan pay run terkunci terakhir; tren 12 bulan; biaya per departemen; komponen biaya; pay run dalam proses; sisa pinjaman. **Hanya angka total**, dan meminta konfirmasi kata sandi seperti daftar pay run | yang boleh melihat payroll |
| Klaim | menunggu keputusan, disetujui belum dibayar, dibayar tahun ini, per kategori, departemen, dan status, rata-rata waktu keputusan | yang mengelola klaim |

Angka pada grafik selalu tersedia juga sebagai teks (nilai di samping batang, tabel untuk pembaca layar). Dasbor akses dan keamanan (akun, peran, aktivitas masuk) pindah ke **Administrasi → Ringkasan Akses**.

## 1. Semua karyawan

| Yang ingin dilakukan | Caranya |
|---|---|
| Melihat ringkasan hari ini | **Beranda** (Home): status absen, sisa cuti, pengajuan yang menunggu, tugas, pengumuman, slip gaji terbaru. Yang bekerja di HR juga melihat *Organisasi hari ini*: jumlah karyawan, hadir, cuti, yang menunggu keputusan, dan grafik kehadiran |
| Absen masuk/pulang | **Absensi Saya** (My Attendance) → pilih *Di kantor / Dari rumah / Klien-perjalanan dinas* → **Absen masuk**. Jam diambil dari server. Bila perusahaan menetapkan lokasi kerja dan Anda memilih "Di kantor", peramban akan meminta izin lokasi; menolak tetap bisa absen, tetapi tercatat "Lokasi belum terkonfirmasi" |
| Lupa absen | **Absensi Saya** → *Koreksi* → isi jam dan alasan → menunggu atasan |
| Lembur | **Absensi Saya** → *Lembur* → diajukan **sebelum** hari-H, maks. 4 jam/hari dan 18 jam/minggu |
| Mengajukan cuti | **Cuti Saya** (My Leave) → **Ajukan cuti**. Saldo berkurang saat disetujui. Bila ada hari bawaan tahun lalu yang akan hangus, kartu saldo menampilkan jumlah dan tanggalnya, dan Anda diingatkan 30 dan 7 hari sebelumnya. Jenis cuti tertentu perlu persetujuan atasan **lalu** HR; statusnya terlihat di daftar |
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
4b. **Pembayaran Mingguan** (Weekly Payouts) untuk pekerja harian: setelah minggu berakhir → **Pembayaran mingguan baru** (Senin, % ditahan) → **Hitung** → orang lain **Setujui** → *File bank* → **Tandai dibayar**. Penggajian bulanan otomatis memotong yang sudah dibayar mingguan.
5. Unduh **File bank**, **Register**, **Laporan BPJS**, **Jurnal** (centang *Sebagai Excel* bila perlu) → bayar → **Tandai dibayar**.
6. Laporan: **Biaya per departemen** dan **Ringkasan pajak tahunan** di halaman Penggajian. **Data 1721-A1** (CSV per karyawan untuk diisikan ke Coretax) ada di halaman yang sama.
7. **Run paralel**: di halaman pay run pilih **Bandingkan dengan payroll lama**, unggah CSV dari sistem lama (kolom nomor karyawan dan bruto/BPJS/PPh 21/gaji bersih). Hasilnya: siapa yang cocok, berbeda, atau hanya ada di satu sisi, dengan selisih per angka; unduh selisih untuk dicatat penjelasannya. Berkas tidak disimpan.
8. Pekerja harian: di **Kompensasi** pilih cara pemotongan PPh 21, *bulanan (TER)* atau *harian* untuk pegawai tidak tetap yang dibayar harian/mingguan.

## 5. Administrator sistem

- **Cek Kesiapan Go-Live** (Go-Live Check): daftar yang dijawab sistem sendiri (dua super admin, MFA akun berhak tinggi, maker–checker, HTTPS, e-mail, antivirus, monitoring, kelengkapan data payroll) dan yang harus dikonfirmasi manusia (validasi konsultan pajak, pentest, latihan pemulihan, dokumen PDP, pelatihan). **Merah = jangan go-live.**
- **Provisioning (SCIM)**: buat token untuk penyedia identitas (Entra ID, Okta, Google) agar akun dibuat dan dinonaktifkan otomatis; URL dasar tampil di halaman itu. Di bagian **Peran yang dikelola penyedia identitas**, centang peran yang keanggotaannya boleh diatur dari sana (tampil sebagai grup); peran terlindungi tidak bisa dibuka.
- **Alur Persetujuan** (Approval Flows): atur siapa saja yang harus menyetujui cuti atau klaim (atasan, atasan dari atasan, HR), per jenis cuti atau mulai nominal tertentu. Pengajuan yang sudah masuk memakai alur saat diajukan.
- **Periksa dokumen**: slip gaji dan surat keterangan kerja memuat kode verifikasi. Siapa pun (bank, kedutaan) dapat memeriksanya di `/verify` tanpa akun; yang tampil hanya perusahaan, nomor/periode, nama (tersamar untuk slip), dan tanggal.
- **Webhook**: beri tahu sistem lain saat ada kejadian HR (skema: doc 29). **OpenAPI**: `/api/openapi/v1.json` saat masuk, salinan di `docs/openapi-v1.json`.
- Monitoring: `GET /health/metrics` dengan token; aturan alert di `ops/prometheus-alerts.yml` (doc 24).

## 5b. Fitur tambahan (HR dan semua karyawan)

- **Perubahan jabatan terjadwal**: saat mengubah jabatan/departemen/atasan, tanggal berlaku boleh di masa depan (maks. satu tahun). Perubahan menunggu di "Perubahan terjadwal" pada tab Pekerjaan, bisa dibatalkan, dan diterapkan otomatis pada harinya.
- **Foto**: HR dapat mengunggah foto profil karyawan (PNG/JPEG, maks. 1 MB). Saat absen, foto bersifat opsional dan terhapus sendiri setelah 90 hari.
- **Laporan terjadwal**: di **Pengaturan notifikasi** pilih laporan (dashboard HR, kehadiran, cuti, penggajian, persetujuan, tim) dan jadwal mingguan atau bulanan. E-mail hanya berisi tautan, tanpa angka.
- **Cuti hangus diuangkan**: di **Pengaturan Cuti**, jenis cuti dapat diatur agar sisa hari bawaan yang hangus dibayarkan (upah bulanan ÷ 25 × hari) pada pay run berikutnya.
- **Persetujuan bertingkat**: status pengajuan menampilkan kemajuan, misalnya "1/3 · menunggu atasan berikutnya".

## 6. Pertanyaan yang sering muncul

- **"Slip gaji saya belum ada."** Slip terbit saat penggajian *dikunci*, bukan saat dihitung.
- **"Klaim saya sudah disetujui tapi belum dibayar."** Dibayar pada penggajian berikutnya; statusnya menjadi *Dibayar* saat penggajian itu dikunci.
- **"Kenapa diminta kata sandi lagi?"** Data sensitif dan payroll meminta konfirmasi ulang bila sudah lebih dari beberapa menit sejak konfirmasi terakhir.
- **"Saya atasan, tapi tombol Setujui hilang."** Jenis cuti itu perlu HR setelah Anda; status "Disetujui atasan · menunggu HR".
- **"Absen saya bertanda Lokasi belum terkonfirmasi."** Anda memilih "Di kantor" tanpa berbagi lokasi atau di luar radius. Bukan pelanggaran otomatis; HR yang menilai.
