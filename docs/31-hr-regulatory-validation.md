# 31 — Validasi Aturan Payroll terhadap Sumber Resmi

Status per 2026-11-10. Dokumen ini mencatat **apa yang sudah dicocokkan dengan sumber**, **apa yang dikoreksi**, dan **apa yang tetap perlu diputuskan konsultan pajak/HR-Legal**. Ini bukan nasihat pajak; tanda tangan konsultan tetap diperlukan sebelum run pertama (lihat PRD §11).

Cara membaca kolom *Status*: **Cocok** = dibandingkan dengan teks/lampiran peraturan · **Dikoreksi** = implementasi diubah setelah riset · **Asumsi** = belum ada sumber yang tegas; perlu keputusan.

## 1. PPh Pasal 21

| Aturan | Implementasi | Status | Sumber |
|---|---|---|---|
| TER bulanan kategori A/B/C | `PayrollRules.DefaultTerA/B/C`: 44 / 40 / 41 lapisan | **Cocok.** Seluruh 125 tarif dan batas lapisan dibandingkan dengan Lampiran PP 58/2023 (PDF resmi dirender lalu di-OCR; urutan tarif identik di ketiga tabel; batas yang tidak terbaca OCR diperiksa dengan mata pada halaman 1, 2, 5, 8, 11, 12, 14). Tes `The_built_in_rules_follow…` menjaga jumlah lapisan dan kenaikannya | [Lampiran PP 58/2023](https://www.pajak.go.id/sites/default/files/lampiran/Lampiran%20PP58TAHUN2023.pdf) |
| Kategori TER dari status PTKP | A = TK/0, TK/1, K/0 · B = TK/2, TK/3, K/1, K/2 · C = K/3 | **Cocok** | [pajak.go.id: Simpel Pakai TER](https://www.pajak.go.id/id/artikel/simpel-pakai-ter-begini-penjelasannya) |
| TER harian (pegawai tidak tetap dibayar harian/mingguan/satuan) | ≤ Rp450.000/hari: 0% · > Rp450.000–Rp2.500.000: 0,5% × bruto · > Rp2.500.000: tarif Pasal 17 × 50% × bruto. Rata-rata per hari untuk upah mingguan | **Dikoreksi (baru).** Sebelumnya pekerja harian selalu memakai TER bulanan. Sekarang ada pilihan per kompensasi: *dibayar sebulan sekali* (TER bulanan) atau *dibayar harian/mingguan* (TER harian) | Lampiran PP 58/2023 huruf D; [DDTC: PPh 21 pegawai tidak tetap upah harian](https://news.ddtc.co.id/berita/nasional/1816737/penghitungan-pph-pasal-21-atas-pegawai-tidak-tetap-dengan-upah-harian) (Pasal 12 PMK 168/2023) |
| Pegawai tidak tetap dibayar bulanan | TER bulanan atas bruto sebulan | **Cocok** | idem |
| Penghitungan ulang masa pajak terakhir (Desember / bulan berhenti) | Pasal 17 atas PKP setahun − PPh yang sudah dipotong; hanya untuk pegawai tetap | **Dikoreksi.** Sebelumnya juga diterapkan pada pekerja harian; sekarang pegawai tidak tetap tetap di TER, termasuk Desember | [DDTC: PPh 21 pegawai tidak tetap dihitung pakai TER, termasuk Desember](https://news.ddtc.co.id/berita/nasional/1800256/pph-21-pegawai-tidak-tetap-dihitung-pakai-ter-termasuk-desember) |
| Pegawai tetap yang masuk/keluar di tengah tahun | Penghasilan neto **tidak** disetahunkan, PTKP **setahun penuh** | **Dikoreksi.** Sebelumnya PTKP diprorata per bulan bekerja, sehingga pajak Desember terlalu besar bagi yang masuk tengah tahun. Contoh uji: masuk 1 September, Rp16 juta/bulan, TK/0 → Desember lebih potong Rp2.960.000 | [Ortax: PPh 21 karyawan masuk pertengahan tahun](https://ortax.org/menghitung-pph-21-karyawan-yang-masuk-pada-pertengahan-tahun); UU PPh Pasal 7 |
| Penyetahunan neto (kewajiban subjektif mulai/berakhir di tengah tahun, umumnya WNA) | **Tidak didukung** | **Asumsi/terbuka.** Untuk kasus ini hitung manual atau minta konsultan | idem |
| PTKP | TK/0 54 jt; +4,5 jt per tanggungan (maks 3); K +4,5 jt | **Cocok** (PMK 101/PMK.010/2016, belum berubah) | — |
| Biaya jabatan | 5% bruto, maks Rp500.000 × bulan bekerja | **Cocok** | [pajak.go.id: Bukti Potong A1](https://pajak.go.id/sites/default/files/2025-12/Pembuatan%20Bukti%20Pemotongan%20PPh%20Pasal%2021-Tahunan%20A1%20%20%281%29.pdf) (Pasal 5 PMK 168/2023) |
| Tarif Pasal 17 | 5/15/25/30/35% pada 60 jt / 250 jt / 500 jt / 5 M | **Cocok** (UU HPP) | — |
| Tanpa NPWP | +20% | **Asumsi.** Ketentuan UU PPh Pasal 21 ayat (5a) masih ada; sejak NIK berlaku sebagai NPWP, praktiknya jarang terpakai. Konfirmasi ke konsultan | — |
| Premi yang dibayar pemberi kerja masuk bruto | BPJS Kesehatan (4%), JKK, JKM ditambahkan ke penghasilan bruto; JHT/JP pemberi kerja tidak | **Cocok** dengan praktik PMK 168/2023; iuran JHT/JP karyawan menjadi pengurang di masa pajak terakhir | — |
| Bukti potong tahunan | Ekspor **Data 1721-A1** (kolom mengikuti urutan formulir: gaji, tunjangan/lembur, premi pemberi kerja, bonus/THR, bruto, biaya jabatan, iuran pensiun, neto, PTKP, PKP, PPh setahun, PPh dipotong) | **Sebagian.** Datanya ada; berkas XML Coretax (BPA1/BPMP) harus dibuat dengan templat/konverter resmi DJP, karena struktur XML-nya tidak dipublikasikan sebagai spesifikasi yang bisa saya verifikasi | [pajak.com: templat XML Coretax](https://www.pajak.com/pajak/perubahan-impor-data-di-core-tax-unduh-template-xml-di-sini/), [Ortax: bukti potong A1 di Coretax](https://ortax.org/simak-cara-pembuatan-bukti-potong-a1-di-coretax) |

## 2. BPJS

| Aturan | Implementasi | Status | Sumber |
|---|---|---|---|
| Batas upah Jaminan Pensiun | Mengikuti periode: Mar 2023 Rp9.559.600 · Mar 2024 Rp10.042.300 · Mar 2025 Rp10.547.400 · **Mar 2026 Rp11.086.300** | **Dikoreksi.** Sebelumnya satu angka (11.086.300) dipakai untuk semua bulan; sekarang aturan bawaan memilih batas sesuai bulan gaji (`PayrollRules.BuiltInFor`). **Setiap Maret harus dibuat versi aturan baru** di halaman Payroll Rules begitu BPJS mengumumkan angkanya | [ANTARA: batasan upah JP 2026](https://jateng.antaranews.com/berita/625410/bpjs-ketenagakerjaan-sosialisasikan-batasan-upah-jaminan-pensiun), [BSC: surat B/1226/022026](https://bsc.co.id/ideas/batas-upah-jaminan-pensiun-bpjs-maret-2026/) |
| Tarif JP / JHT / JKM / JKK | JP 2% + 1% · JHT 3,7% + 2% · JKM 0,3% · JKK 0,24% (bawaan; 0,24–1,74% per kelompok risiko, diatur per karyawan) | **Cocok** | idem; PP 44/2015, PP 45/2015, PP 46/2015 |
| BPJS Kesehatan | 4% + 1%, batas upah Rp12.000.000 | **Cocok** (Perpres 64/2020) | — |
| Batas bawah upah (UMK) sebagai dasar iuran | **Tidak dicek** | **Terbuka.** Sistem tidak menyimpan UMK per lokasi; pastikan upah yang dimasukkan ≥ UMK | — |
| Iuran atas rapel (kenaikan gaji berlaku surut) | Selisih gaji dibayar sebagai penghasilan kena pajak bulan berjalan; **iuran BPJS bulan lampau tidak dibuka ulang** | **Asumsi.** Tidak ditemukan aturan tegas. Pedoman BPJS Ketenagakerjaan: iuran mengikuti upah yang dilaporkan, jadi laporkan upah baru sejak bulan berlakunya; untuk kekurangan bulan lampau tanyakan ke kantor cabang BPJS | [BPJS Ketenagakerjaan: data gaji tidak sesuai](https://www.bpjsketenagakerjaan.go.id/artikel/18964/artikel-apa-yang-harus-dilakukan-jika-data-gaji-di-bpjs-ketenagakerjaan-tidak-sesuai.bpjs) |
| Pekerja harian | Iuran atas upah yang benar-benar diterima bulan itu | **Asumsi.** Banyak perusahaan memakai upah sebulan = upah harian × hari kerja sebulan (mis. 25) atau minimal UMK | — |

## 3. Ketenagakerjaan (dari teks peraturan yang dikenal; belum dibandingkan ulang baris per baris pada riset ini)

| Aturan | Implementasi | Status |
|---|---|---|
| Upah lembur (PP 35/2021 Pasal 31; 5 hari kerja) | Upah sejam = 1/173 upah sebulan · hari kerja: jam pertama 1,5×, berikutnya 2× · hari istirahat/libur: 8 jam pertama 2×, jam ke-9 3×, jam ke-10 dst. 4× · batas 4 jam/hari, 18 jam/minggu | **Asumsi yang kuat**; pola 6 hari kerja belum didukung |
| Upah lembur pekerja harian | Upah sebulan = 25 × upah sehari (6 hari kerja) | **Asumsi.** Kepmenaker 102/2004: 25 untuk 6 hari kerja, **21 untuk 5 hari kerja**; sistem selalu memakai 25 |
| THR (Permenaker 6/2016) | ≥ 12 bulan: 1 bulan upah · 1–<12 bulan: proporsional | **Asumsi yang kuat** |
| Pesangon, UPMK, UPH (PP 35/2021) + PPh final pesangon (PP 68/2009: 0% s.d. 50 jt, 5%, 15%, 25%) | Kalkulator pesangon dengan pengali per alasan PHK | **Asumsi yang kuat**; pengali per alasan wajib dicek konsultan |
| Upah mingguan | Pembayaran di muka per minggu, diselesaikan di penggajian bulanan; pilih *TER harian* pada kompensasi agar pajaknya mengikuti aturan pegawai tidak tetap | **Asumsi model**; lihat doc 23 batch 7 |

## 4. Yang harus diputuskan konsultan (ringkas)

1. Tanda tangan atas tabel bawaan (bagian 1–2) dan versi aturan untuk tahun berjalan.
2. Tanpa-NPWP +20%: masih diterapkan atau tidak.
3. Dasar iuran BPJS pekerja harian dan iuran atas rapel.
4. Pengali lembur pekerja harian untuk 5 hari kerja (21) dan pola 6 hari kerja.
5. Pengali pesangon per alasan PHK untuk kasus perusahaan.
6. Penyetahunan untuk karyawan yang kewajiban pajak subjektifnya tidak setahun penuh.
7. Templat Coretax yang dipakai untuk BPA1/BPMP dan pemetaan kolom dari ekspor *Data 1721-A1*.
