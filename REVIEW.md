# Self-review

Project: Access Request Hub — Phase 1  
Terakhir diperbarui: 16 Oktober 2026  
Status: DRAFT — hasil akhir pengujian dan review pribadi pengembang perlu dilengkapi.

Review dibantu ChatGPT berdasarkan source/paket sampai langkah 11 dan output
yang dilaporkan pengembang. Severity mengukur dampak terhadap kesiapan
submission atau penggunaan di luar scope demo lokal. Status Open berarti
tindak lanjut atau bukti hasil masih diperlukan.

## 1. Findings

| Finding | Severity | Action | Status | Evidence |
| --- | --- | --- | --- | --- |
| R01 — Hasil akhir build dan pengujian runtime belum dilaporkan setelah executable terkunci | High untuk kesiapan submission | Hentikan server demo; jalankan Test-Step11.ps1; catat commit dan hasil C#/API/hosting; perbaiki setiap failure | Open | Error MSB3027/MSB3021 yang dilaporkan; scripts/Test-Step11.ps1; docs/STEP_11_VALIDATION.md |
| R02 — Manual browser QA belum mempunyai hasil aktual yang tercatat | Medium | Uji critical flow, conflict dua tab, keyboard/dialog, loading/empty/error, mobile, dan Console/CSP | Open | src/AccessRequestHub/wwwroot; docs/STEP_11_VALIDATION.md |
| R03 — Fetch frontend belum mempunyai timeout aplikasi yang eksplisit | Medium | Tambahkan deadline/AbortController dan pesan retry yang mempertahankan ClientRequestId atau expected Version; uji request menggantung | Open; perbaikan belum diterapkan | Fungsi api() dan busy() pada src/AccessRequestHub/wwwroot/app.js |
| R04 — User switcher/header merupakan identitas simulasi yang dapat dipilih pengguna demo | High jika dipakai untuk production | Pertahankan demo lokal dengan loopback binding; rancang autentikasi nyata jika scope berubah | Accepted for assessment scope | Program.cs; HubService.cs; README.md |
| R05 — SQLite mempunyai satu writer aktif; lock dapat menunda operasi lain | Medium untuk perluasan beban | Terapkan transaksi, timeout, unique key, expected Version, dan safe retry; verifikasi concurrent behavior | Accepted for local MVP; bukti runtime masih perlu dicatat | HubDatabase.cs; HubService.cs; tests/AccessRequestHub.Tests/Program.cs |
| R06 — Ownership declaration dan konfirmasi bantuan/sumber belum difinalisasi pengembang | Medium untuk kelengkapan evidence | Isi INTEGRITY.md berdasarkan review aktual; selaraskan AI_USAGE.md dan hasil validasi | Open | INTEGRITY.md; AI_USAGE.md |
| R07 — Detail PLAN.md dilengkapi setelah kerangka awal yang berisi judul bagian | Low untuk provenance | Catat pembaruan sesuai waktu sebenarnya dan pertahankan Git history | Documented; history diperiksa saat finalisasi | PLAN.md; git log |

R03 merupakan temuan review source: koneksi yang tidak selesai dapat membuat
state busy bertahan lama karena fetch belum dibatasi deadline aplikasi.
Mekanisme pending request dan retry perlu dipertahankan saat memperbaikinya.

## 2. Review security dan correctness

Kontrol berikut teridentifikasi pada source. Bukti runtime dilengkapi setelah
pengujian aplikasi dan walkthrough pada komputer pengembang.

| Area | Kontrol implementasi | Evidence |
| --- | --- | --- |
| Identity/requester | Seeded user divalidasi; requester berasal dari current user; unknown JSON field ditolak | Program.cs; HubService.GetUserAsync/CreateAsync |
| Authorization | Visibility berdasarkan hubungan user; assigned stage dan self-approval diperiksa backend; auditor tidak otomatis menjadi approver | HubService.ListAsync/DetailAsync/DecideAsync |
| Input/SQL | Enum/length validation, strict JSON, body cap, dan SQL berparameter | Program.cs; HubService.cs |
| Idempotency | Unique ClientRequestId, actor/payload comparison, dan transaksi create/replay | HubService.CreateAsync; migration 001_initial.sql |
| Concurrency/state | Immediate transaction, expected Version, conditional UPDATE, dan terminal-state guards | HubDatabase.cs; HubService.DecideAsync; migration |
| Audit | Trigger dalam transaksi mutation; audit update/delete dibatasi; failure-injection checks disediakan | migration; tests/AccessRequestHub.Tests/Program.cs |
| Policy/history | PolicyVersion v1 dan snapshot manager/owner disimpan; request facts immutable | migration; HubService.cs |
| Read consistency | Detail request dan audit dibaca dalam satu read transaction | HubService.DetailAsync |
| Frontend | textContent, pending ClientRequestId/payload, double-submit guard, dan respons konteks lama diabaikan | wwwroot/app.js; scripts/Test-Step10.mjs |
| HTTP/logging | Loopback Host/Origin, CSP/security headers, trace ID, log tanpa justification/reject reason | Program.cs |

## 3. Evidence verifikasi yang tersedia

| Pemeriksaan | Lingkungan/sumber | Hasil yang diketahui |
| --- | --- | --- |
| Project test terdaftar di solution | Output CLI pengembang | Confirmed: solution already contains tests/AccessRequestHub.Tests/AccessRequestHub.Tests.csproj |
| JavaScript frontend simulasi | Environment pembuat paket | 10 lulus, 0 gagal; dijalankan ulang saat menyiapkan paket langkah 11 |
| SQL/schema tambahan | Environment pembuat paket | Enam pemeriksaan lulus, termasuk audit rollback dan concurrent raw INSERT/conditional UPDATE |
| Build aplikasi/project test | Komputer pengembang | Pernah gagal karena file terkunci; hasil build ulang belum dicatat di sini |
| Service/database integration C# | Target 23 kasus | Hasil eksekusi pengembang belum dilaporkan |
| API regression | Target 33 checks | Hasil eksekusi pengembang belum dilaporkan |
| Hosting/database smoke | Target tujuh checks | Hasil eksekusi pengembang belum dilaporkan |
| Manual browser QA | Komputer pengembang | Hasil belum dilaporkan |

Enam pemeriksaan SQL menjalankan schema/query melalui SQLite. Sepuluh
pemeriksaan JavaScript menggunakan DOM/HTTP simulasi. Bukti C#, HTTP,
middleware, serta browser nyata diperoleh dari pengujian terpisah.
Jumlah target 23/33/7 belum dinyatakan lulus.

Command utama dari root repository setelah server demo dihentikan:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Step11.ps1
```

Jika Node.js belum tersedia, gunakan -SkipFrontend dan catat SKIPPED untuk
kelompok JavaScript. Simpan hasil aktual pada docs/STEP_11_VALIDATION.md.

## 4. Known limitations

- Identitas simulasi sesuai demo lokal; autentikasi production belum tercakup.
- SQLite mempunyai satu writer aktif. Operasi async Microsoft.Data.Sqlite
  berjalan sinkron; kapasitas untuk beban lebih luas belum dievaluasi.
- Timeout fetch eksplisit belum diterapkan; lihat R03.
- Retry lintas reload bergantung pada sessionStorage. Fallback memory hanya
  bertahan selama halaman tetap terbuka jika storage tidak dapat digunakan.
- Append-only dijaga pada normal application flow; administrator yang
  menguasai database/schema dapat mengubah trigger dan data.
- Test C# menggunakan console runner dengan dotnet run dan exit code 0/1.
  Frontend simulation checks belum memeriksa layout/native dialog browser.
- Hasil source review/simulasi perlu dilengkapi bukti runtime sebelum finalisasi.

## 5. Prioritas dan deferred work

1. Jalankan runner langkah 11 dan walkthrough; perbaiki failure critical flow
   yang ditemukan; catat hasil dan commit yang diuji.
2. Prioritaskan timeout fetch R03 setelah baseline runtime tervalidasi. Jika
   ditunda karena timebox, catat alasan dan dampaknya pada dokumen ini.
3. Lengkapi review pribadi, deklarasi ownership, sumber bantuan, serta evidence.
4. Phase 2 menunggu Change Request assessor. SSO/provisioning/notification
   nyata, cloud deployment, kompleks RBAC, advanced search/pagination, dan
   production observability berada di luar scope MVP.

Target assessment adalah 4–8 jam effective work untuk Phase 1, Phase 2, dan
dokumentasi. Bila timebox tercapai, pertahankan state runnable/stable dan
jelaskan bagian yang belum selesai serta alasan prioritasnya.

## 6. Finalisasi pengembang

- Commit source yang direview/diuji: [isi hash]
- Tanggal review pribadi: [isi tanggal aktual]
- Hasil build, C#, API, hosting, dan frontend/skip: [isi atau tautkan catatan]
- Hasil manual browser QA: [isi atau tautkan catatan]
- Tindak lanjut R03: [perbaikan dan hasil uji / deferred dengan alasan]
- Finding tambahan: [isi bila ada]
- Waktu efektif dan pekerjaan tertunda: [isi berdasarkan kondisi aktual]

Sesuaikan setiap status dengan bukti, lengkapi INTEGRITY.md, dan catat
keterbatasan yang tersisa. Tag phase-1-complete dibuat setelah Definition of
Done Phase 1 terpenuhi. Ubah status DRAFT setelah review/evidence dilengkapi.
