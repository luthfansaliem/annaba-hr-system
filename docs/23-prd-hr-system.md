# 23 — PRD: Annaba HR System (Production-Ready)

Versi: 1.0 · Status: Draft untuk persetujuan PO · Tanggal: 2026-11-10
Bahasa dokumen: Indonesia (istilah teknis tetap Inggris). Dokumen ini **melengkapi** `01-prd.md` (PRD Identity & Access), bukan menggantikannya. Semua requirement IAM (AUTH, USR, ROLE, PERM, SES, AUD, SEC, SET, NAV, UX, NFR-SEC) tetap berlaku dan menjadi fondasi HR System.

Prioritas: **P0** = wajib untuk go-live, **P1** = wajib dalam 90 hari setelah go-live, **P2** = roadmap.
Catatan hukum: aturan ketenagakerjaan, pajak, dan BPJS di bawah adalah *baseline* per tanggal dokumen dan **harus divalidasi oleh HR/Legal/Konsultan Pajak** sebelum go-live. Semua nilai bersifat konfigurasi, bukan hard-code.

## 0. Status Implementasi (diperbarui 2026-11-10)

| Fase | Status | Bukti |
|---|---|---|
| 0 Fondasi | **Sebagian**: permission `employees.*`/`hrconfig.manage`, role `hr_admin` & `hr_head`, migrasi `HrCore`, enkripsi field (AES-GCM + AAD per baris/kolom). Belum: workflow engine, observability | `api/src/AnnabaDigital.Domain/Security/Permissions.cs`, migrasi `*_HrCore` |
| 1 Core HR | **Selesai sebagian**: EMP-001/002/003/004/007 (daftar, cari, filter, ekspor CSV aman formula), departemen (ADM-001 sebagian), offboarding dasar (ONB-003: nonaktifkan akun + cabut sesi). Belum: EMP-005 dokumen, EMP-006 impor massal, ONB-001/002/005 | 9 tes integrasi `HrEmployeeTests` + 346 tes API lain lulus; UI diverifikasi di browser |
| 2 Cuti & Absensi | **Selesai sebagian**: LVE-001/003/004(1 level)/005/006/007(libur)/009 sebagian, ATT-002/006 (clock web + laporan bulanan). Grant cuti tahunan otomatis setelah 12 bulan; ledger append-only; pending direservasi; maker≠approver; 6 permission baru + role `manager`. Belum: kalender tim, delegasi/eskalasi, notifikasi email, koreksi absen, lembur, shift, impor mesin, penutupan periode (ATT-003/004/005/008) | 9 tes integrasi `HrLeaveTests` (termasuk 2 tes konkurensi); UI diverifikasi di browser (ajukan → approve, absen) |
| 3 Payroll | **Selesai sebagian**: PAY-001/002/003/004/005 (sebagian)/007/008/009/010/011. Kompensasi berperiode & terenkripsi; PPh 21 TER bulanan + true-up Desember/bulan terakhir (PTKP prorata, NPWP +20%); BPJS Kes/JHT/JP/JKK/JKM dengan cap; prorata masuk/keluar; potongan cuti tanpa gaji; baris tambahan (bonus/THR/potongan); alur Draft→Dihitung→Disetujui→Dikunci→Dibayar; maker≠checker; trigger database mengunci pay run; slip gaji; file bank & register CSV; peringatan anomali. Belum: lembur, alpa, THR/pesangon otomatis, pinjaman, retro, bukti potong 1721-A1/e-Bupot, jurnal akuntansi, slip PDF server-side, parameter regulasi yang bisa diedit per tenant | 11 tes unit golden (hand-computed) + 9 tes integrasi `HrPayrollTests` + UI diverifikasi end-to-end (atur gaji → hitung → setujui oleh orang kedua → kunci → slip karyawan) |
| 4 Hardening & Go-Live | **Sebagian (bagian yang bisa dikerjakan di repo)**: impor karyawan massal all-or-nothing (EMP-006) + tautan akun di form, sapuan otorisasi otomatis atas seluruh route HR, uji skala 5.000 karyawan, endpoint `/health/ready` (cek DB + migrasi), skrip `restore-drill.sh` yang sudah dijalankan, runbook, threat model, lembar PDP, rencana cutover, skrip k6. **Belum (butuh pihak/infrastruktur lain)**: pentest eksternal, validasi pajak, paralel run, monitoring & alert, uji beban k6 di staging, DR drill di infrastruktur sungguhan, DPO & dokumen legal, pelatihan, CI di GitHub | `HrEmployeeTests` (13), `HrAuthorizationSweepTests` (4), `HrScaleTests` (p95 339 ms; payroll 7 s); restore drill: 42 tabel / 2.168 baris cocok, perubahan satu baris terdeteksi |

Keputusan implementasi (Fase 3): aturan regulasi ada di kode (`PayrollRules`, versi `ID-2026.1`) dan disimpan per pay run sebagai snapshot JSON, belum bisa diedit per tenant; **tabel TER, PTKP, tarif/batas BPJS dan metode true-up ditulis dari pengetahuan umum dan WAJIB divalidasi konsultan pajak sebelum go-live**; true-up memakai PTKP prorata (bukan penyetahunan) untuk masa kerja < 12 bulan; BPJS dihitung dari upah kontrak (tidak berkurang oleh cuti tanpa gaji); satu pay run per bulan; bulan hanya bisa dihitung bila bulan sebelumnya di tahun yang sama sudah terkunci; semua endpoint payroll memerlukan konfirmasi kata sandi terbaru; angka pada baris pay run disimpan polos (agar bisa dijumlah) dan dilindungi izin + trigger, sedangkan gaji di kompensasi dienkripsi.

Keputusan implementasi (Fase 2): hari kerja = Senin–Jumat dikurangi hari libur; satu pengajuan tidak boleh melintasi tahun; terlambat = clock-in pertama setelah 09:10 WIB (default, jadi setelan saat shift dibuat); jenis cuti default dibuat otomatis per tenant saat pertama diakses; waktu clock selalu dari server.

Keputusan implementasi (Fase 1): perubahan jabatan *tidak boleh berlaku mundur ke periode tertutup* dan belum mendukung tanggal masa depan (EMP-004, v1); data sensitif hanya tampil jika punya `employees.read_sensitive` **dan** reauth ≤ 5 menit, setiap tampilan tercatat di audit; scoping data per atasan (MSS) ditunda ke Fase 2.

---

## 1. Ringkasan & Tujuan

**Masalah.** Data karyawan, cuti, absensi, dan penggajian tersebar di spreadsheet dan chat; approval tidak terlacak; risiko kesalahan hitung payroll/pajak dan kebocoran data pribadi (UU PDP).

**Visi.** Satu sistem tepercaya untuk seluruh siklus karyawan (hire → retire) untuk perusahaan Indonesia, dengan self-service karyawan, approval berjenjang, dan payroll yang akurat serta teraudit.

**Tujuan terukur (12 bulan setelah go-live):**
| # | Tujuan | Target |
|---|---|---|
| G1 | Waktu proses payroll bulanan (HR) | ≤ 1 hari kerja (baseline spreadsheet: 3–5 hari) |
| G2 | Selisih payroll vs. perhitungan manual-verifikasi | 0 selisih pada sampel audit 100% slip |
| G3 | Pengajuan cuti/izin lewat sistem | ≥ 95% |
| G4 | Approval selesai ≤ 2 hari kerja | ≥ 90% |
| G5 | Adopsi self-service karyawan (login ≥ 1×/bulan) | ≥ 85% |
| G6 | Ketersediaan | ≥ 99,9% (jam kerja WIB) |
| G7 | Insiden kebocoran data pribadi | 0 |

**Non-goals (v1):** rekrutmen/ATS penuh, performance review 360°, LMS, payroll multi-negara, integrasi bank host-to-host (disediakan file transfer massal saja), aplikasi mobile native (PWA saja, sudah ada di repo).

## 2. Konteks Teknis (sudah ada di repo)

- Web: Next.js 15 (App Router), React 19, i18n EN/ID, tema terang/gelap, PWA.
- API: ASP.NET Core (.NET 10), EF Core + PostgreSQL 16, Clean Architecture (`Domain/Application/Infrastructure/Api`).
- IAM siap pakai: login + MFA TOTP, SSO OIDC, RBAC, undangan, sesi, audit log, security events, pengaturan per-tenant, ekspor/penutupan akun, proxy-key + CSRF.
- Deploy: Docker/Coolify, backup script, Playwright e2e, Testcontainers.

**Prinsip:** HR = modul baru di atas IAM. Permission HR memakai katalog `resource.action` yang sama. Tidak ada tabel/identitas ganda: `Employee` ← 1:0..1 → `User`.

## 3. Persona

| Persona | Kebutuhan utama | Role |
|---|---|---|
| **Karyawan** | Lihat profil, slip gaji, saldo cuti; ajukan cuti/izin/lembur; absen | `employee` |
| **Manajer / Atasan** | Approve tim, lihat kalender & kehadiran tim, org chart | `manager` |
| **HR Admin** | Kelola data master karyawan, cuti, absensi, dokumen, onboarding/offboarding | `hr_admin` |
| **Payroll Officer** | Jalankan, tinjau, kunci payroll; laporan pajak/BPJS | `payroll_officer` |
| **HR Head / Direksi** | Dashboard headcount, turnover, biaya; approval sensitif | `hr_head` |
| **Auditor** | Baca-saja audit & laporan | `security_auditor` (ada) |
| **Super Admin** | Konfigurasi sistem, kebijakan, akses | `super_admin` (ada) |

Aturan pemisahan tugas (SoD): pembuat payroll ≠ pemberi persetujuan akhir; HR Admin tidak melihat gaji kecuali punya `payroll.read`; manajer tidak melihat gaji bawahan.

## 4. Lingkup Modul & Requirement Fungsional

Format ID: `<MODUL>-nnn`. Setiap requirement punya kriteria penerimaan di §9.

### 4.1 Core HR — Data Karyawan (EMP)
| ID | Requirement | Pri |
|---|---|---|
| EMP-001 | Profil karyawan: NIK karyawan (auto/unik), nama, tgl lahir, gender, status pernikahan, agama, kontak, alamat (KTP/domisili), kontak darurat, foto | P0 |
| EMP-002 | Data kepegawaian: tgl masuk, status (PKWT/PKWTT/Magang/Probation), tgl berakhir kontrak, jabatan, grade, departemen, lokasi, atasan langsung, entitas hukum | P0 |
| EMP-003 | Data sensitif terpisah & ber-permission: NIK KTP, NPWP, nomor BPJS, rekening bank, gaji — terenkripsi at-rest (AES-256-GCM seperti MFA secret), masking di UI/log/ekspor | P0 |
| EMP-004 | Riwayat efektif-tanggal (*effective-dated*): jabatan, gaji, atasan, status — tidak menimpa, dapat dilihat per tanggal | P0 |
| EMP-005 | Dokumen karyawan (KTP, kontrak, ijazah, sertifikat): upload tervalidasi (tipe/ukuran/antivirus), versi, kategori, tanggal kedaluwarsa + pengingat | P0 |
| EMP-006 | Import massal CSV/XLSX dengan *dry-run* validasi, laporan error per baris, rollback atomik | P0 |
| EMP-007 | Pencarian & filter server-side, ekspor (ber-audit, field sensitif butuh permission) | P0 |
| EMP-008 | Org chart & struktur organisasi (departemen, posisi, atasan); deteksi siklus atasan | P1 |
| EMP-009 | Perubahan data oleh karyawan (ESS) melalui *change request* yang di-approve HR untuk field kritis | P1 |
| EMP-010 | Manajemen entitas hukum/cabang (multi-entitas dalam satu tenant) | P1 |

### 4.2 Onboarding & Offboarding (ONB)
| ID | Requirement | Pri |
|---|---|---|
| ONB-001 | Pre-hire → hire: HR membuat karyawan dari data kandidat; undangan IAM otomatis (reuse `invitations`) | P0 |
| ONB-002 | Checklist onboarding/offboarding per template (tugas, PIC, due date, status), notifikasi & pengingat | P0 |
| ONB-003 | Offboarding: resign/PHK/pensiun/kontrak berakhir; tanggal efektif, alasan, exit checklist (aset, akses), nonaktifkan akun IAM otomatis pada tanggal efektif & cabut sesi | P0 |
| ONB-004 | Perhitungan final pay (sisa cuti, prorata, pesangon/UPMK/UPH sesuai konfigurasi kebijakan) sebagai input payroll | P1 |
| ONB-005 | Pelacakan masa percobaan & kontrak: pengingat 30/14/7 hari sebelum berakhir ke HR dan atasan | P0 |

### 4.3 Cuti & Izin (LVE)
| ID | Requirement | Pri |
|---|---|---|
| LVE-001 | Jenis cuti terkonfigurasi: tahunan, sakit, melahirkan, ayah, menikah, duka, ibadah, tanpa gaji, cuti bersama, lainnya; per jenis: kuota, accrual, carry-over, kedaluwarsa, butuh dokumen, minimal/maks hari, setengah hari | P0 |
| LVE-002 | Baseline kebijakan ID (konfigurabel): cuti tahunan 12 hari setelah 12 bulan kerja; cuti melahirkan ≥ 3 bulan (dapat lebih sesuai UU KIA bila ada indikasi medis); cuti sakit dengan surat dokter; cuti bersama & hari libur nasional dari kalender | P0 |
| LVE-003 | Pengajuan: rentang tanggal, setengah hari, alasan, lampiran, delegasi; validasi saldo, bentrok, blackout period, hari kerja vs. libur | P0 |
| LVE-004 | Alur approval berjenjang (atasan → HR, bisa dikonfigurasi per jenis/durasi); delegasi saat atasan cuti; eskalasi otomatis jika > SLA | P0 |
| LVE-005 | Saldo ledger (immutable): accrual, penggunaan, penyesuaian, hangus, carry-over — setiap perubahan teraudit | P0 |
| LVE-006 | Pembatalan & revisi (sebelum/ sesudah approve) dengan pemulihan saldo | P0 |
| LVE-007 | Kalender tim/perusahaan (siapa cuti), kalender libur nasional & cuti bersama (tahunan, dapat diimpor) | P0 |
| LVE-008 | Notifikasi email + in-app (diajukan, disetujui, ditolak, eskalasi, saldo hampir hangus) | P0 |
| LVE-009 | Laporan saldo & penggunaan cuti; ekspor | P0 |
| LVE-010 | Konversi cuti hangus → kompensasi (opsional per kebijakan) | P2 |

### 4.4 Absensi & Waktu Kerja (ATT)
| ID | Requirement | Pri |
|---|---|---|
| ATT-001 | Jadwal/shift kerja per karyawan/grup; hari kerja 5/6, jam fleksibel; toleransi keterlambatan | P0 |
| ATT-002 | Clock-in/out via web/PWA dengan timestamp server (bukan jam klien), IP/geofence opsional, foto opsional; anti-duplikasi | P0 |
| ATT-003 | Impor log mesin fingerprint/CSV; rekonsiliasi | P1 |
| ATT-004 | Koreksi absensi (lupa absen) dengan approval; semua koreksi teraudit | P0 |
| ATT-005 | Lembur: pengajuan pra-approval, perhitungan jam, batas sesuai regulasi (maks 4 jam/hari & 18 jam/minggu sebagai default konfigurasi), upah lembur mengikuti PP 35/2021 | P0 |
| ATT-006 | Rekap kehadiran bulanan: hadir, telat, alpa, cuti, sakit, lembur → input payroll | P0 |
| ATT-007 | WFH/dinas luar & status kerja jarak jauh | P1 |
| ATT-008 | Penutupan periode absensi (lock) sebelum payroll; pembukaan ulang butuh alasan & audit | P0 |

### 4.5 Penggajian (PAY) — domain berisiko tertinggi
| ID | Requirement | Pri |
|---|---|---|
| PAY-001 | Struktur gaji: gaji pokok, tunjangan tetap/tidak tetap, potongan; komponen konfigurabel (taxable/non-taxable, BPJS-base, prorata) | P0 |
| PAY-002 | Siklus payroll: Draft → Dihitung → Ditinjau → Disetujui → Dikunci → Dibayar. Hanya status Draft/Dihitung yang bisa dihitung ulang; Dikunci immutable (koreksi lewat *adjustment run*) | P0 |
| PAY-003 | Perhitungan PPh 21 sesuai metode berlaku (TER bulanan + penyesuaian akhir tahun/Desember, PTKP per status, NPWP/non-NPWP); tabel tarif & PTKP sebagai data berversi, bukan kode | P0 |
| PAY-004 | BPJS Kesehatan & Ketenagakerjaan (JHT, JP, JKK, JKM) — tarif, batas upah (cap), porsi perusahaan/karyawan konfigurabel & berversi; baseline: Kes 4%/1% (cap Rp12 jt), JHT 3,7%/2%, JP 2%/1% (cap berjalan), JKK 0,24–1,74%, JKM 0,3% | P0 |
| PAY-005 | Prorata (masuk/keluar tengah bulan), potongan alpa/telat, upah lembur, cuti tanpa gaji, reimburse, bonus, THR (≥12 bln = 1× gaji; < 12 bln proporsional; dibayar paling lambat H-7 hari raya), pesangon | P0 |
| PAY-006 | Pinjaman/kasbon dengan cicilan otomatis dan saldo | P1 |
| PAY-007 | Pratinjau & rekonsiliasi: perbandingan dengan bulan lalu, anomali (selisih > ambang, gaji negatif, komponen hilang), checklist sebelum approve | P0 |
| PAY-008 | Approval payroll 2-orang (maker–checker), tidak boleh orang yang sama | P0 |
| PAY-009 | Slip gaji PDF per karyawan (ESS), dilindungi akses; notifikasi email *tanpa* nominal; riwayat slip | P0 |
| PAY-010 | Output: file transfer bank massal (CSV format konfigurabel), laporan BPJS, rekap PPh 21 (bukti potong 1721-A1 / data untuk e-Bupot), jurnal ke akuntansi (CSV) | P0 |
| PAY-011 | Presisi uang: `decimal` (tanpa float), pembulatan sesuai aturan terdokumentasi, mata uang IDR; perhitungan deterministik & dapat direproduksi dari snapshot input | P0 |
| PAY-012 | Retroaktif: kenaikan gaji/koreksi mundur menghasilkan selisih otomatis pada periode berjalan | P1 |
| PAY-013 | Multi-skema payroll (bulanan, mingguan, harian) | P2 |

### 4.6 Self-Service (ESS) & Manager Self-Service (MSS)
| ID | Requirement | Pri |
|---|---|---|
| ESS-001 | Dashboard karyawan: saldo cuti, status pengajuan, absensi hari ini, pengumuman, tugas onboarding | P0 |
| ESS-002 | Profil sendiri, dokumen sendiri, slip gaji sendiri, bukti potong tahunan | P0 |
| ESS-003 | Surat keterangan kerja otomatis dari template (approval HR) | P1 |
| ESS-004 | Reimbursement/klaim dengan lampiran & approval | P1 |
| MSS-001 | Inbox approval terpadu (cuti, lembur, koreksi absen, klaim) dengan aksi massal dan komentar | P0 |
| MSS-002 | Dashboard tim: kehadiran hari ini, cuti mendatang, kontrak berakhir | P0 |

### 4.7 Pelaporan & Dashboard (RPT)
| ID | Requirement | Pri |
|---|---|---|
| RPT-001 | Dashboard HR: headcount, joiner/leaver, turnover, komposisi (dept/gender/usia/status), kontrak akan berakhir | P0 |
| RPT-002 | Laporan standar: daftar karyawan, kehadiran, cuti, lembur, payroll register, BPJS, PPh 21, biaya tenaga kerja per departemen | P0 |
| RPT-003 | Ekspor CSV/XLSX/PDF, ber-audit, sesuai permission & masking | P0 |
| RPT-004 | Laporan terjadwal via email (tautan aman, bukan lampiran sensitif) | P2 |

### 4.8 Pengumuman, Notifikasi, Kebijakan (COM)
| ID | Requirement | Pri |
|---|---|---|
| COM-001 | Pengumuman perusahaan dengan target audiens, tanda baca | P1 |
| COM-002 | Pusat notifikasi in-app + email (outbox durable yang sudah ada) dengan preferensi pengguna | P0 |
| COM-003 | Penerbitan kebijakan & tanda terima persetujuan karyawan (acknowledgement), versi dokumen | P1 |

### 4.9 Administrasi & Konfigurasi (ADM)
| ID | Requirement | Pri |
|---|---|---|
| ADM-001 | Master data: departemen, jabatan, grade, lokasi, entitas, jenis cuti, komponen gaji, kalender libur | P0 |
| ADM-002 | Workflow engine ringan: urutan approver per jenis permintaan, kondisi (durasi/nominal), SLA, delegasi, eskalasi | P0 |
| ADM-003 | Parameter regulasi berversi dan berlaku-efektif (tarif BPJS, PTKP, TER, UMP/UMK referensi) dengan jejak perubahan | P0 |
| ADM-004 | Integrasi/ekspor: REST API terdokumentasi (OpenAPI), webhook event (employee.hired, leave.approved, payroll.locked) | P1 |
| ADM-005 | Integrasi SSO (OIDC sudah ada), provisioning SCIM | P2 |

## 5. Model Data Inti (ringkas)

Entitas baru (skema `hr`, semua tabel memuat `tenant_id`, `created_at/by`, `updated_at/by`, `row_version` untuk *optimistic concurrency*):
`employees`, `employee_employments` (effective-dated), `employee_compensations` (effective-dated, terenkripsi), `employee_documents`, `org_units`, `positions`, `legal_entities`, `leave_types`, `leave_policies`, `leave_balance_ledger`, `leave_requests`, `holidays`, `shifts`, `shift_assignments`, `attendance_logs`, `attendance_corrections`, `overtime_requests`, `pay_components`, `pay_runs`, `pay_run_items`, `pay_run_item_lines`, `payslips`, `statutory_parameters` (berversi), `loans`, `claims`, `approval_workflows`, `approval_instances`, `approval_steps`, `onboarding_templates`, `onboarding_tasks`, `announcements`, `policy_acknowledgements`.

Aturan integritas: ledger cuti & pay run terkunci bersifat *append-only*; relasi `employees.user_id` unik nullable; atasan tidak boleh membentuk siklus; satu karyawan satu employment aktif per tanggal; semua uang `numeric(18,2)`.

Detail ERD, state machine, dan migrasi ditulis di dokumen desain lanjutan (lihat §12), mengikuti format `07-data-model.md`.

## 6. Hak Akses (RBAC) — tambahan katalog permission

Role baru: `employee`, `manager`, `hr_admin`, `payroll_officer`, `hr_head`. Scoping data: *own*, *direct reports*, *org unit*, *all* — di-enforce **di server** pada query (bukan hanya UI).

| Modul | Permission baru |
|---|---|
| Employee | `employees.read`, `employees.create`, `employees.update`, `employees.read_sensitive`, `employees.export`, `employees.import`, `documents.read`, `documents.manage` |
| Leave | `leave.request` (own), `leave.approve`, `leave.manage`, `leave.read_all`, `holidays.manage` |
| Attendance | `attendance.clock` (own), `attendance.read_team`, `attendance.manage`, `attendance.close_period`, `overtime.approve` |
| Payroll | `payroll.read`, `payroll.run`, `payroll.approve`, `payroll.lock`, `payroll.configure`, `payslips.read_own` |
| Reports | `reports.hr`, `reports.payroll` |
| Admin | `hrconfig.manage`, `workflows.manage` |

Invariant tambahan (masing-masing wajib punya tes): (1) tidak bisa approve permintaan sendiri; (2) maker ≠ checker pada payroll; (3) manajer hanya melihat bawahan langsung/tidak langsung; (4) `payroll.*` dan `employees.read_sensitive` butuh reauth ≤ 5 menit; (5) setiap akses data sensitif dan setiap ekspor tercatat di audit; (6) karyawan nonaktif kehilangan seluruh akses pada tanggal efektif.

## 7. Persyaratan Non-Fungsional (Production-Ready)

### 7.1 Keamanan & Privasi
- **UU No. 27/2022 (PDP):** dasar pemrosesan & persetujuan, hak subjek data (akses, koreksi, hapus, ekspor — reuse ekspor/penutupan akun IAM), pencatatan pemrosesan (RoPA), prosedur notifikasi insiden ≤ 3×24 jam, penunjukan penanggung jawab data. Data kesehatan (surat sakit) dan biometrik diperlakukan sebagai data spesifik.
- Enkripsi in-transit (TLS 1.2+, HSTS) dan at-rest (disk + field-level untuk NIK, NPWP, rekening, gaji); rotasi kunci berversi (sudah ada `EncryptionKeys`).
- OWASP ASVS L2; tes IDOR/BOLA wajib untuk setiap endpoint ber-ID karyawan; upload: validasi MIME magic-bytes, batas ukuran, penyimpanan di luar webroot, unduh via tautan bertanda tangan berumur pendek.
- Log & audit: tanpa data sensitif; audit *append-only* dengan retensi ≥ 5 tahun untuk payroll/pajak (menyesuaikan kewajiban penyimpanan dokumen perpajakan), konfigurabel.
- Pentest eksternal sebelum go-live; temuan High/Critical = 0 terbuka.

### 7.2 Keandalan & Operasi
- SLO: ketersediaan 99,9% jam kerja; RPO ≤ 15 menit (WAL archiving/PITR), RTO ≤ 4 jam; backup harian terenkripsi + uji restore **bulanan** (terdokumentasi).
- Environment: dev → staging → production dengan migrasi sebagai langkah rilis (tidak saat boot), rollback tertulis (`12-migration-and-rollback.md`).
- Observability: structured logging + correlation ID, metrik (latency, error rate, antrean outbox, durasi payroll run), health/readiness endpoint, alerting (on-call), dashboard.
- Pekerjaan latar (accrual cuti, pengingat kontrak, payroll run, outbox) idempoten, dapat diulang, dengan *distributed lock*; payroll run berjalan sebagai job dengan progres.
- Rate limiting, request size limit, timeout, circuit breaker untuk email/penyimpanan.

### 7.3 Performa & Skala
- Target v1: 5.000 karyawan/tenant, 50 tenant; p95 < 300 ms untuk list ter-paginasi, p95 < 800 ms untuk halaman detail; payroll 5.000 karyawan dihitung < 5 menit; uji beban sebelum go-live (k6) pada 2× target.
- Semua list server-side pagination; indeks pada kolom filter; tanpa N+1 (dites).

### 7.4 Kualitas & Aksesibilitas
- WCAG 2.1 AA; responsif 375–1440; EN & ID penuh (semua teks lewat `t()`); zona waktu WIB/WITA/WIT per lokasi, penyimpanan UTC.
- Pyramid tes: unit (domain payroll/cuti/pajak: *golden files* dengan kasus dari konsultan pajak), integrasi (Testcontainers), e2e Playwright untuk perjalanan kritis, a11y axe, tes otorisasi negatif. Cakupan domain payroll/cuti ≥ 90%; gerbang CI: typecheck, lint, unit, api test, e2e smoke, audit dependensi, SAST/secret scan.

### 7.5 Kepatuhan Data & Retensi
- Retensi: data karyawan aktif selama hubungan kerja + periode legal setelahnya; dokumen perpajakan/payroll ≥ 10 tahun (konfirmasi dengan konsultan pajak); anonimisasi setelah masa retensi; *legal hold*.

## 8. Alur Kritis (ringkas)
1. **Hire:** HR buat karyawan → undangan IAM → karyawan aktivasi + MFA → checklist onboarding → akses ESS.
2. **Cuti:** karyawan ajukan → validasi saldo/bentrok → atasan approve → HR (jika perlu) → saldo ledger terpotong → kalender & absensi terupdate.
3. **Payroll bulanan:** tutup absensi → HR input variabel → Payroll Officer *Hitung* → tinjau anomali → Maker kirim → Checker setujui (reauth) → *Kunci* → slip terbit + file bank + laporan BPJS/PPh → tandai *Dibayar*.
4. **Offboarding:** HR input resign/PHK + tanggal efektif → final pay dihitung → exit checklist → pada tanggal efektif akun dinonaktifkan, sesi dicabut, data diarsipkan.

## 9. Kriteria Penerimaan Utama (Definition of Done per modul)
- **Core HR:** impor 1.000 baris dengan 5% error menghasilkan laporan error tepat & tidak ada data parsial; field sensitif tidak muncul di log, respons tanpa permission, atau ekspor tanpa permission; riwayat effective-dated benar untuk 3 perubahan berurutan.
- **Cuti:** 30 skenario tabel keputusan (prorata, setengah hari, libur, bentrok, pembatalan, carry-over) lulus; saldo = jumlah ledger selalu; tidak ada race condition saat 2 pengajuan bersamaan (tes konkurensi).
- **Absensi:** timestamp selalu dari server; koreksi tanpa approval tidak mengubah rekap; periode terkunci menolak perubahan.
- **Payroll:** ≥ 50 kasus golden (termasuk PTKP berbeda, non-NPWP, cap BPJS, prorata, THR, Desember true-up, lembur) cocok dengan perhitungan konsultan pajak sampai rupiah; run yang sama dengan input sama menghasilkan hasil identik; run terkunci tidak bisa diubah via API/DB role aplikasi; maker=checker ditolak.
- **Keamanan:** tes IDOR untuk semua endpoint karyawan/slip/dokumen lulus; pentest tanpa temuan High/Critical terbuka; reauth pada aksi kritis ditegakkan.
- **Operasi:** restore dari backup ke staging sukses dalam RTO; alert uji menyala; runbook on-call tersedia.

## 10. Rencana Rilis

| Fase | Isi | Perkiraan | Gerbang keluar |
|---|---|---|---|
| **0 — Fondasi** | Skema `hr`, role/permission baru, master data, workflow engine, effective-dating, enkripsi field, observability dasar | 3–4 minggu | Tes otorisasi & enkripsi lulus |
| **1 — Core HR + Onboarding** | EMP-001…007, ONB-001…003,005, dokumen, impor | 4 minggu | Pilot data nyata (staging) |
| **2 — Cuti & Absensi** | LVE-*, ATT-001,002,004,005,006,008, kalender, notifikasi | 5 minggu | UAT HR + 1 departemen pilot |
| **3 — Payroll** | PAY-001…005,007…011, slip, laporan | 6–8 minggu | **Paralel run 2 siklus** vs. proses lama, selisih = 0 |
| **4 — Hardening & Go-Live** | Pentest, uji beban, DR drill, pelatihan, migrasi data, runbook | 3 minggu | Checklist §11 100% |
| **5 — P1** | Org chart, ESS change request, pinjaman, retro, klaim, pengumuman, integrasi | pasca go-live | — |

Strategi go-live: migrasi data → pilot satu entitas → paralel run payroll → cut-over bertahap; rollback plan: kembali ke proses lama selama siklus paralel.

## 11. Checklist Kesiapan Produksi (Go/No-Go)
Status per 2026-11-10. ✅ = dibuktikan di repo (bukti di kolom kanan), ⬜ = terbuka.
- ⬜ Semua requirement P0 punya bukti: **belum** (lihat §0: dokumen, onboarding, lembur, koreksi absen, notifikasi, dll. belum ada)
- ⬜ Parameter regulasi (PPh 21/TER, PTKP, BPJS, lembur, THR) divalidasi tertulis oleh konsultan pajak/HR-Legal
- ⬜ Paralel run payroll ≥ 2 siklus dengan selisih 0 (rencana: doc 27)
- ⬜ Pentest eksternal selesai, High/Critical = 0 (threat model internal: doc 25)
- ✅/⬜ Uji beban: ✅ 5.000 karyawan in-process (p95 339 ms, payroll 7 s); ⬜ 2× target di staging dengan k6 (`scripts/k6-hr.js`)
- ✅/⬜ Backup + restore: ✅ restore drill berhasil & detektor mismatch teruji; ⬜ DR drill di infrastruktur sungguhan, RPO ≤ 15 menit belum dikonfigurasi
- ⬜ Secret production dibuat baru, rotasi terjadwal (`StartupChecks` menolak kunci dev: ✅ ada)
- ⬜ DPA/RoPA/kebijakan privasi & prosedur insiden PDP disetujui; DPO/PIC ditunjuk (lembar kerja: doc 26)
- ⬜ Monitoring, alerting, on-call aktif (daftar alert & runbook: doc 24)
- ⬜ Pelatihan HR, Payroll, Manajer; panduan pengguna ID/EN
- ⬜ Rencana rollback & komunikasi go-live disetujui (draf: doc 24 §1, doc 27 §3)
- ⬜ Audit akses akhir: ≥ 2 super admin, MFA wajib untuk HR/Payroll/Admin

## 12. Dokumen Turunan yang Harus Dibuat (mengikuti pola docs 06–12)
`24-hr-data-model.md`, `25-hr-api-spec.md`, `26-hr-rbac-matrix.md`, `27-payroll-calculation-spec.md` (rumus, pembulatan, kasus golden), `28-hr-threat-model.md` (STRIDE + PDP), `29-hr-test-plan.md`, `30-hr-migration-and-cutover.md`.

## 13. Metrik Keberhasilan & Pemantauan
Produk: adopsi ESS, waktu approval, % cuti via sistem, selisih payroll, tiket koreksi per siklus. Teknis: error rate < 0,5%, p95 latency, antrean outbox < 1 menit, kegagalan job = 0 tanpa alert. Keamanan: akses data sensitif per hari, percobaan akses ditolak, waktu patch Critical ≤ 7 hari.

## 14. Risiko & Mitigasi
| Risiko | Dampak | Mitigasi |
|---|---|---|
| Perubahan regulasi pajak/BPJS | Salah hitung | Parameter berversi & berlaku-efektif; review kuartalan; golden tests |
| Kesalahan payroll | Finansial & hukum | Maker–checker, anomali, kunci immutable, paralel run |
| Kebocoran data pribadi | Sanksi PDP, reputasi | Field encryption, least privilege, audit, pentest, DLP ekspor |
| Kualitas data migrasi buruk | Go-live tertunda | Dry-run import, rekonsiliasi, pilot |
| Adopsi rendah | ROI turun | PWA mobile-first, pelatihan, champion per departemen |
| Skala/performa payroll | Timeout | Job async, batch, uji beban |
| Ketergantungan email | Approval macet | Outbox durable, notifikasi in-app, eskalasi |

## 15. Asumsi & Keputusan Default (rekomendasi; dapat diubah PO)
| ID | Keputusan | Default |
|---|---|---|
| H-01 | Tenancy | Multi-tenant (per-tenant settings sudah ada), `tenant_id` di semua tabel HR |
| H-02 | Metode PPh 21 | TER bulanan + true-up Desember; dapat dikonfigurasi |
| H-03 | Pembayaran gaji | File transfer massal; tanpa integrasi bank langsung di v1 |
| H-04 | Absensi | Web/PWA + impor file; biometrik langsung di P1 |
| H-05 | Jam kerja | 40 jam/minggu default, konfigurabel per shift |
| H-06 | Penyimpanan dokumen | Object storage S3-compatible, enkripsi server-side |
| H-07 | Notifikasi | Email (Resend, sudah ada) + in-app; WhatsApp di P2 |
| H-08 | Mata uang | IDR saja |
| H-09 | Bahasa | ID default, EN tersedia |
| H-10 | Pembatasan tahun pertama | Satu negara (Indonesia), satu entitas pilot lalu bertahap |

## 16. Pertanyaan Terbuka (tidak memblokir; default di §15 dipakai bila tidak dijawab)
Jumlah karyawan & entitas aktual; format file bank yang dipakai; sistem akuntansi tujuan jurnal; kebijakan cuti spesifik perusahaan (carry-over, cuti bersama); apakah ada perusahaan outsourcing/PKWT dengan skema khusus.
