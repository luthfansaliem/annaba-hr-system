# 34 — HR System: Paket Pelindungan Data Pribadi (dasar pemrosesan, pemberitahuan privasi, DPIA)

Dokumen kerja untuk penanggung jawab data pribadi dan penasihat hukum. **Bukan nasihat hukum.** Isinya: usulan keputusan yang tinggal disetujui atau diubah, bukan halaman kosong. Setiap usulan menyebut pasal yang dipakai supaya mudah diperiksa. Rujukan: UU No. 27 Tahun 2022 tentang Pelindungan Data Pribadi (UU PDP), berlaku penuh sejak 17 Oktober 2024. Peraturan pelaksana dan lembaga pengawasnya masih berproses; periksa perkembangannya sebelum menandatangani.

Lembar kerja pendahulunya (catatan pemrosesan, hak subjek data, insiden) ada di `26-hr-pdp-compliance.md`.

## 1. Tiga keputusan yang diminta: lokasi, agama, foto

### 1.1 Lokasi saat absen

| | |
|---|---|
| Yang diproses | Koordinat saat menekan "masuk" atau "pulang", hanya bila perusahaan sudah mendaftarkan lokasi kerja **dan** karyawan memilih "di kantor". Disimpan dibulatkan; HR melihatnya sebagai tanda "di luar zona", bukan sebagai peta. Menolak berbagi lokasi tetap bisa absen (dicatat sebagai tanda). Tidak ada pelacakan terus-menerus. |
| Jenis data | Data pribadi umum (tidak termasuk daftar data spesifik di Pasal 4 ayat (2)). |
| **Usulan dasar pemrosesan** | **Kepentingan yang sah** pengendali, Pasal 20 ayat (2) huruf f: memastikan kehadiran di tempat kerja untuk karyawan yang wajib hadir di kantor. Alternatif yang juga dapat dipertahankan: pemenuhan kewajiban perjanjian (huruf b), bila kewajiban hadir di lokasi tertulis di perjanjian kerja atau peraturan perusahaan. |
| Mengapa bukan persetujuan | Persetujuan harus bisa ditolak dan ditarik tanpa akibat (Pasal 9, Pasal 22–24). Dalam hubungan kerja posisi karyawan tidak setara, dan absensi tetap harus berjalan bila persetujuan ditarik. Dasar yang bergantung pada persetujuan mudah gugur. |
| Uji keseimbangan (wajib untuk huruf f) | Tujuan sah dan nyata; data seminimal mungkin (satu titik per absen, dibulatkan, hanya untuk mode kantor); ada cara lain bagi yang menolak; dampak ke karyawan rendah; karyawan diberi tahu di pemberitahuan privasi. Simpan uji ini secara tertulis. |
| Syarat pendamping | Tulis di peraturan perusahaan; batasi retensi (usulan: 12 bulan, lalu koordinat dihapus dan hanya tanda zona yang tersisa); jangan dipakai untuk tujuan lain (disiplin di luar absensi, pemantauan pergerakan). |

Catatan teknis: alamat IP saat absen juga tersimpan. Perlakukan sama: tujuan pembuktian absensi, retensi yang sama.

### 1.2 Agama

| | |
|---|---|
| Yang diproses | Kolom pilihan (boleh kosong) pada data karyawan. Dipakai untuk merencanakan tanggal pembayaran THR keagamaan sesuai hari raya masing-masing. |
| Jenis data | **Data pribadi umum**: Pasal 4 ayat (3) menyebut agama secara tegas bersama nama, jenis kelamin, kewarganegaraan, dan status perkawinan. (Lembar 26 versi lama menyebutnya data spesifik; itu keliru dan sudah diperbaiki.) |
| **Usulan dasar pemrosesan** | **Pemenuhan kewajiban hukum** pengendali, Pasal 20 ayat (2) huruf c: THR keagamaan wajib dibayar paling lambat tujuh hari sebelum hari raya keagamaan pekerja (Permenaker No. 6 Tahun 2016), sehingga perusahaan perlu tahu hari raya yang berlaku bagi tiap pekerja. |
| Perlakuan yang disarankan | Walau tergolong umum, perlakukan sebagai data peka karena risiko diskriminasi: hanya HR yang melihat; tidak tampil di direktori, laporan, atau ekspor umum; tidak dipakai untuk keputusan lain apa pun; dihapus saat anonimisasi (sudah demikian di sistem). Bila perusahaan membayar THR semua orang pada satu tanggal yang sama, **jangan kumpulkan sama sekali**: kosongkan kolomnya. |

### 1.3 Foto karyawan dan foto saat absen

| | |
|---|---|
| Yang diproses | Foto profil (opsional, diunggah HR) dan foto saat absen (opsional per absen; dihapus otomatis setelah 90 hari). Sistem **tidak** melakukan pencocokan wajah otomatis. |
| Jenis data | Hati-hati: Penjelasan Pasal 4 ayat (2) huruf b memberi contoh "gambar wajah" untuk data biometrik. Sampai penasihat hukum menyatakan lain, **perlakukan foto sebagai data spesifik**. |
| **Usulan dasar pemrosesan** | Foto profil: kepentingan yang sah (huruf f) untuk pengenalan rekan kerja, dengan hak menolak. Foto absen: hanya bila ada kebutuhan nyata (pekerja lapangan, risiko titip absen), dengan dasar kepentingan yang sah dan uji keseimbangan tertulis; bila tidak ada kebutuhan itu, **jangan diwajibkan**. |
| Akibatnya | Karena diperlakukan spesifik: masuk DPIA (§3), akses dibatasi dan setiap kali melihat foto absen orang lain tercatat di jejak audit (sudah demikian), retensi 90 hari dipertahankan, dan tidak boleh ditambah pengenalan wajah tanpa DPIA baru. |

## 2. Draf Pemberitahuan Privasi Karyawan

Untuk dibagikan sebelum sistem dipakai (Pasal 21: informasi yang wajib disampaikan saat memproses). Di aplikasi: terbitkan sebagai Pengumuman dengan tanda terima baca, sehingga ada bukti tiap karyawan sudah menerimanya. Isi bagian dalam kurung siku.

> **Pemberitahuan Privasi untuk Karyawan [Nama Perusahaan]**
>
> **Siapa yang memproses data Anda.** [Nama badan hukum], [alamat], sebagai pengendali data pribadi. Pertanyaan dan permintaan: [nama/jabatan penanggung jawab data pribadi], [e-mail].
>
> **Data apa dan untuk apa.**
> - Identitas dan kontak (nama, tanggal lahir, alamat, telepon, kontak darurat): administrasi hubungan kerja.
> - NIK, NPWP, nomor BPJS, rekening bank: penggajian, pemotongan dan pelaporan pajak, kepesertaan BPJS.
> - Gaji, tunjangan, status PTKP, pinjaman, klaim: penghitungan dan pembayaran upah.
> - Cuti dan kehadiran, termasuk jam masuk/pulang dan alamat IP: administrasi waktu kerja dan cuti.
> - Lokasi saat absen: hanya bila Anda memilih "di kantor"; untuk memastikan kehadiran di lokasi kerja. Kami tidak melacak posisi Anda di luar saat absen.
> - Foto: foto profil (opsional; Anda boleh meminta HR menghapusnya) [dan foto saat absen untuk …]. Kami tidak memakai pengenalan wajah otomatis.
> - Agama (boleh tidak diisi): hanya untuk menentukan tanggal THR keagamaan Anda.
> - Dokumen yang Anda atau HR unggah (kontrak, ijazah, surat dokter, bukti klaim).
>
> **Dasar kami memprosesnya.** Pelaksanaan perjanjian kerja; kewajiban hukum (perpajakan, BPJS, ketenagakerjaan); dan kepentingan yang sah perusahaan untuk mengelola kehadiran dan keamanan sistem. Kami tidak menjual data Anda dan tidak memakainya untuk iklan.
>
> **Siapa yang dapat melihat.** Anda sendiri; atasan langsung (cuti, kehadiran, klaim tim); HR; petugas penggajian (hanya data gaji); administrator sistem (mengelola akun dan peran). Setiap kali data peka dibuka (NIK, NPWP, rekening, gaji), hal itu tercatat.
>
> **Pihak lain yang menerima.** Direktorat Jenderal Pajak, BPJS Kesehatan dan BPJS Ketenagakerjaan, bank pembayar gaji, serta penyedia hosting dan e-mail kami ([nama, lokasi server]) sebagai prosesor berdasarkan perjanjian. [Bila server di luar Indonesia: sebutkan negara dan dasar transfernya, Pasal 56.]
>
> **Berapa lama disimpan.** Selama Anda bekerja dan [10] tahun sesudahnya untuk dokumen pajak dan penggajian; foto saat absen 90 hari; koordinat absen [12 bulan]. Setelah itu data dihapus atau dianonimkan.
>
> **Hak Anda.** Mendapat informasi dan salinan data Anda (menu Profil HR → Unduh data saya); memperbaiki data yang keliru; meminta penghapusan bila tidak lagi diperlukan dan tidak ada kewajiban hukum untuk menyimpannya; menolak atau membatasi pemrosesan tertentu; menarik persetujuan untuk hal yang berdasar persetujuan; dan mengajukan keberatan kepada kami atau kepada lembaga pelindungan data pribadi. Kami menjawab dalam [3 x 24 jam] sesuai ketentuan.
>
> **Keamanan.** Data peka dienkripsi; akses dibatasi menurut peran; tindakan penting memerlukan verifikasi ulang kata sandi; ada jejak audit. Bila terjadi kegagalan pelindungan data yang menyangkut Anda, kami memberi tahu Anda secara tertulis paling lambat 3 x 24 jam.
>
> Berlaku sejak [tanggal]. Perubahan akan diumumkan di aplikasi.

## 3. Draf Penilaian Dampak (DPIA)

Pasal 34 mewajibkan penilaian dampak bila pemrosesan berisiko tinggi; sistem ini memproses data spesifik (keuangan pribadi; data kesehatan pada surat sakit; foto bila diperlakukan biometrik), jadi DPIA diperlukan. Skala: K = kemungkinan, D = dampak, masing-masing rendah/sedang/tinggi.

**Gambaran pemrosesan.** Sistem HR internal untuk [jumlah] karyawan [nama perusahaan]; modul data karyawan, cuti, kehadiran, penggajian, klaim, dokumen. Diakses lewat peramban; server di [lokasi]; prosesor: [hosting], [e-mail]. Tidak ada pengambilan keputusan otomatis yang berdampak hukum bagi karyawan (penggajian dihitung sistem tetapi disetujui dua orang).

**Kebutuhan dan proporsionalitas.** Setiap data punya tujuan dan dasar (§1 dan lembar 26). Yang opsional memang bisa dikosongkan (agama, foto, lokasi). Retensi ditetapkan per jenis data.

| # | Risiko bagi karyawan | K | D | Pengendalian yang sudah ada | Sisa risiko | Tindak lanjut |
|---|---|---|---|---|---|---|
| 1 | Gaji atau rekening bocor dari basis data atau cadangan | rendah | tinggi | NIK/NPWP/rekening/gaji pokok terenkripsi per kolom; cadangan terenkripsi; peran basis data hak-minimum; audit setiap akses | rendah | aktifkan enkripsi disk di server; uji pemulihan bulanan |
| 2 | Orang dalam melihat data yang bukan urusannya | sedang | sedang | izin per peran; atasan hanya timnya; verifikasi ulang kata sandi; setiap pembukaan data peka tercatat | rendah | tinjau akses per kuartal (fitur Access Review) |
| 3 | Rekening diganti lewat akun yang dibajak (penipuan gaji) | rendah | tinggi | perubahan rekening berupa permintaan yang disetujui HR; verifikasi ulang; MFA; notifikasi ke karyawan | rendah | wajibkan MFA untuk HR dan payroll |
| 4 | Lokasi dipakai untuk memantau karyawan | rendah | sedang | satu titik per absen, hanya mode kantor, dibulatkan, HR hanya melihat tanda | rendah | tetapkan retensi 12 bulan; larang pemakaian lain di peraturan perusahaan |
| 5 | Foto dipakai untuk pengenalan wajah atau tersebar | rendah | sedang | tanpa pencocokan otomatis; foto absen dihapus 90 hari; akses tercatat | rendah | putuskan apakah foto absen memang perlu |
| 6 | Diskriminasi berdasarkan agama atau data kesehatan | rendah | tinggi | agama opsional dan hanya HR; alasan sakit pendek; dokumen hanya HR dan pemilik | rendah | kosongkan agama bila THR satu tanggal |
| 7 | Data bekas karyawan tersimpan selamanya | sedang | rendah | anonimisasi otomatis setelah masa retensi (dimatikan sampai diputuskan) | sedang | tetapkan masa retensi, lalu aktifkan |
| 8 | Dokumen palsu atas nama perusahaan (slip atau surat) | sedang | sedang | kode verifikasi di setiap slip dan surat; halaman periksa publik tanpa menampilkan nominal | rendah | beri tahu bank rekanan cara memeriksanya |
| 9 | Prosesor (hosting, e-mail) lalai | rendah | tinggi | e-mail tidak memuat nominal gaji; data peka terenkripsi di aplikasi | sedang | tanda tangani perjanjian pemrosesan data; catat lokasi server |
| 10 | Insiden tidak diketahui atau terlambat dilaporkan | sedang | tinggi | peringatan otomatis, jejak audit, runbook insiden | sedang | tunjuk pemilik insiden; latihan sekali sebelum go-live |

**Kesimpulan draf.** Dengan pengendalian di atas, sisa risiko rendah sampai sedang dan dapat diterima, **dengan syarat** tindak lanjut di kolom terakhir selesai. Tidak ada risiko tinggi yang tersisa yang mengharuskan konsultasi dengan lembaga sebelum pemrosesan.

Disusun oleh: [nama] · Ditinjau penasihat hukum: [nama, tanggal] · Disetujui: [pemilik sistem, tanggal] · Tinjau ulang: setiap tahun atau bila ada fitur baru yang memproses data pribadi.

## 4. Yang masih harus diputuskan orang (isi lalu tandatangani)

| Keputusan | Usulan | Diputuskan oleh | Tanggal |
|---|---|---|---|
| Dasar pemrosesan lokasi absen | kepentingan yang sah + uji keseimbangan tertulis (§1.1) | | |
| Agama dikumpulkan atau tidak | hanya bila tanggal THR berbeda per agama (§1.2) | | |
| Foto absen dipakai atau tidak | tidak, kecuali ada kebutuhan nyata (§1.3) | | |
| Masa retensi data karyawan setelah berhenti | 10 tahun untuk pajak dan penggajian; konfirmasi konsultan pajak | | |
| Retensi koordinat absen | 12 bulan | | |
| Penanggung jawab data pribadi (Pasal 53) | tunjuk satu orang walau belum tentu wajib | | |
| Perjanjian pemrosesan dengan hosting dan e-mail | wajib sebelum go-live (Pasal 51) | | |
| Lokasi server dan dasar transfer bila di luar negeri | catat; Pasal 56 | | |
| Pemilik insiden dan kontak hukum | dua nama, dua nomor telepon | | |

Sumber: [UU 27/2022 di JDIH BPK](https://peraturan.bpk.go.id/Details/229798/uu-no-27-tahun-2022) · [teks di Wikisource](https://id.wikisource.org/wiki/Undang-Undang_Republik_Indonesia_Nomor_27_Tahun_2022) · [catatan untuk tim HR, Hukumonline](https://www.hukumonline.com/berita/a/poin-poin-penting-bagi-tim-hr-dalam-melaksanakan-isi-uu-pdp-lt65587caf9a370) · [anotasi putusan MK atas Pasal 53](https://s.mkri.id/public/content/infoumum/undang/pdf/Anotasi_151_UU%20No.%2027%20thn%202022%20PDP%20-hype.pdf).
