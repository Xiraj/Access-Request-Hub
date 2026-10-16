# Verifikasi langkah 11

## Pemeriksaan pada environment pembuat paket

Tanggal: 16 Oktober 2026.

| Pemeriksaan | Hasil |
| --- | --- |
| Struktur csproj dan ProjectReference | XML valid; target .NET 10; referensi aplikasi dan migration ditinjau |
| Registrasi test C# | 23 kasus; method yang direferensikan tersedia; belum dikompilasi/dijalankan |
| Review runner PowerShell | Tujuh smoke assertions; hanya memulai/menghentikan proses test miliknya; database temporary; belum diparse/dijalankan di PowerShell |
| SQL/schema tambahan pada SQLite | Enam pemeriksaan lulus; rincian di bawah |
| JavaScript langkah 10 | Salinan script pada paket dijalankan ulang terhadap assets langkah 10 dalam layout repository sementara: 10 lulus, 0 gagal; DOM/HTTP simulasi |
| Build/run C#, runner PowerShell, API dan hosting nyata | Belum dijalankan; .NET SDK dan PowerShell tidak tersedia |
| Browser/manual QA | Belum dijalankan pada environment pembuat paket |

Enam pemeriksaan SQLite menggunakan schema langkah 8 dan query INSERT/UPDATE
yang diekstrak dari HubService langkah 9:

1. Schema baru, seed lima user/dua aplikasi, dan foreign key aktif.
2. Audit decision gagal → state/version rollback; retry berhasil setelah trigger kegagalan dihapus.
3. Audit create gagal → request/audit rollback; ClientRequestId sama dapat dicoba kembali.
4. Audit update/delete dan perubahan immutable request facts ditolak.
5. Dua raw INSERT bersamaan → satu create, satu unique-constraint failure, satu submit audit.
6. Dua conditional UPDATE pada version sama → satu row berubah dan hanya satu decision audit.

Pemeriksaan SQL ini tidak menjalankan HubService C#, HTTP, ataupun middleware.
Unique-constraint failure pada raw SQL bukan bukti respons idempotent replay API.
Hasil runtime aplikasi tetap perlu diperoleh dari command di bawah.

## Hasil aktual pengembang

- Tanggal/waktu: ...
- Commit/revisi source yang diuji: ...
- Output dotnet --version: ...
- PowerShell version: ...
- Node.js version atau alasan skip: ...
- Browser/version: ...
- Command: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Step11.ps1
- Opsi tambahan bila dipakai: ...
- Exit code/hasil akhir: ...

| Kelompok | Target | Hasil aktual |
| --- | --- | --- |
| Build aplikasi dan project test | Berhasil | Belum dicatat |
| C# service/database checks | 23/23 | Belum dicatat |
| API regression | 33 checks | Belum dicatat |
| Hosting/database smoke | 7 checks | Belum dicatat |
| Frontend JavaScript simulasi | 10 checks atau SKIPPED dengan alasan | Belum dicatat |

## Manual browser QA

- [ ] Standard: Alice → Bob → Approved; dua audit event.
- [ ] Production: Alice → Bob → Carol → Approved; tiga audit event.
- [ ] Admin: Finance Portal → Bob → Dana → Approved.
- [ ] Reason reject wajib; reason dan actor tercatat pada timeline.
- [ ] Approved/Rejected tidak menyediakan keputusan lanjutan.
- [ ] Erin dapat melihat semua request dan tidak mendapat action approver.
- [ ] Dua tab dengan expected Version lama menunjukkan conflict dan current status.
- [ ] Loading/empty/error state dapat dipahami; retry setelah server kembali berfungsi.
- [ ] Keyboard, dialog, desktop/mobile, dan scroll tabel diperiksa.
- [ ] Console browser diperiksa untuk JavaScript/CSP error.

## Temuan dan tindak lanjut

| Finding | Severity | Action | Status | Evidence |
| --- | --- | --- | --- | --- |
| [Isi temuan aktual] | ... | ... | ... | ... |

Setelah memperbaiki kode, tulis commit/source yang baru diuji dan hasil ulang
yang relevan. Bedakan hasil lulus, gagal, skipped, dan belum dijalankan.
