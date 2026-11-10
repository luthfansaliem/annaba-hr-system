# 35 — HR System: Paket Pelatihan Pengguna

Bahan siap pakai untuk pelatih (HR atau tim proyek): jadwal, persiapan, skenario latihan per peran dengan hasil yang diharapkan, kuis singkat, dan lembar tanda terima. Rujukan isi fitur: `30-hr-user-guide.md`. Nama menu ditulis seperti di aplikasi berbahasa Indonesia; dalam kurung adalah nama Inggrisnya.

Prinsip: setiap peserta **mengerjakan sendiri** di lingkungan latihan (staging), bukan menonton. Satu sesi selesai bila setiap peserta menyelesaikan semua skenario perannya.

## 1. Jadwal

| Sesi | Peserta | Lama | Isi |
|---|---|---|---|
| A | Semua karyawan (boleh bergelombang, maks. 25 orang per kelas) | 45 menit | masuk dan MFA, absen, cuti, slip gaji, klaim, profil dan data pribadi, surat keterangan kerja |
| B | Atasan | 45 menit (setelah A) | kotak persetujuan, cuti dan lembur tim, delegasi saat cuti, alur bertingkat |
| C | HR Admin | 2 jam | data karyawan, perubahan jabatan (termasuk bertanggal ke depan), dokumen, pengaturan cuti, shift dan lokasi kerja, koreksi absen, permintaan perubahan data, pengumuman, laporan terjadwal |
| D | Payroll Officer dan Payroll Approver (bersama) | 2,5 jam | kompensasi, komponen berulang, pay run dari hitung sampai kunci, pemisahan tugas, berkas bank, laporan pajak dan BPJS, upah mingguan, perbandingan run paralel |
| E | Administrator sistem | 1 jam | pengguna dan peran, SSO dan provisioning (SCIM), tinjauan akses, jejak audit, Cek Kesiapan Go-Live |

Urutan yang disarankan: E → C → D → B → A. Pelatihan karyawan paling akhir, paling dekat dengan hari mulai.

## 2. Persiapan pelatih (sehari sebelumnya)

1. Lingkungan latihan aktif dan **berisi data contoh, bukan data asli**: minimal 1 perusahaan, 3 departemen, 12 karyawan dengan atasan, kompensasi, dan satu bulan absensi. Impor dari CSV contoh lewat Karyawan (Employees) → Impor.
2. Satu akun latihan per peserta dengan peran yang benar (undang lewat Pengguna → Undang). Untuk sesi D: dua orang berbeda untuk Officer dan Approver.
3. Aturan cuti, hari libur, shift, dan satu lokasi kerja sudah diatur. Untuk sesi B: aktifkan satu aturan di Alur Persetujuan (Approval Flows), misalnya cuti ≥ 5 hari: atasan → atasan dari atasan → HR.
4. Untuk sesi D: satu berkas CSV "payroll lama" untuk latihan perbandingan (kolom `NIK;Bruto;PPh 21;Gaji Bersih`), dengan satu angka sengaja dibuat berbeda.
5. Proyektor, dan setiap peserta membawa laptop atau ponsel. Cetak lembar §5 dan §6.

## 3. Skenario latihan

Format: **langkah** → *hasil yang harus terlihat*. Bila hasil berbeda, catat dan laporkan ke tim proyek.

### Sesi A — Semua karyawan

| # | Langkah | Hasil yang harus terlihat |
|---|---|---|
| A1 | Masuk dengan akun latihan; aktifkan MFA dari Profil → Keamanan | kode cadangan ditampilkan sekali; masuk berikutnya meminta kode |
| A2 | Absensi Saya (My Attendance) → Masuk; pilih mode kerja | jam masuk tercatat; bila memilih "di kantor" peramban meminta izin lokasi |
| A3 | Cuti Saya (My Leave) → ajukan cuti tahunan 2 hari | saldo "menunggu" bertambah; status menunggu atasan; atasan menerima notifikasi |
| A4 | Batalkan pengajuan tadi, lalu ajukan lagi 1 hari | saldo kembali; pengajuan baru muncul |
| A5 | Slip Gaji Saya (My Payslips) → buka slip, unduh PDF | hanya slip dari pay run yang sudah dikunci yang tampil; PDF memuat kode verifikasi di bagian bawah |
| A6 | Buka `/verify`, masukkan kode dari slip dan gaji bersihnya | "Slip gaji asli" dan "Sesuai dengan catatan kami"; nama tampil tersamar |
| A7 | Klaim Saya (My Claims) → ajukan klaim dengan foto kuitansi | klaim menunggu persetujuan; kuitansi bisa dibuka kembali |
| A8 | Profil HR Saya → ubah nomor telepon; lalu ajukan perubahan rekening bank | telepon langsung berubah; rekening menjadi permintaan yang menunggu HR |
| A9 | Surat Keterangan Kerja → minta surat untuk "pengajuan KPR" | permintaan menunggu HR; setelah disetujui PDF bernomor bisa diunduh |
| A10 | Lonceng notifikasi → Pengaturan notifikasi: matikan e-mail untuk satu kategori | pilihan tersimpan; notifikasi dalam aplikasi tetap muncul |

Pesan yang harus sampai: slip dan data pribadi hanya bisa dilihat pemiliknya; rekening tidak bisa diganti tanpa HR; jangan pernah membagikan kata sandi atau kode MFA, juga kepada HR.

### Sesi B — Atasan

| # | Langkah | Hasil yang harus terlihat |
|---|---|---|
| B1 | Kotak Persetujuan (Approvals Inbox) → setujui cuti 1 hari dari A4 | status disetujui; saldo karyawan berkurang; karyawan menerima notifikasi |
| B2 | Minta seorang anggota tim mengajukan cuti 5 hari, lalu setujui | status "1/3": menunggu atasan berikutnya, belum disetujui penuh |
| B3 | Coba setujui pengajuan cuti Anda sendiri | ditolak sistem |
| B4 | Tim Saya (My Team) → lihat kehadiran dan kalender cuti tim | hanya bawahan langsung yang tampil |
| B5 | Delegasi Persetujuan → delegasikan ke rekan selama 3 hari | rekan melihat pengajuan tim Anda selama masa itu; berakhir sendiri |
| B6 | Tolak satu klaim dengan alasan | alasan wajib diisi; karyawan melihat alasannya |

### Sesi C — HR Admin

| # | Langkah | Hasil yang harus terlihat |
|---|---|---|
| C1 | Karyawan → tambah karyawan baru lengkap dengan atasan dan perusahaan | nomor karyawan dibuat; riwayat pekerjaan berisi satu baris |
| C2 | Buka karyawan → lihat NIK dan rekening | diminta kata sandi lagi; pembukaan tercatat di jejak audit |
| C3 | Ubah jabatan dengan tanggal berlaku bulan depan | muncul di "Perubahan terjadwal"; jabatan sekarang belum berubah |
| C4 | Unggah dokumen (kontrak PDF) dan foto karyawan | tersimpan; berkas bukan PDF/PNG/JPEG ditolak |
| C5 | Pengaturan Cuti (Leave Setup) → buat jenis cuti dengan persetujuan HR mulai 3 hari | jenis baru bisa dipilih karyawan |
| C6 | Persetujuan Waktu (Time Approvals) → setujui koreksi absen dan lembur | absensi terkoreksi; lembur masuk ke pay run bulan itu |
| C7 | Permintaan Perubahan (Change Requests) → setujui perubahan rekening dari A8 | diminta kata sandi; data berubah; karyawan diberi tahu |
| C8 | Akhiri hubungan kerja seorang karyawan latihan | akunnya nonaktif saat itu juga; sesi aktifnya terputus |
| C9 | Pengumuman → terbitkan pemberitahuan privasi dengan tanda terima baca | daftar siapa yang sudah membaca terlihat |
| C10 | Pengaturan notifikasi → jadwalkan laporan kehadiran mingguan | e-mail berisi tautan (tanpa angka) tiba pada jadwalnya |

### Sesi D — Payroll Officer dan Approver

| # | Siapa | Langkah | Hasil yang harus terlihat |
|---|---|---|---|
| D1 | Officer | Kompensasi → atur gaji pokok dan tunjangan satu karyawan, berlaku awal bulan | riwayat kompensasi bertambah; nilai lama tetap tersimpan |
| D2 | Officer | Pay Run → buat untuk bulan berjalan → Hitung | setiap karyawan punya baris; peringatan muncul bila data kurang (NPWP, rekening) |
| D3 | Officer | Tambah baris bonus untuk satu karyawan → Hitung ulang | bruto, PPh 21, dan gaji bersih berubah |
| D4 | Officer | Coba Setujui pay run sendiri | ditolak: penyusun tidak boleh menyetujui |
| D5 | Approver | Periksa, lalu Setujui dan Kunci | status terkunci; tidak ada lagi tombol ubah |
| D6 | Officer | Coba ubah baris setelah terkunci | ditolak |
| D7 | Approver | Unduh berkas bank; Officer unduh register, laporan BPJS, jurnal | berkas terunduh; tercatat di jejak audit |
| D8 | Officer | Bandingkan dengan payroll lama → unggah CSV latihan | satu karyawan "Berbeda" dengan selisih yang tepat; unduh selisih |
| D9 | Keduanya | Pembayaran Mingguan → Officer menyiapkan, Approver menyetujui | setelah disetujui tidak bisa diubah; dipotong di pay run bulanan |
| D10 | Officer | Laporan → data 1721-A1 untuk tahun lalu | CSV per karyawan terunduh |

Pesan yang harus sampai: angka diperbaiki di **data atau aturan**, lalu dihitung ulang, tidak pernah diedit di hasil; pay run terkunci tidak bisa dibuka, koreksi masuk ke bulan berikutnya.

### Sesi E — Administrator sistem

| # | Langkah | Hasil yang harus terlihat |
|---|---|---|
| E1 | Pengguna → undang pengguna dengan peran HR Admin | undangan terkirim; peran terlihat setelah diterima |
| E2 | Peran → lihat izin Payroll Officer dan Payroll Approver | Officer tidak punya izin setujui dan kunci |
| E3 | Provisioning (SCIM) → buat token; buka satu peran untuk dikelola penyedia identitas | token tampil sekali; peran terlindungi tidak bisa dibuka |
| E4 | Jejak Audit → cari "payroll" hari ini | tindakan sesi D terlihat dengan pelakunya |
| E5 | Cek Kesiapan Go-Live | daftar hijau/merah; yang merah menjadi daftar kerja |
| E6 | Sesi → akhiri sesi seorang pengguna | pengguna itu langsung keluar |

## 4. Kuis singkat (5 menit di akhir tiap sesi)

1. Siapa saja yang dapat melihat slip gaji Anda? *(Anda sendiri dan petugas penggajian.)*
2. Anda mengganti rekening bank di profil. Kapan gaji masuk ke rekening baru? *(Setelah HR menyetujui permintaannya.)*
3. Atasan sedang cuti dan ada pengajuan mendesak. Apa yang seharusnya sudah dilakukan atasan? *(Mendelegasikan persetujuan.)*
4. (Payroll) Pay run sudah terkunci dan ada tunjangan terlewat. Apa yang dilakukan? *(Penyesuaian di pay run bulan berikutnya; yang terkunci tidak dibuka.)*
5. (Payroll) Mengapa penyusun tidak bisa menyetujui pay run-nya sendiri? *(Pemisahan tugas: mencegah kesalahan dan penyalahgunaan oleh satu orang.)*
6. (HR) Mengapa sistem meminta kata sandi lagi saat membuka NIK? *(Data peka: memastikan yang membuka memang Anda, dan pembukaan dicatat.)*
7. Bank meminta bukti slip gaji asli. Bagaimana bank memeriksanya? *(Kode verifikasi di slip, lewat halaman periksa dokumen.)*
8. Ada e-mail yang meminta kata sandi atau kode MFA Anda "dari HR". Apa yang Anda lakukan? *(Tidak memberikannya dan melaporkannya; HR tidak pernah memintanya.)*

## 5. Lembar hadir dan tanda terima

| No | Nama | Unit | Sesi | Semua skenario selesai (ya/tidak) | Pemberitahuan privasi sudah dibaca | Tanda tangan |
|---|---|---|---|---|---|---|
| | | | | | | |

## 6. Catatan pelatih

| Sesi | Tanggal | Jumlah peserta | Skenario yang bermasalah | Pertanyaan yang sering muncul | Tindak lanjut |
|---|---|---|---|---|---|
| | | | | | |

Setelah semua sesi: kirim rekap ke pemilik sistem. Go-live mensyaratkan sesi C, D, dan E selesai 100 % dan sesi A minimal 90 % karyawan (sisanya menyusul dalam dua minggu, dengan panduan `30-hr-user-guide.md`).
