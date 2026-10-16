# Integrity Declaration

Project: Access Request Hub — Phase 1

Terakhir diperbarui: 16 Oktober 2026; cakupan sampai langkah 11 dan dokumentasi.

Status: DRAFT — detail bantuan/sumber dan konfirmasi review perlu dilengkapi oleh pengembang.

Dokumen ini merupakan draf deklarasi sesuai bagian 4.2 assessment. Isi kolom
di bawah berdasarkan kejadian sebenarnya. Konfirmasikan deklarasi setelah
meninjau source dan mampu menjelaskan implementasi yang akan diserahkan.

## Ownership Declaration

Teks deklarasi berikut untuk dikonfirmasi oleh pengembang saat finalisasi:

> I confirm that this submission represents my own engineering work.
> AI-assisted tools were used as documented in AI_USAGE.md.
> I reviewed the submitted code and can explain its architecture, behavior,
> known limitations, security implications, and trade-offs.

Date: [Isi tanggal konfirmasi setelah review aktual]

Primary AI tools used: ChatGPT, termasuk bantuan coding agent untuk planning,
code generation, debugging, pemeriksaan, dan dokumentasi.

External human assistance: [Isi None jika tidak ada; jika ada, jelaskan pihak,
jenis bantuan, dan batas kontribusinya. Bantuan AI dicatat terpisah di AI_USAGE.md.]

Starter/template code used: ASP.NET Core Empty template dari `dotnet new web`
dan solution scaffold dari `dotnet new sln`. Bagian backend, migration/seed,
frontend, script pemeriksaan, dan dokumentasi disiapkan dengan bantuan
ChatGPT sebagaimana dicatat dalam AI_USAGE.md.

Pemeriksaan langkah 11 menambahkan console test project C# dan runner
PowerShell. Porsi bantuan AI mencakup source test, failure-injection scenarios,
serta penyusunan README.md, REVIEW.md, dan pembaruan dokumen provenance.

External repositories/snippets copied or adapted: [Isi None jika setelah
pemeriksaan tidak ada; jika ada, cantumkan URL/sumber, bagian yang diadaptasi,
dan lisensi/atribusi yang relevan.]

## Verification Status

| Area | Status berdasarkan bukti yang tersedia |
| --- | --- |
| Project test dalam solution | Sudah terdaftar menurut output CLI pengembang |
| JavaScript simulasi pada environment pembuat paket | 10 lulus, 0 gagal; DOM/HTTP disimulasikan |
| SQL/schema pada environment pembuat paket | Enam pemeriksaan tambahan lulus; menjalankan SQL/schema |
| Build/C# 23 kasus/API 33 checks/hosting 7 checks | Hasil akhir pengembang belum dilaporkan |
| Frontend checks dan walkthrough pada komputer pengembang | Hasil aktual belum dilaporkan; isi hasil atau alasan skip yang relevan |

Target jumlah test dan hasil pada environment pembuat paket dicatat terpisah
dari hasil pengembang. Setiap deklarasi review pribadi mengikuti pemeriksaan
yang benar-benar dilakukan pada source yang akan diserahkan.

## Review Before Finalization

Gunakan daftar ini untuk mencatat review yang benar-benar telah dilakukan:

- [ ] Saya meninjau source yang akan diserahkan dan dapat menjelaskan arsitekturnya.
- [ ] Saya memahami workflow standard/high-risk, role demo, dan authorization backend.
- [ ] Saya memahami idempotency ClientRequestId, concurrency Version, serta audit atomic/append-only.
- [ ] Saya menjalankan build serta pemeriksaan C#/API/hosting; hasil aktual terdokumentasi.
- [ ] Saya mencatat hasil frontend checks atau alasan skip yang digunakan.
- [ ] Saya menjalankan critical demo flow melalui browser dan meninjau hasilnya.
- [ ] Saya memahami finding, risiko, dan pekerjaan tertunda yang dicatat dalam REVIEW.md.
- [ ] AI_USAGE.md menjelaskan bantuan AI dan keputusan saya terhadap saran yang diberikan.
- [ ] REVIEW.md mencatat temuan, known limitations, dan deferred work.
- [ ] Saya melengkapi semua kolom sumber/bantuan dan mengonfirmasi deklarasi di atas.

Setelah review selesai, isi seluruh placeholder, sesuaikan checklist dengan
bukti aktual, dan ubah status DRAFT menjadi Final jika deklarasinya benar.
Hasil pengujian yang belum dijalankan tetap dicatat sebagai belum diverifikasi.
    