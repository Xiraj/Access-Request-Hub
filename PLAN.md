# Implementation Plan

Project: Access Request Hub — Phase 1  
Technology: ASP.NET Core (.NET 10), SQLite, HTML/CSS/JavaScript  
Tanggal pembaruan: 16 Oktober 2026

Dokumen ini melengkapi kerangka PLAN.md yang sebelumnya telah dibuat.
Detail rencana dan strategi verifikasi diisi pada tahap pengerjaan langkah 10.
Hasil pengujian aktual dicatat secara terpisah pada dokumen validasi.

## 1. Problem Understanding

Permintaan akses melalui email/chat sulit dilacak, dapat terkirim dua kali,
dan berisiko diproses bersamaan oleh approver. Project ini menyediakan MVP
lokal untuk create request, approval/rejection, dan audit trail dalam satu alur.

Request dimiliki current user. Semua request memerlukan persetujuan Manager.
Request berisiko tinggi, yaitu Production ATAU Admin, juga memerlukan
persetujuan System Owner setelah Manager menyetujui.

Prioritas engineering adalah ketepatan workflow, authorization di backend,
idempotency, perlindungan terhadap keputusan bersamaan, audit yang konsisten,
dan aplikasi yang dapat dijalankan ulang dari source serta migration/seed.

## 2. Scope Phase 1

- User switcher dengan seeded users: Alice, Bob, Carol, Dana, dan Erin.
- Applications: CRM dengan owner Carol; Finance Portal dengan owner Dana.
- Environment: NonProduction dan Production. Access level: Read dan Admin.
- Create request dengan justification wajib dan ClientRequestId dari client.
- My Requests, Approval Inbox, detail, dan audit timeline.
- Standard flow: AwaitingManager menjadi Approved setelah Manager approve.
- High-risk flow: AwaitingManager menjadi AwaitingOwner, lalu Approved.
- Reject dengan reason wajib; Approved dan Rejected bersifat terminal.
- Self-approval dan action oleh approver yang tidak ditugaskan ditolak backend.
- Retry create tidak menambah request; stale/concurrent decision menghasilkan conflict.
- Setiap request Phase 1 menyimpan PolicyVersion v1.
- Loading, empty, error, dan conflict state untuk critical frontend flow.
- Migration/seed, automated checks, panduan run, dan dokumen engineering.

SSO, provisioning, notifikasi nyata, cloud deployment, administrasi RBAC
kompleks, dan fitur pencarian/pagination lanjutan berada di luar scope MVP.
Phase 2 menunggu dokumen Change Request dari assessor.

## 3. Technology Stack

| Komponen | Pilihan dan tujuan |
| --- | --- |
| Runtime/backend | .NET 10 dan ASP.NET Core Minimal API |
| Database | SQLite melalui Microsoft.Data.Sqlite |
| Akses data | SQL berparameter dan transaksi eksplisit |
| Frontend | HTML, CSS, JavaScript tanpa framework tambahan |
| Frontend hosting | wwwroot, disajikan aplikasi ASP.NET Core yang sama |
| Version control | Git dan repository GitHub |
| Pemeriksaan API | scripts/Test-Step09.ps1 saat server berjalan |
| Pemeriksaan frontend | scripts/Test-Step10.mjs; Node.js opsional untuk menjalankan checks |

## 4. Architecture and Data Model

Browser mengakses frontend dan API pada origin yang sama. User switcher
mengirim identitas demo melalui X-User-Id. Backend memvalidasi identitas
tersebut dan menentukan hak membaca atau mengambil keputusan.

| Komponen | Tanggung jawab |
| --- | --- |
| Program.cs | Konfigurasi aplikasi, middleware, HTTP endpoints, static files, dan error responses |
| Domain/HubService.cs | Validasi, authorization, idempotency, dan aturan transisi workflow |
| Infrastructure/HubDatabase.cs | Koneksi SQLite, migration, dan transaksi |
| migrations/001_initial.sql | Schema, seed, constraints, dan audit triggers |
| wwwroot | Form, daftar, detail, timeline, dan penanganan respons API |

| Entitas | Data dan hubungan utama |
| --- | --- |
| users | ID, nama, email, role, dan hubungan manager_id ke user |
| applications | ID, nama, dan owner_id ke user System Owner |
| access_requests | ClientRequestId, requester, snapshot manager/owner, application, environment, access level, justification, status, policy version, concurrency version, dan waktu |
| audit_events | Request, actor, action, status sebelum/sesudah, reason, request version, policy version, dan waktu |
| schema_migrations | Riwayat migration yang sudah diterapkan |

Primary/foreign keys dan constraints menjaga hubungan serta invariant data.
ClientRequestId mempunyai unique constraint. Create/replay menggunakan
transaksi untuk membandingkan actor dan payload. Decision memeriksa expected
Version dan melakukan conditional update. Audit triggers berjalan dalam
transaksi perubahan state; audit dibatasi append-only pada normal application flow.

Error API membedakan validation, identity, forbidden, not found, dan conflict.
Basic logging menyertakan request ID tanpa mencatat justification/reject reason.

## 5. Implementation Order

1. Siapkan repository, solution/project ASP.NET Core, .gitignore, dan kerangka
   dokumen. Pelihara Git history serta penanda assessment-start sesuai waktu
   implementasi yang sebenarnya.
2. Susun schema, migration, seed, dan koneksi SQLite. Pastikan startup dapat
   membangun database baru dan tidak mengulang seed pada database yang sama.
3. Implementasikan API bootstrap, create, list/detail, inbox, approve, dan reject.
4. Terapkan validation, authorization, idempotency, expected Version,
   terminal-state protection, dan audit atomic.
5. Hubungkan frontend ke API: user switcher, form, list, detail, dan timeline.
   Tangani loading/empty/error, double submit, retry, serta conflict.
6. Jalankan build, API checks, dan walkthrough browser. Periksa business rules
   serta skenario race/failure; perbaiki temuan sebelum menganggapnya selesai.
7. Lengkapi README.md, AI_USAGE.md, REVIEW.md, INTEGRITY.md, dan catatan
   validasi. Periksa reproducibility dari database baru.
8. Setelah Phase 1 selesai dan tests dijalankan, buat tag phase-1-complete.
   Lanjutkan Phase 2 hanya setelah Change Request diterima.

Target keseluruhan assessment adalah 4–8 jam effective work, termasuk
Phase 2 dan dokumentasi. Jika timebox tercapai, pertahankan state yang stabil
dan catat pekerjaan tertunda serta prioritas di REVIEW.md.

## 6. Test Strategy

| Area | Skenario dan expected result |
| --- | --- |
| Standard flow | Alice membuat CRM / NonProduction / Read; Bob approve menghasilkan Approved |
| Production | Alice membuat CRM / Production / Read; Bob approve menghasilkan AwaitingOwner; Carol approve menghasilkan Approved |
| Admin | Alice membuat Finance Portal / NonProduction / Admin; Bob approve menunggu Dana |
| Rejection | Reason kosong ditolak; reason valid tersimpan pada audit; state Rejected terminal |
| Authorization | Non-assigned approver, self-approval, dan wrong-stage action ditolak backend |
| Identity/validation | User tidak valid, payload requester palsu, dan input invalid ditolak |
| Idempotency | Retry identik dan simultaneous create dengan ID sama menghasilkan satu request; payload berbeda menghasilkan conflict |
| Concurrency | Dua decisions pada Version sama: satu transition berhasil dan action lain conflict |
| Audit | Jumlah/isi audit sesuai transition; failure tidak meninggalkan state tanpa audit; update/delete audit ditolak |
| Reproducibility | Database baru dapat dibuat dari migration/seed; restart tidak menggandakan master data |
| Frontend | User switch, list/detail, form validation, empty/error state, retry, dan stale-version message |

Command dari root repository:

```powershell
dotnet build AccessRequestHub.sln
dotnet run --project src/AccessRequestHub --no-launch-profile --urls http://127.0.0.1:5080
```

Saat server berjalan, pada terminal kedua:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Step09.ps1
```

Jika Node.js 20+ tersedia:

```powershell
node --test .\scripts\Test-Step10.mjs
```

Script frontend menggunakan DOM dan HTTP simulasi. Pemeriksaan integrasi
backend dan walkthrough browser tetap diperlukan. Skenario audit rollback
dan database baru yang belum tercakup script perlu diuji dan didokumentasikan
pada tahap pengujian menyeluruh.

Simpan hasil aktual, tanggal, environment, command, dan temuan pada dokumen
validasi. Status lulus diisi setelah pemeriksaan dilakukan pada project yang
akan diserahkan. Cantumkan command test dan demo flow dalam README.md.

## 7. Trade-offs

| Keputusan | Manfaat | Konsekuensi |
| --- | --- | --- |
| SQLite untuk demo lokal | Setup ringan, relational constraints dan transaksi tersedia | Satu writer aktif; lock timeout harus ditangani, dan kapasitas multi-user perlu dievaluasi bila scope membesar |
| SQL langsung dan migration script dibanding ORM | Transaksi, conditional update, constraint, dan audit trigger terlihat eksplisit | Mapping data dan perubahan schema lebih banyak dikelola manual |
| Satu aplikasi ASP.NET Core untuk API dan frontend tanpa framework | Satu proses/origin, dependensi dan setup lebih sederhana untuk MVP | Pengelolaan DOM, dialog, serta asynchronous state dilakukan manual dan perlu diverifikasi |

## 8. Plan Changes

| Perubahan/penajaman | Alasan | Dampak |
| --- | --- | --- |
| Tech stack ditetapkan ASP.NET Core sesuai revisi kebutuhan | Mengikuti stack yang dipilih untuk assessment | Solution/project dan implementasi backend menggunakan .NET 10 |
| Pengerjaan dipecah menjadi database langkah 8, backend langkah 9, dan frontend langkah 10 | Memudahkan pemasangan, pemeriksaan, dan commit bertahap | Setiap tahap memiliki panduan dan verifikasi yang dilanjutkan dengan pengujian menyeluruh |
| Retry frontend menyimpan ClientRequestId/payload, dan respons lama diabaikan setelah user/detail berganti | Mengurangi duplikasi serta tampilan data dari konteks user yang sudah berubah | Perlu menguji respons hilang, double submit, dan asynchronous state |
| Verifikasi pembuat paket dibedakan dari verifikasi pada komputer pengembang | Environment pembuat paket tidak memiliki SDK .NET dan browser executable | Hasil simulasi JavaScript dicatat terpisah dari build, API, dan browser test pada aplikasi nyata |
| Detail PLAN.md dilengkapi setelah kerangka awal | Kerangka sebelumnya baru berisi judul bagian | Pembaruan ini dicatat pada commit saat dokumen dilengkapi; hasil review dan testing mengikuti bukti aktual |

Tambahkan perubahan berikutnya beserta alasan dan dampaknya sesuai kejadian.
Jangan menandai seluruh Phase 1 selesai sebelum critical flow, tests,
reproducibility, dan dokumen pendukung telah diperiksa.
