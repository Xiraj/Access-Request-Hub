# Access Request Hub

MVP pengajuan akses aplikasi internal dengan workflow approval dan audit trail.
Project dibuat untuk Phase 1 engineering assessment menggunakan ASP.NET Core
dan SQLite, serta berjalan lokal dengan seeded users.

Critical flow: **create request → Manager approval → System Owner approval
jika high-risk → Approved/Rejected → audit timeline**.

## 1. Fitur dan aturan utama

- User switcher, create request form, My Requests, Approval Inbox, detail,
  dan audit timeline.
- Applications: CRM dan Finance Portal. Environment: NonProduction/Production.
  Access level: Read/Admin.
- Semua request memerlukan Manager approval terlebih dahulu.
- **High-risk = Production ATAU Admin**; setelah Manager approve, request
  menunggu System Owner aplikasi terkait.
- Request standar menjadi Approved setelah Manager approve.
- Reject memerlukan reason. Approved dan Rejected merupakan terminal states.
- Backend memeriksa visibility, assigned approver, tahap, dan larangan self-approval.
- Create idempotent berdasarkan ClientRequestId; payload/actor berbeda dengan
  key sama menghasilkan conflict.
- Expected Version melindungi approval/rejection dari stale atau concurrent action.
- Request menyimpan PolicyVersion v1 dan snapshot manager/owner. Audit ditulis
  dalam transaksi yang sama dengan perubahan state dan append-only pada normal flow.
- Loading, empty, error, serta conflict state ditangani pada frontend.

## 2. Tech stack dan prasyarat

| Komponen | Teknologi |
| --- | --- |
| Backend | ASP.NET Core Minimal API, target .NET 10 |
| Database | SQLite melalui Microsoft.Data.Sqlite 10.0.x |
| Akses data | SQL berparameter dan transaksi eksplisit |
| Frontend | HTML, CSS, JavaScript; assets di wwwroot |
| Test service/database | Console runner C# dengan SQLite temporary |
| Test API/hosting | PowerShell dan HttpClient terhadap server ASP.NET Core |
| Test JavaScript | Node.js dengan DOM/HTTP simulasi |

Prasyarat:

- .NET 10 SDK untuk restore, build, run, dan pemeriksaan C#.
- Git untuk mengambil repository.
- Windows PowerShell 5.1+ untuk contoh script pengujian Windows.
- Node.js 20+ untuk frontend checks; runner menyediakan opsi skip eksplisit.

Periksa environment:

```powershell
dotnet --version
git --version
$PSVersionTable.PSVersion
```

Jika akan menjalankan frontend checks, periksa juga `node --version`.
Contoh command di README menggunakan PowerShell dan dijalankan dari root repository.

## 3. Setup dan menjalankan aplikasi

Ambil source dan build:

```powershell
git clone https://github.com/Xiraj/Access-Request-Hub.git
cd Access-Request-Hub

dotnet restore AccessRequestHub.sln
dotnet build AccessRequestHub.sln --no-restore
```

Jika repository sudah tersedia, gunakan folder yang berisi AccessRequestHub.sln.
Hentikan aplikasi yang sedang berjalan dengan Ctrl+C sebelum melakukan build ulang.

Jalankan aplikasi:

```powershell
dotnet run --project src/AccessRequestHub --no-launch-profile --urls http://127.0.0.1:5080
```

| URL | Fungsi |
| --- | --- |
| http://127.0.0.1:5080/ | Dashboard dan user switcher |
| http://127.0.0.1:5080/api/health | Readiness database, migration, dan jumlah record |

Biarkan terminal server tetap aktif selama memakai browser. Gunakan Ctrl+C
untuk menghentikannya. Buka frontend melalui server ASP.NET Core pada URL
tersebut agar assets dan API menggunakan origin yang sama.

## 4. Migration, seed, dan database

Migration berada pada `src/AccessRequestHub/migrations/001_initial.sql`.
Startup aplikasi memanggil migration sebelum menerima request, membuat schema
serta seed pada database baru, dan mencatat version `001_initial` pada
schema_migrations. Menjalankan kembali aplikasi mempertahankan data yang sudah ada.

Path default adalah `data/hub.sqlite3`, relatif terhadap ContentRoot aplikasi.
Dengan command run di atas, database berada di folder data pada project
src/AccessRequestHub. Folder migrations perlu tersedia pada source/output
sesuai konfigurasi copy di csproj.

Seed menghasilkan **5 users** dan **2 applications**. Jumlah requests dan
audit_events pada health bertambah setelah demo atau test API standalone.

Untuk mencoba database baru tanpa mengganti database demo, hentikan server
sebelumnya lalu gunakan path temporary unik:

```powershell
$freshDirectory = Join-Path ([IO.Path]::GetTempPath()) ("accesshub-demo-" + [Guid]::NewGuid().ToString("N"))
$env:HUB_DB_PATH = Join-Path $freshDirectory "hub.sqlite3"

dotnet run --project src/AccessRequestHub --no-launch-profile --urls http://127.0.0.1:5080
```

Setelah selesai, hentikan server dengan Ctrl+C dan kembalikan konfigurasi
terminal ke path default:

```powershell
Remove-Item Env:HUB_DB_PATH -ErrorAction SilentlyContinue
```

HUB_DB_PATH mempunyai prioritas atas konfigurasi Hub:DatabasePath dan path
default. Runner Test-Step11 menetapkan variabel tersebut hanya pada child process.
File database lokal, bin, dan obj diabaikan Git; migration/seed SQL disimpan
bersama source.

## 5. Demo users

Authentication disimulasikan melalui dropdown Demo user yang mengirim header
X-User-Id. Backend memvalidasi seeded identity dan authorization untuk setiap
business operation. Pilihan user frontend disimpan per tab.

| User / ID | Email | Tanggung jawab |
| --- | --- | --- |
| Alice / alice | alice@example.local | Requester, reports to Bob |
| Bob / bob | bob@example.local | Manager untuk Alice |
| Carol / carol | carol@example.local | System Owner CRM |
| Dana / dana | dana@example.local | System Owner Finance Portal |
| Erin / erin | erin@example.local | Auditor, dapat membaca seluruh request; tidak otomatis menjadi approver |

Hanya Alice memiliki manager pada seed ini, sehingga tombol New request aktif
untuk Alice. My Requests menampilkan milik current user; Approval Inbox
menampilkan request yang sedang menunggu tindakan user tersebut. Erin dapat
membuka Audit overview. Angka ringkasan mengikuti daftar yang dipilih.

## 6. Demo flow

Pilih user melalui dropdown, buat request sebagai Alice, lalu pindah ke user
approver dan buka request dari Approval Inbox melalui tombol View.

| Skenario | Langkah | Hasil |
| --- | --- | --- |
| Standard | Alice: CRM / NonProduction / Read; Bob approve | Approved, version 2, dua audit event |
| Production | Alice: CRM / Production / Read; Bob approve; Carol approve | AwaitingOwner setelah Bob, lalu Approved version 3, tiga audit event |
| Admin | Alice: Finance Portal / NonProduction / Admin; Bob approve; Dana approve | AwaitingOwner setelah Bob, lalu Approved |
| Rejection | Alice membuat request; Bob reject dengan reason | Rejected; reason tersedia pada audit; state terminal |
| Owner rejection | Request high-risk sudah disetujui Bob; owner reject dengan reason | Rejected pada tahap Owner; reason dan actor tersedia pada audit |
| Auditor | Erin membuka Audit overview dan detail | Seluruh request dapat dibaca; action approver tidak tersedia |

Untuk demo conflict, buat request standar baru dan buka detail yang sama
sebagai Bob pada dua tab sebelum mengambil keputusan. Approve pada tab
pertama, lalu coba approve pada tab kedua tanpa refresh. Tab kedua menampilkan
conflict dan memuat ulang status terbaru; hanya satu decision audit ditambahkan.

Pemeriksaan API juga mencakup unauthorized approval, self-approval, duplicate
submit, dan simultaneous decision. Periksa loading/empty/error state,
keyboard/dialog, layout mobile, serta Console browser saat walkthrough.

## 7. Automated tests

### Semua kelompok pemeriksaan

Hentikan server demo sebelum build, lalu jalankan:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Step11.ps1
```

Runner melakukan build, menjalankan service/database checks dan frontend
checks, lalu memulai server ASP.NET Core pada port 5081 dengan database baru
di temporary directory OS. Server dan temporary directory milik runner
dibersihkan setelah selesai.

| Kelompok | Target jumlah | Ruang lingkup |
| --- | --- | --- |
| tests/AccessRequestHub.Tests | 23 | HubService/HubDatabase asli, migration, workflow, authorization, idempotency, concurrency, constraints, audit rollback |
| scripts/Test-Step09.ps1 | 33 | HTTP/API regression dengan seeded identity dan concurrent calls |
| scripts/Test-Step11.ps1 | 7 smoke checks | Database baru, seed, HTML/JS/CSS/favicon, JSON 404, dan record setelah regresi |
| scripts/Test-Step10.mjs | 10 | Perilaku JavaScript dengan DOM/HTTP simulasi |

Angka tersebut adalah **target**, bukan pernyataan bahwa test telah lulus.
Runner gagal jika build atau pemeriksaan gagal. Test C# mengembalikan exit
code 0 ketika semua kasus berhasil, atau 1 bila ada yang gagal.

Jika Node.js belum tersedia, gunakan skip eksplisit dan catat sebagai SKIPPED:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Step11.ps1 -SkipFrontend
```

Untuk port alternatif:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Step11.ps1 -Port 5082
```

### Menjalankan test terpisah

Service/database checks menggunakan console runner C#:

```powershell
dotnet run --project tests/AccessRequestHub.Tests --no-launch-profile
```

Setiap kasus memakai database fixture temporary terpisah, termasuk trigger
kegagalan untuk memverifikasi rollback audit. Concurrent calls dijalankan pada
task terpisah dengan gerbang mulai bersama.

Frontend JavaScript checks:

```powershell
node --test .\scripts\Test-Step10.mjs
```

Untuk API checks terpisah, jalankan aplikasi pada port 5080 di terminal pertama,
kemudian jalankan pada terminal kedua:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Step09.ps1
```

Mode API standalone menambah data ke database server aktif. Mode Test-Step11
memakai database temporary. Test JavaScript memakai simulasi; walkthrough
browser tetap diperlukan untuk native dialog, layout, keyboard, dan CSP runtime.

Catat environment, commit yang diuji, command, hasil lulus/gagal/skipped, dan
temuan pada [docs/STEP_11_VALIDATION.md](docs/STEP_11_VALIDATION.md).

## 8. Kontrak API ringkas

Semua business endpoint memerlukan satu header `X-User-Id` berisi seeded ID,
misalnya alice atau bob. Health dapat diakses tanpa header tersebut.
JSON menggunakan nama field snake_case; field yang tidak dikenal ditolak.

| Method | Path | Fungsi |
| --- | --- | --- |
| GET | /api/health | Readiness dan record counts |
| GET | /api/bootstrap | Users, applications, environment, access levels, dan policy version |
| GET | /api/requests?scope=mine | Request milik current user |
| GET | /api/requests?scope=inbox | Request yang menunggu current approver |
| GET | /api/requests?scope=all | Daftar seluruh request untuk auditor |
| GET | /api/requests/{id} | Detail dan audit timeline |
| POST | /api/requests | Create atau idempotent replay |
| POST | /api/requests/{id}/approve | Approve dengan expected version |
| POST | /api/requests/{id}/reject | Reject dengan expected version dan reason |

Create body berisi client_request_id, application_id, environment, access_level,
dan justification. Requester ditentukan backend. Create baru mengembalikan
201; replay identik mengembalikan 200 dengan replayed=true.

Approve body: `{"version":1}`. Reject body:
`{"version":1,"reason":"Scope terlalu luas"}`. Version berupa JSON integer.

Error utama: 400 untuk JSON/binding, 401 untuk identity, 403 untuk forbidden,
404 untuk not found, 409 untuk conflict, 422 untuk business validation,
413/415 untuk ukuran/content type, dan 503 untuk database busy.
Error menyertakan code/message dan request_id; header X-Request-Id membantu
penelusuran log. UI tidak menjadi batas authorization.

## 9. Struktur source dan dokumen

| Path | Tanggung jawab |
| --- | --- |
| AccessRequestHub.sln | Solution aplikasi dan project test |
| src/AccessRequestHub/Program.cs | Startup, middleware, endpoints, static hosting |
| src/AccessRequestHub/Domain | Business service, commands, DTO, dan API errors |
| src/AccessRequestHub/Infrastructure | Koneksi SQLite, migration, dan transaksi |
| src/AccessRequestHub/migrations | Schema, constraints, triggers, dan demo seed |
| src/AccessRequestHub/wwwroot | HTML, CSS, JavaScript, dan favicon |
| tests/AccessRequestHub.Tests | Service/database integration checks |
| scripts | Test-Step09.ps1, Test-Step10.mjs, Test-Step11.ps1 |
| docs | Catatan verifikasi dan bahan review |

Dokumen engineering:

- [PLAN.md](PLAN.md): pemahaman masalah, arsitektur, urutan implementasi,
  strategi test, trade-offs, dan perubahan plan.
- [AI_USAGE.md](AI_USAGE.md): interaksi AI yang berpengaruh, keputusan,
  koreksi, dan status verifikasi.
- [REVIEW.md](REVIEW.md): findings, severity, action, status, evidence,
  known limitations, dan deferred work.
- [INTEGRITY.md](INTEGRITY.md): ownership declaration serta tools/bantuan/sumber.

## 10. Batas scope dan status verifikasi

Identitas disimulasikan untuk demo lokal. Server dicontohkan dengan binding
loopback dan memeriksa Host/Origin. SQLite mempunyai satu writer aktif;
transaksi, unique constraint, expected Version, dan timeout/retry digunakan
untuk menjaga consistency.

SSO, provisioning, email nyata, cloud deployment, kompleks RBAC, serta
advanced search/pagination berada di luar scope MVP. Phase 2 menunggu
dokumen Change Request dari assessor.

Pada environment pembuat paket, 10 JavaScript simulation checks dan 6
pemeriksaan SQL/schema tambahan telah lulus. Build C#, eksekusi PowerShell,
API/hosting nyata, dan manual browser QA perlu dilengkapi dengan hasil aktual
pengembang pada dokumen validasi. README ini belum mencatat hasil akhir
runner langkah 11 dari komputer pengembang.

Git history dipertahankan. Assessment meminta tag assessment-start sebelum
implementasi utama dan phase-1-complete setelah Phase 1 selesai serta tests
dijalankan. Finalisasi tag/evidence mengikuti kondisi repository dan hasil
review aktual.

## 11. Troubleshooting

| Gejala | Tindakan |
| --- | --- |
| dotnet tidak dikenali | Pasang .NET 10 SDK, buka terminal baru, periksa dotnet --version |
| MSB3027/MSB3021 dan executable terkunci | Hentikan dotnet run dengan Ctrl+C, lalu ulangi build/test |
| Solution already contains project | Project test sudah terdaftar; lanjutkan pengujian |
| Port 5081 dipakai | Jalankan Test-Step11.ps1 dengan -Port 5082 |
| Node atau Test-Step10.mjs tidak tersedia | Pasang Node/salin script; atau gunakan -SkipFrontend dan catat SKIPPED |
| Dashboard 404 atau masih JSON lama | Periksa Program.cs langkah 10 dan wwwroot, lalu restart server |
| CSS/JS belum diperbarui | Gunakan Ctrl+F5 di browser |
| Business endpoint 401 dari address bar | Gunakan frontend user switcher atau HTTP client dengan X-User-Id |
| New request nonaktif | Pilih Alice yang mempunyai manager pada demo seed |
| git push: Could not resolve host | Periksa koneksi dan DNS ke github.com; ulangi push setelah koneksi pulih |
