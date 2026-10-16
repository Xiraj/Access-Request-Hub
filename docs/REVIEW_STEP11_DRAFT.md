# Self-review — bahan pembaruan REVIEW.md

Status: DRAFT. Gabungkan dengan REVIEW.md yang sudah ada dan sesuaikan hasil
berdasarkan repository serta verifikasi pengembang. Status di bawah menunjukkan
informasi yang tersedia saat paket langkah 11 disiapkan.

## Findings

| Finding | Severity | Action | Status | Evidence |
| --- | --- | --- | --- | --- |
| Build pernah gagal karena executable dipakai proses aplikasi; hasil build ulang belum dicatat dalam percakapan | High untuk kesiapan submission | Hentikan proses demo, jalankan runner, catat output build dan commit | Open sampai hasil aktual dicatat | Error MSB3027/MSB3021 yang dilaporkan; docs/STEP_11_VALIDATION.md |
| Test C# dan runner PowerShell baru belum dieksekusi di environment pembuat paket | High untuk kepastian correctness | Jalankan Test-Step11.ps1 pada .NET 10/PowerShell dan perbaiki setiap failure | Open | tests/AccessRequestHub.Tests; scripts/Test-Step11.ps1 |
| Walkthrough browser dan akses keyboard/mobile belum dicatat | Medium | Jalankan demo flow dan manual QA; catat temuan | Open | wwwroot; docs/STEP_11_VALIDATION.md |
| Identitas demo dapat dipilih melalui header/user switcher | High jika digunakan untuk production | Pertahankan scope demo lokal dan loopback binding; implementasi identity nyata di luar scope MVP | Accepted for assessment scope | Program.cs; HubService.cs; README.md |
| Detail PLAN.md dilengkapi setelah kerangka awal | Low untuk provenance | Pertahankan riwayat Git aktual dan jelaskan waktu pengisian detail | Documented; pengembang memeriksa history | PLAN.md; git log |
| Deklarasi integrity dan hasil review pribadi memerlukan konfirmasi pengembang | Medium untuk kelengkapan evidence | Isi data bantuan/sumber serta konfirmasikan deklarasi setelah review | Open sampai finalisasi aktual | INTEGRITY.md; AI_USAGE.md |

## Rancangan kontrol yang perlu diverifikasi

- Current requester berasal dari seeded identity tervalidasi; payload requester bebas ditolak.
- Visibility, assigned stage, dan larangan self-approval diperiksa backend.
- SQL berparameter, enum/length validation, strict JSON, body cap, dan error categories diterapkan.
- ClientRequestId unique, canonical payload comparison, dan immediate transaction menjaga create/replay.
- Expected Version dan conditional UPDATE menjaga keputusan; terminal states tidak ditransisikan lagi.
- Audit trigger berjalan dalam transaksi yang sama; update/delete audit ditolak.
- Snapshot manager/owner dan policy v1 disimpan untuk menjelaskan request lama.
- Detail request dan audit dibaca dalam satu read transaction.
- Frontend menggunakan textContent, menyimpan ID retry, dan mengabaikan respons konteks lama.
- Security headers, loopback Host/Origin, dan logging request ID ditinjau; hasil runtime perlu dicatat.

Kontrol tersebut merupakan hasil review rancangan/source, dengan bukti runtime
yang dilengkapi setelah pengujian. Catat kontrol yang gagal sebagai finding.

## Known limitations

- Simulated identity sesuai demo lokal; autentikasi production belum menjadi bagian MVP.
- SQLite mempunyai satu writer aktif; immediate transaction dapat menunggu lock.
  Busy timeout dan kontrak retry perlu dipahami saat mengevaluasi deployment lebih luas.
- Frontend checks memakai DOM/HTTP simulasi; layout, native dialog, keyboard,
  dan browser security behavior memerlukan manual QA.
- Append-only dijaga pada normal application flow; administrator yang menguasai
  database dapat mengubah schema/trigger.
- Console test runner dijalankan dengan dotnet run; tidak memakai test discovery xUnit/MSTest.
- Setiap pengujian yang belum dijalankan tetap dicatat sebagai unverified.

## Deferred work

- Phase 2 menunggu Change Request assessor.
- SSO/provisioning/notification nyata, cloud deployment, kompleks RBAC, advanced
  search/pagination, dan production observability berada di luar scope assessment.
- Tuliskan pekerjaan Phase 1 yang belum selesai atau ditunda karena timebox,
  beserta alasan prioritasnya, berdasarkan keadaan aktual.

## Evidence akhir

Tambahkan commit yang diuji, hasil C#/API/hosting/frontend, hasil manual QA,
dan lokasi log/catatan. Ubah status finding berdasarkan bukti tersebut.
Tag phase-1-complete dibuat setelah Definition of Done Phase 1 terpenuhi.
