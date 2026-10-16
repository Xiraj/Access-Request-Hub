# AI Usage Log

Project: Access Request Hub — Phase 1  
Periode catatan: sampai tahap frontend/langkah 10, pembaruan dokumentasi, dan klarifikasi status script pengujian  
Terakhir diperbarui: 16 Oktober 2026

## 1. Tools dan Batas Catatan

ChatGPT, termasuk bantuan coding agent, digunakan untuk pemahaman requirement,
planning, arsitektur, code generation, SQL/migration, debugging, penyusunan
pemeriksaan, review bantuan AI, dan dokumentasi.

Bantuan mencakup bagian besar rancangan dan kode backend/frontend, bukan hanya
perbaikan sintaks. Kode diberikan melalui paket langkah database, backend, dan
frontend untuk dipasang pada repository pengembang.

Catatan ini disusun dari percakapan dan paket yang sudah disiapkan. Keputusan
di bawah menggambarkan arah implementasi yang dilanjutkan dalam pendampingan.
Pengembang perlu menyesuaikan status adopsi jika isi repository akhirnya
berbeda. Review pribadi dan hasil runtime di komputer pengembang dicatat
berdasarkan bukti aktual.

## 2. Lima Interaksi AI yang Paling Berpengaruh

### 1. Memecah assessment menjadi tahapan dan menetapkan ASP.NET Core

- **Ask:** Jelaskan urutan pengerjaan assessment dari membuat project,
  repository GitHub, PLAN.md, dan seterusnya; revisi tech stack menjadi ASP.NET Core.
- **Suggestion:** Gunakan solution dan project ASP.NET Core .NET 10, SQLite
  untuk demo lokal, serta frontend statis pada aplikasi yang sama. Kerjakan
  scaffold, database, backend, frontend, pengujian, dan dokumentasi secara bertahap.
- **Accepted/changed/rejected:** Changed — arah implementasi disesuaikan ke
  ASP.NET Core sesuai pilihan pengembang. SQLite dan frontend HTML/CSS/JavaScript
  dipertahankan sebagai rancangan MVP yang dipandu sampai langkah 10.
- **Why:** Mengikuti stack yang dipilih dan menjaga setup lokal serta dependensi
  tetap sederhana untuk scope assessment.

### 2. Database, migration/seed, dan batas integritas data

- **Ask:** Lanjutkan langkah database, jelaskan konfigurasi csproj untuk file
  SQL, dan tentukan file yang perlu diabaikan Git.
- **Suggestion:** Gunakan Microsoft.Data.Sqlite, migration 001_initial.sql,
  seed lima user dan dua aplikasi, primary/foreign keys, unique ClientRequestId,
  constraints, serta audit triggers. Salin migration ke output/publish melalui csproj.
- **Accepted/changed/rejected:** Accepted sebagai baseline database langkah 8.
  Konfigurasi ItemGroup harus berada di dalam elemen Project. File build dan
  database lokal diabaikan; file SQL migration/seed tetap disimpan dalam Git.
- **Why:** Environment baru harus dapat membangun database yang sama, dan
  invariant penting perlu dilindungi pada lapisan database.

### 3. Backend workflow, authorization, idempotency, dan concurrency

- **Ask:** Lanjutkan implementasi backend/API setelah database disiapkan.
- **Suggestion:** Sediakan bootstrap, create, list/detail, inbox, approve, dan
  reject. Ambil requester dari seeded identity tervalidasi. Gunakan transaksi,
  unique ClientRequestId dan perbandingan payload untuk retry, expected Version
  serta conditional update untuk keputusan, dan audit dalam transaksi state change.
- **Accepted/changed/rejected:** Accepted sebagai rancangan paket langkah 9.
  Workflow v1 memakai Manager lebih dahulu; Production ATAU Admin dilanjutkan
  ke System Owner. Self-approval, wrong-stage action, reason kosong, dan
  transition dari terminal state ditolak backend. Keberhasilan runtime seluruh
  aturan ini masih perlu dibuktikan pada repository yang akan diserahkan.
- **Why:** Pencegahan double click di UI tidak cukup untuk mencegah request
  duplikat, action tidak sah, atau dua keputusan terhadap version yang sama.

### 4. Frontend yang terhubung dengan API dan pemeriksaan perilakunya

- **Ask:** Lanjutkan ke langkah 10 untuk frontend.
- **Suggestion:** Gunakan wwwroot untuk user switcher, form request, My Requests,
  Approval Inbox, detail, dan audit timeline. Simpan ClientRequestId/payload
  submit yang belum terkonfirmasi, blok double submit, abaikan respons konteks
  user/detail lama, dan muat ulang status setelah conflict. Render input user
  melalui textContent. Tambahkan pemeriksaan JavaScript dengan DOM/HTTP simulasi.
- **Accepted/changed/rejected:** Accepted sebagai baseline paket langkah 10.
  Program.cs diperbarui untuk menyajikan halaman root dan assets. Pengembang
  mencoba build dan melaporkan executable terkunci; hasil build ulang dan
  walkthrough setelah troubleshooting belum dilaporkan dalam catatan ini.
- **Why:** Frontend harus menunjukkan status yang dapat dipahami sekaligus
  mempertahankan idempotency dan penanganan stale data pada critical flow.

### 5. Troubleshooting lokal dan perbaikan evidence engineering

- **Ask:** Jelaskan error build, error Git pathspec docs, waktu push dokumen,
  serta isi PLAN.md, AI_USAGE.md, dan INTEGRITY.md sesuai assessment.
- **Suggestion:** Hentikan proses aplikasi yang mengunci executable sebelum
  rebuild; buat dan isi folder docs sebelum staging; catat perubahan dokumen
  secara bertahap. Lengkapi delapan bagian PLAN.md, lima interaksi penting dan
  Three things AI got wrong pada AI_USAGE.md, serta deklarasi dan sumber bantuan
  pada INTEGRITY.md. Gunakan rename sementara untuk perubahan casing nama file.
- **Accepted/changed/rejected:** Changed — template AI_USAGE yang awalnya hanya
  daftar bantuan diperluas menjadi log keputusan sesuai PDF. Draf PLAN.md dan
  INTEGRITY.md sudah disediakan. Hasil eksekusi setiap command Git/build dan
  finalisasi deklarasi tetap mengikuti keadaan aktual pengembang.
- **Why:** Evidence perlu menjelaskan keputusan, kesalahan, koreksi, dan batas
  verifikasi; keberadaan file saja belum membuktikan engineering ownership.

## 3. Three things AI got wrong

### 1. Panduan .gitignore diberikan terlalu terlambat dalam urutan pengerjaan

- **What went wrong:** Urutan pendampingan membahas .gitignore pada langkah 8,
  setelah scaffold dan commit awal. Build artifacts bin/obj sempat ikut terlacak.
  Menambahkan .gitignore setelah file terlacak tidak otomatis mengeluarkannya
  dari index Git.
- **Correction:** .gitignore dilengkapi dan folder build yang sudah terlacak
  dibersihkan dari index dengan git rm --cached sebelum commit berikutnya.
- **Lesson:** Siapkan ignore rules sebelum build dan staging pertama; periksa
  git status serta staged diff. Jangan menganggap .gitignore menghapus file
  yang sebelumnya sudah masuk history.

### 2. Command staging mengasumsikan folder docs sudah tersedia

- **What went wrong:** AI menyarankan git add yang mencakup docs sebelum
  memastikan file dokumentasi telah disalin ke folder tersebut. Pengembang
  melaporkan: fatal: pathspec 'docs' did not match any files.
- **Correction:** Panduan diperjelas: jalankan dari root repository, buat docs,
  salin STEP_10_VALIDATION.md ke dalamnya, lalu ulangi staging. Git tidak
  menyimpan folder kosong.
- **Lesson:** Command siap salin harus sesuai struktur yang benar-benar ada;
  langkah membuat dan mengisi path perlu dijelaskan sebelum path itu di-stage.

### 3. Template awal AI_USAGE.md belum memenuhi seluruh format PDF

- **What went wrong:** Template yang sempat diberikan hanya merangkum tools,
  jenis bantuan, dan status verifikasi. Template tersebut belum memuat 2–5
  interaksi dengan ask → suggestion → accepted/changed/rejected → why serta
  section Three things AI got wrong.
- **Correction:** Setelah membaca ulang ketentuan assessment, AI mengakui
  kekurangan template. Dokumen ini menggantikannya dengan lima interaksi dan
  tiga koreksi konkret dari pengerjaan.
- **Lesson:** Cocokkan isi minimum setiap deliverable dengan dokumen sumber;
  nama file dan ringkasan umum belum cukup memenuhi requirement evidence.

## 4. Status Verifikasi Sampai Catatan Ini Dibuat

| Pemeriksaan | Bukti/status yang tersedia |
| --- | --- |
| Syntax JavaScript frontend | Lulus pada environment pembuat paket langkah 10 |
| scripts/Test-Step10.mjs pada environment pembuat paket | 10 lulus, 0 gagal pada Node.js 24.19.0; DOM/HTTP disimulasikan |
| scripts/Test-Step10.mjs pada komputer pengembang | Belum dijalankan menurut catatan yang diberikan; script sempat belum disalin ke repository |
| Review Program.cs langkah 10 | Perubahan terhadap langkah 9 terbatas pada hosting static files dan penghapusan endpoint JSON root |
| Pemeriksaan backend langkah 9 | Catatan paket mencakup review statis dan pemeriksaan SQL; tidak dihitung sebagai build atau .NET integration tests |
| Build di komputer pengembang | Pernah gagal dengan MSB3027/MSB3021 karena AccessRequestHub PID 7184 mengunci executable; hasil build ulang belum dilaporkan |
| Build/run .NET pada environment pembuat paket | Belum dijalankan karena SDK .NET tidak tersedia |
| scripts/Test-Step09.ps1 | Script pemeriksaan API yang disediakan pada langkah 9; target 33 HTTP checks; hasil eksekusi aktual belum dicatat di sini |
| Browser dengan server ASP.NET Core nyata | Hasil walkthrough, layout, keyboard/dialog, dan Console/CSP belum dilaporkan di sini |
| Review pribadi pengembang dan finalisasi integrity declaration | Perlu dicatat oleh pengembang berdasarkan review yang benar-benar dilakukan |

Sepuluh pemeriksaan frontend simulasi mencakup struktur HTML/assets, bootstrap
dan empty state, restore demo user, respons list/detail lama, retry submit
setelah respons hilang/reload, double submit, stale version, reject reason,
dan rendering justification sebagai literal text.

Hasil simulasi tersebut tidak menggantikan pengujian backend/integration dan
browser nyata. Klaim dari implementasi atau stack lain tidak dipakai sebagai
bukti bahwa project ASP.NET Core ini telah lulus pengujian.

### Status Test-Step10.mjs

- Script disiapkan dengan bantuan ChatGPT dan disertakan dalam paket langkah 10.
- Sepuluh pemeriksaan simulasi lulus di environment pembuat paket.
- Pengembang melaporkan bahwa script sempat belum disalin ke repository.
  Status penyalinan saat dokumen diperbarui belum dikonfirmasi.
- Hasil pengujian pada komputer pengembang: belum dijalankan menurut catatan
  yang diberikan.
- Setelah script disalin dan dijalankan, tambahkan tanggal, commit yang diuji,
  command, dan hasil aktual pada dokumen ini serta catatan validasi.

Folder scripts yang direncanakan berisi dua file pengujian:

| File | Ruang lingkup | Kebutuhan menjalankan |
| --- | --- | --- |
| scripts/Test-Step09.ps1 | HTTP/API backend; target 33 pemeriksaan | PowerShell dan aplikasi ASP.NET Core yang sedang berjalan |
| scripts/Test-Step10.mjs | JavaScript frontend; 10 pemeriksaan DOM/HTTP simulasi | Node.js 20+; tidak memerlukan server untuk simulasi ini |

## 5. Sumber dan Provenance

- Requirement utama: Enterprise_Fullstack_AI_Assisted_Candidate.pdf yang
  diberikan untuk assessment.
- Scaffold: template bawaan dotnet new web dan dotnet new sln.
- Paket database: Microsoft.Data.Sqlite.
- Frontend diadaptasi dari iterasi AI untuk assessment yang sama dan
  dihubungkan ke kontrak API paket langkah 9.
- Rujukan troubleshooting: dokumentasi resmi Microsoft untuk static files
  ASP.NET Core, MSB3021/MSB3027, dan Stop-Process.
- Source repository, snippet, template tambahan, serta bantuan manusia yang
  digunakan di luar percakapan ini perlu ditambahkan oleh pengembang bila ada.

## 6. Pembaruan oleh Pengembang

Lengkapi berdasarkan keadaan repository dan output aktual:

- Commit/revisi source yang diperiksa: [isi commit hash]
- Hasil build ulang setelah proses aplikasi dihentikan: [isi hasil]
- Hasil script API langkah 9: [isi hasil aktual atau belum dijalankan]
- Status penyalinan scripts/Test-Step10.mjs ke repository: [konfirmasi sudah disalin atau belum]
- Hasil Test-Step10.mjs pada komputer pengembang: belum dijalankan menurut catatan saat ini; perbarui setelah eksekusi aktual.
- Hasil walkthrough standard, Production, Admin, rejection, dan conflict: [isi hasil]
- Saran AI yang akhirnya diubah atau ditolak saat review: [isi perubahan dan alasan, atau belum ada yang dicatat]
- Keterbatasan/pekerjaan tertunda: [catat dan selaraskan dengan REVIEW.md]

Perbarui log saat ada interaksi penting berikutnya. Status selesai dan tag
phase-1-complete mengikuti implementasi, hasil pengujian, dan evidence aktual.
