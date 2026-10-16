## Testing

Gunakan .NET 10 SDK dan PowerShell. Hentikan aplikasi sebelum build agar
output executable dapat diganti. Node.js 20+ digunakan untuk frontend checks.

Jalankan dari root repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Step11.ps1
```

Runner membangun test project beserta aplikasi, menjalankan 23 service/database
checks, memeriksa frontend JavaScript, lalu menyalakan server ASP.NET Core
terisolasi untuk 33 API checks dan 7 hosting/database smoke checks. Database
temporary dan server milik runner dibersihkan setelah selesai.

Jika Node.js belum tersedia, skip frontend unit checks secara eksplisit dan
catat hasil SKIPPED:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Step11.ps1 -SkipFrontend
```

Pilih port lain jika 5081 sedang dipakai, misalnya tambahkan `-Port 5082`.

Untuk menjalankan kelompok test terpisah:

```powershell
dotnet run --project tests/AccessRequestHub.Tests --no-launch-profile
node --test .\scripts\Test-Step10.mjs
```

Project AccessRequestHub.Tests menggunakan console runner dengan exit code
0/1; gunakan dotnet run. Test service menjalankan HubService dan HubDatabase
asli terhadap SQLite temporary, tanpa menjalankan web server.

Untuk pemeriksaan API terpisah, jalankan server pada terminal pertama:

```powershell
dotnet run --project src/AccessRequestHub --no-launch-profile --urls http://127.0.0.1:5080
```

Pada terminal kedua:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Step09.ps1
```

Mode API terpisah ini menambah data ke database demo server aktif. Runner
Test-Step11 menggunakan database temporary untuk pemeriksaan otomatisnya.

Walkthrough browser mencakup standard, Production, Admin, reject reason,
auditor visibility, stale decision dua tab, loading/empty/error, dan penggunaan
keyboard/mobile. Hasil aktual beserta environment/commit dicatat pada
docs/STEP_11_VALIDATION.md. Angka target pemeriksaan tidak dianggap lulus
sebelum script dan walkthrough benar-benar dijalankan.
