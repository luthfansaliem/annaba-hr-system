# 37 — Perbandingan dengan Mekari Talenta

Disusun 12 November 2026 dari situs resmi Talenta (halaman harga dan halaman fitur) dan dicocokkan dengan kode serta PRD kita (`23-prd-hr-system.md` §0). Talenta tidak dicoba langsung; yang dibandingkan adalah fitur yang mereka iklankan.

## 1. Jawaban singkat

**Belum semua.** Kita setara untuk inti operasional HR yang dijual Talenta di paket **Essential**: data karyawan, absensi, cuti, lembur, penggajian dengan PPh 21 TER dan BPJS, slip gaji, klaim, pinjaman, persetujuan, layanan mandiri, dan laporan. Sebagian fitur paket **Plus** sudah ada (multi perusahaan, payroll mingguan, data 1721-A1), sebagian belum (e-Bupot langsung ke DJP, transfer gaji otomatis, manpower planning). Modul pengembangan talenta di **Talenta 360** (kinerja/KPI, rekrutmen, LMS, suksesi) **belum ada sama sekali**.

**Cara pakainya juga belum sama.** Perbedaan terbesar:

| | Talenta | Annaba HRIS |
|---|---|---|
| Aplikasi karyawan | Aplikasi Android dan iOS | Aplikasi web yang bisa dipasang di layar utama ponsel; belum ada aplikasi di Play Store / App Store |
| Absen | Selfie dengan pengenalan wajah, AI liveness, geotagging, kios tablet, mesin sidik jari tersambung | Tombol absen di web, foto opsional (tanpa pengenalan wajah), lokasi dan radius kantor, impor CSV dari mesin |
| Gaji sampai ke rekening | Transfer massal lewat Virtual Account dari dalam aplikasi | Unduh file bank, transfer di internet banking, lalu tandai sudah dibayar |
| Bukti potong pajak | e-Bupot dibuat dan dikirim ke DJP dari Talenta (lewat Klikpajak) | Unduh data 1721-A1 (CSV) lalu isikan ke Coretax |
| Ekosistem | Tersambung ke Mekari Jurnal, Klikpajak, Sign, Flex, Expense | Webhook, SCIM, dan OpenAPI; belum ada sambungan siap pakai ke aplikasi akuntansi atau pajak |

## 2. Paket Talenta

Harga tidak dipublikasikan; semua lewat tim sales (uji coba gratis 14 hari). Situs pihak ketiga menyebut kisaran Rp10.000–50.000 per karyawan per bulan, tetapi angkanya saling berbeda.

| Paket | Untuk siapa (menurut Talenta) | Yang ditonjolkan |
|---|---|---|
| **Essential** | Perusahaan yang butuh HR inti dan kepatuhan regulasi | Shift dan absensi, payroll, employee self-service |
| **Plus** | Operasi lebih kompleks | Strategic Business Unit (SBU), e-Bupot terintegrasi DJP, Manpower Planning |
| **Talenta 360** (enterprise) | Termasuk pengembangan talenta | AI liveness, performance management, rekrutmen |

Modul tambahan yang dijual terpisah: Rewarding & Recognizing (KPI/OKR, review, kompetensi, suksesi, IDP), Insight & Connectivity (Insight+, Form+), Recruitment (ATS, job portal, AI scoring, MBTI), LMS, Live Tracking, Workflow & Integration (Talenta Tab Portal, absen tanpa sentuh).

Tabel perbandingan di situs mereka tidak bisa dibaca per paket dengan pasti (tabelnya ter-render datar), jadi kolom "Paket" di bawah adalah perkiraan dari teks halaman.

## 3. Per modul

Tanda: **Ada** = sudah ada dan dipakai dengan cara yang setara; **Sebagian** = ada tetapi lebih sempit atau cara pakainya berbeda; **Belum** = tidak ada.

### Administrasi HR

| Fitur Talenta | Paket | Kita | Catatan |
|---|---|---|---|
| Database karyawan terpusat | Essential | **Ada** | Termasuk riwayat jabatan, perubahan terjadwal, NIK/NPWP/rekening terenkripsi |
| Employee self-service | Essential | **Sebagian** | Lengkap di web (HR Saya); belum ada aplikasi mobile native, kunci slip dengan PIN/sidik jari, kalender perusahaan dengan ulang tahun, direktori karyawan |
| Multi level approval | Essential | **Sebagian** | Rantai atasan → atasan dari atasan → HR untuk cuti dan klaim; lembur, koreksi absen, dan surat masih alur tetap |
| Dokumen karyawan | Essential | **Ada** | Dengan versi, tanggal kedaluwarsa, dan pindai virus |
| Template surat + nomor otomatis | Essential | **Sebagian** | Hanya surat keterangan kerja (nomor otomatis, kode verifikasi); belum ada template surat lain yang bisa disusun HR |
| Tanda tangan digital (Mekari Sign) | Add-on | **Belum** | Kita punya kode verifikasi keaslian, bukan tanda tangan elektronik tersertifikasi |
| Onboarding & offboarding (checklist) | Essential | **Ada** | Checklist otomatis saat karyawan masuk/keluar; akun dinonaktifkan otomatis |
| Bagan organisasi | Essential | **Ada** | |
| Pengumuman + acknowledgement | Essential | **Ada** | Target per departemen, laporan siapa belum membaca |
| Form & survei karyawan | Essential (terbatas) | **Belum** | |
| Custom fields | Essential (5) / Plus (tak terbatas) | **Belum** | |
| Custom roles, akses fleksibel | Essential | **Ada** | Peran dan izin bisa diatur; matriks akses; tinjauan akses berkala |
| Multi cabang, SBU, banyak NPWP | Plus | **Sebagian** | Banyak perusahaan dengan NPWP masing-masing dan pay run terpisah; belum ada konsep cabang/SBU di bawah satu perusahaan |
| HR Helpdesk (tiket pertanyaan) | Add-on | **Belum** | |
| Log aktivitas | Essential | **Ada** | Log audit yang tidak bisa diubah, termasuk setiap pembukaan data sensitif |

### Absensi dan waktu kerja

| Fitur Talenta | Paket | Kita | Catatan |
|---|---|---|---|
| Absen mobile | Essential | **Sebagian** | Lewat web di ponsel; bukan aplikasi native |
| Selfie + face recognition | Essential | **Sebagian** | Foto opsional, tanpa pencocokan wajah |
| AI liveness validation | Talenta 360 | **Belum** | |
| Geotagging dan radius lokasi | Essential | **Ada** | Di luar radius ditandai untuk dinilai HR, tidak ditolak |
| Mesin sidik jari tersambung | Essential | **Sebagian** | Impor CSV dari mesin; belum tersambung langsung |
| Kios tablet (Talenta Tab Portal) | Add-on | **Belum** | |
| Manajemen shift, multiple shift | Essential | **Ada** | Termasuk shift lewat tengah malam; belum ada penyusun jadwal rotasi (shift generator) |
| Jam istirahat | Essential | **Belum** | |
| Lembur | Essential | **Ada** | Batas 4 jam/hari, 18 jam/minggu; dibayar otomatis menurut PP 35/2021 |
| Koreksi absen | Essential | **Ada** | |
| Lock attendance | Essential | **Ada** | Tutup bulan |
| Cuti dasar | Essential | **Ada** | Saldo, carry-over dan kedaluwarsanya, cuti hangus diuangkan, setengah hari, kalender cuti |
| Block leave (cuti wajib berturut-turut) | Essential | **Belum** | |
| Cuti per jam | Essential | **Belum** | |
| On-call | Plus | **Belum** | |
| Task & timesheet | Plus | **Belum** | |
| Live tracking (lokasi real-time, rute kunjungan) | Add-on | **Belum** | |

### Penggajian dan keuangan

| Fitur Talenta | Paket | Kita | Catatan |
|---|---|---|---|
| Perhitungan gaji, tunjangan, potongan | Essential | **Ada** | Komponen berulang, baris satu kali, upah harian |
| PPh 21 TER dan PPh 26, PKWT/PKWTT | Essential | **Sebagian** | PPh 21 TER bulanan + penghitungan ulang Desember; PPh 26 belum; **metode gross-up/nett (pajak ditanggung perusahaan) belum ada** |
| BPJS Kesehatan dan Ketenagakerjaan | Essential | **Ada** | Tarif dan batas bisa diubah per tenant, berversi |
| THR, bonus, prorata, pesangon | Essential | **Ada** | Pesangon/UPMK/UPH PP 35/2021 dengan pajak final |
| Struktur gaji dan salary grade | Essential | **Sebagian** | Gaji per karyawan; belum ada tabel grade |
| Compensation management (perencanaan kenaikan) | Plus | **Belum** | |
| Payroll mingguan | Plus | **Ada** | Untuk pekerja harian, dengan potongan sementara untuk pajak/BPJS |
| Jadwal payroll dan cut-off berbeda per entitas | Plus | **Sebagian** | Pay run per perusahaan per bulan kalender; cut-off khusus belum |
| Laporan payroll, pajak, BPJS | Essential | **Ada** | Register, laporan BPJS, jurnal, biaya per departemen, ringkasan pajak, Excel |
| Payroll disbursement (transfer otomatis) | Plus | **Belum** | Hanya file bank |
| e-Bupot ke DJP | Plus | **Belum** | Data 1721-A1 untuk diisikan manual ke Coretax |
| 1721-A1 di akun karyawan | Plus | **Sebagian** | Karyawan mengunduh rekap penghasilan dan PPh 21 setahun; bukan formulir resmi |
| Reimbursement | Essential | **Sebagian** | Klaim dengan bukti, persetujuan, dibayar lewat gaji; belum ada saldo/plafon per kategori per karyawan |
| AI reimbursement (baca struk) | Add-on | **Belum** | |
| Cash advance | Add-on | **Belum** | |
| Pinjaman karyawan | Essential | **Sebagian** | Cicilan otomatis dipotong gaji; belum ada bunga |
| Flexible benefit, earned wage access | Plus / Talenta 360 | **Belum** | Di Talenta lewat produk Mekari Flex |
| Persetujuan payroll | Essential | **Ada** | Pemeriksa wajib orang berbeda; pay run terkunci dibekukan di basis data |

### Akuisisi dan pengembangan talenta (Talenta 360)

| Fitur Talenta | Kita |
|---|---|
| Manpower planning (MPP) | **Belum** |
| Rekrutmen (ATS), integrasi job portal, CV parsing, AI candidate scoring | **Belum** |
| Asesmen, tes MBTI | **Belum** |
| Performance review (360°, self-review, atasan), ringkasan AI | **Belum** |
| KPI dan OKR, goal hierarchy | **Belum** |
| Manajemen kompetensi, 9-box, suksesi, IDP, talent directory | **Belum** |
| LMS (materi, tes, progres, sertifikat) | **Belum** |

### Analitik dan integrasi

| Fitur Talenta | Paket | Kita | Catatan |
|---|---|---|---|
| Laporan komprehensif | Essential | **Ada** | Termasuk laporan terjadwal lewat e-mail |
| Dasbor headcount, turnover, absensi, payroll, lembur | Add-on | **Ada** | Lima halaman analitik sudah termasuk |
| Dasbor kinerja, prediksi karyawan berisiko keluar | Add-on | **Belum** | |
| Custom dashboard, pivot report drag-and-drop | Add-on | **Belum** | |
| AI chatbot / Talenta AI | Add-on | **Belum** | |
| Public API | Add-on | **Sebagian** | OpenAPI untuk pengguna yang masuk, webhook keluar, SCIM; belum ada kunci API untuk sistem lain |
| Integrasi akuntansi/ERP | Add-on | **Sebagian** | Ekspor jurnal seimbang; belum tersambung langsung ke Jurnal/Accurate/ERP |

## 4. Yang kita punya dan tidak ditonjolkan Talenta

- **Kode verifikasi** pada slip gaji dan surat keterangan kerja, bisa diperiksa bank tanpa akun (`/verify`).
- **Pay run terkunci dibekukan di basis data**, bukan hanya di aplikasi; koreksi selalu lewat bulan berikutnya.
- **Perbandingan dengan payroll lama** untuk masa run paralel.
- **SCIM** untuk penyedia identitas (Azure AD, Okta, Google) dan **webhook** bertanda tangan.
- **Data di server sendiri**: tidak ada data karyawan di pihak ketiga, kunci enkripsi milik perusahaan.
- **Cek Kesiapan Go-Live** dan dokumen kepatuhan (PDP, threat model, runbook).

Talenta mungkin punya padanannya di dalam produk; halaman iklannya tidak menyebutnya.

## 5. Usulan urutan kalau ingin menyamai

Diurutkan dari dampak terbesar bagi pengguna sehari-hari dan dari risiko payroll:

1. **Metode gross-up/nett dan PPh 26.** Banyak perusahaan menanggung pajak karyawan; tanpa ini mereka tidak bisa memakai payroll kita.
2. **Aplikasi karyawan** yang terasa native: absen dengan pencocokan wajah, notifikasi push, kunci slip dengan PIN. Bisa dimulai dari aplikasi web yang sekarang (PWA + push) sebelum memutuskan aplikasi Play Store/App Store.
3. **Saldo/plafon klaim per kategori**, cash advance, bunga pinjaman.
4. **Persetujuan bertingkat untuk semua jenis pengajuan** (lembur, koreksi, surat), template surat, custom fields, form/survei sederhana.
5. **Kinerja: KPI/OKR dan review berkala** — modul Talenta 360 yang paling sering dicari.
6. **Rekrutmen sederhana (ATS)** dan manpower planning.
7. Integrasi pihak ketiga yang butuh kontrak: e-Bupot/Coretax lewat PJAP, transfer gaji lewat API bank, tanda tangan elektronik tersertifikasi.
8. LMS, suksesi/9-box, live tracking, AI — terakhir, karena paling jarang menentukan pembelian.

## 6. Batas riset ini

- Sumber hanya situs pemasaran Talenta; detail cara kerja (misalnya aturan lembur yang bisa diatur, mode offline) tidak dijelaskan di sana, jadi tidak dibandingkan.
- Pembagian fitur per paket adalah perkiraan; konfirmasi ke sales Talenta bila dipakai untuk keputusan pembelian atau harga.
- Harga Talenta tidak publik; angka pihak ketiga tidak konsisten.

Sumber: [Harga Talenta](https://www.talenta.co/harga/), [daftar fitur](https://www.talenta.co/fitur/), [Payroll](https://www.talenta.co/fitur/software-payroll/), [Absensi](https://www.talenta.co/fitur/absensi-online/), [Administrasi HR](https://www.talenta.co/fitur/manajemen-administrasi-hr/), [ESS](https://www.talenta.co/fitur/employee-self-service-ess/), [Expense](https://www.talenta.co/fitur/expense-management/), [Talent Development](https://www.talenta.co/fitur/talent-development/), [Talent Acquisition](https://www.talenta.co/fitur/akuisisi-talent/), [HR Analytics](https://www.talenta.co/fitur/hr-analytics/), [Capterra: Talenta pricing](https://www.capterra.com/p/241697/Talenta/pricing/).
