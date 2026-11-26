# PRD My Driver! — Aplikasi Diagnosis Driver Windows

Nov 26, 2026 · @Rappiijiahan

## Ringkasan dan tujuan

My Driver! adalah aplikasi desktop Windows 10/11 yang memantau kesehatan driver, memberi tahu bila ada masalah, lalu menyarankan solusi yang bisa dijalankan dengan satu klik. Aplikasi ini portable dan bekerja di laptop atau PC merek apa pun, karena hanya memakai fitur bawaan Windows.

**Latar belakang.** Masalah driver (WiFi hilang, suara mati, layar berkedip, blue screen) biasanya baru ketahuan saat sudah mengganggu. Aplikasi resmi merek laptop hanya berlaku untuk mereknya sendiri, dan aplikasi "driver updater" komersial sering memasang driver yang salah.

**Tujuan:**

1. Mendeteksi masalah driver sedini mungkin, termasuk yang belum terasa (misalnya driver berubah diam-diam lewat Windows Update).
2. Menjelaskan masalah dalam Bahasa Indonesia yang mudah dipahami (sesuai KBBI).
3. Memberi rekomendasi solusi berurutan dari yang paling aman, dengan tombol aksi langsung.
4. Menyimpan backup dan snapshot driver "sehat" sebagai jalan kembali.
5. Tidak pernah merusak sistem: setiap perubahan didahului restore point dan bisa dibatalkan.

**Pengguna:** pemilik aplikasi sendiri (pemakaian pribadi), untuk laptop pribadi, komputer kantor, atau komputer lain yang perlu diperbaiki.

## Ruang lingkup

Target versi 1.0: Windows 10 (22H2) dan Windows 11, 64-bit (x64), semua merek laptop/PC.

| Aspek | Ketentuan |
| --- | --- |
| Sistem operasi | Windows 10 22H2 dan Windows 11, x64 |
| Merek perangkat | Semua merek; tidak memakai API khusus produsen |
| Distribusi | Satu file exe self-contained, tanpa instal .NET, bisa dijalankan dari flashdisk |
| Hak akses | Aksi perbaikan wajib Administrator (manifest `highestAvailable`); tanpa hak admin masuk mode baca saja dengan tombol "Jalankan sebagai Administrator" |
| Bahasa | Bahasa Indonesia baku (KBBI) |
| Internet | Tidak wajib; hanya dipakai untuk pencarian via Windows Update |

**Di luar cakupan versi 1.0:**

- Windows 7/8 dan Windows berprosesor ARM.
- Mengunduh driver dari situs pihak ketiga atau driver pack.
- Fitur khusus merek (kipas, mode baterai, lampu keyboard).
- Overclocking, tuning performa, atau "pembersih registry".
- Versi publik/multiuser; aplikasi untuk pemakaian sendiri.

## Teknologi dan arsitektur

Aplikasi dibangun dengan C# .NET 8 + WPF berpola MVVM, dan semua akses ke Windows dibungkus dalam lapisan layanan agar mudah diuji.

| Kebutuhan | Teknologi / sumber Windows |
| --- | --- |
| UI | WPF, MVVM (CommunityToolkit.Mvvm), tema terang/gelap |
| Daftar perangkat & kode error | WMI `Win32_PnPEntity` (`ConfigManagerErrorCode`), `Win32_PnPSignedDriver` |
| Info komputer | WMI `Win32_ComputerSystem`, `Win32_BIOS`, `Win32_OperatingSystem` |
| Backup driver | `dism /online /export-driver` |
| Pasang, hapus, daftar paket driver | `pnputil /add-driver`, `/delete-driver`, `/enum-drivers`, `/scan-devices` |
| Aktif/nonaktif & restart perangkat | `pnputil /disable-device`, `/enable-device`, `/restart-device` |
| Pencarian driver resmi | Windows Update Agent API (COM `Microsoft.Update.Session`, filter `Type='Driver'`) |
| Log kejadian | `System.Diagnostics.Eventing.Reader` (log System) |
| Analisis blue screen | Folder `C:\Windows\Minidump` + event BugCheck 1001 |
| Restore point | WMI `SystemRestore.CreateRestorePoint` |
| Pemantauan latar | Task Scheduler (scan saat logon + harian), ikon tray |
| Notifikasi | Windows toast (Microsoft.Toolkit.Uwp.Notifications) |
| Penyimpanan | File JSON di folder data portable |
| Build | `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` |

**Struktur proyek:**

```
MyDriver/
  MyDriver.App/          # WPF: Views, ViewModels, tray, notifikasi
  MyDriver.Core/         # Model, mesin diagnosis, aturan (rules.json)
  MyDriver.Windows/      # WmiService, PnpUtilService, DismService,
                         # WindowsUpdateService, EventLogService,
                         # MinidumpService, RestorePointService
  MyDriver.Tests/        # Unit test aturan diagnosis dengan data contoh
```

Setiap perintah eksternal (`pnputil`, `dism`) dijalankan lewat satu kelas `CommandRunner` yang mencatat perintah, output, dan kode keluar ke log.

## Fitur

Versi 1.0 memiliki sepuluh fitur; F1–F6 wajib, F7–F10 menyusul setelah inti stabil.

| Kode | Fitur | Rincian | Prioritas |
| --- | --- | --- | --- |
| F1 | Scan perangkat | Membaca semua perangkat PnP: nama, kelas, produsen, versi dan tanggal driver, penanda tangan, Hardware ID, kode error. Tampil per kategori (Jaringan, Suara, Layar, USB, Bluetooth, Penyimpanan, Lainnya). | Wajib |
| F2 | Snapshot sehat | Saat pertama dijalankan di komputer, menyimpan daftar driver yang sedang berfungsi sebagai acuan. Bisa diperbarui manual ("Tandai kondisi sekarang sebagai sehat"). | Wajib |
| F3 | Backup & restore driver | Backup semua driver pihak ketiga ke folder data per komputer; restore seluruhnya atau per perangkat. | Wajib |
| F4 | Mesin diagnosis | Mencocokkan temuan dengan aturan (lihat bagian Mesin diagnosis) dan menghasilkan kartu masalah: status, penjelasan, rekomendasi berurutan. | Wajib |
| F5 | Aksi perbaikan | Tombol: nonaktifkan lalu aktifkan, restart perangkat, scan ulang hardware, kembalikan ke versi snapshot, pasang dari backup, uninstall driver lalu scan ulang, cari di Windows Update. | Wajib |
| F6 | Restore point & riwayat | Restore point otomatis sebelum aksi yang mengubah sistem; riwayat semua aksi beserta hasilnya. | Wajib |
| F7 | Pemantauan latar | Scan otomatis saat logon dan sekali sehari lewat Task Scheduler; notifikasi toast bila status berubah ke Perhatian/Bermasalah. | Lanjutan |
| F8 | Deteksi perubahan driver | Membandingkan scan dengan snapshot: driver baru, versi berubah, driver hilang. | Lanjutan |
| F9 | Analisis crash | Membaca event BugCheck dan minidump; menampilkan driver `.sys` yang dicurigai dan perangkat pemiliknya. | Lanjutan |
| F10 | Pembersihan DriverStore | Menampilkan paket driver lama yang tidak dipakai beserta ukurannya; hapus terpilih dengan konfirmasi. | Lanjutan |

**Fitur pendukung:**

- Halaman info komputer (merek, model, BIOS, versi Windows) dengan tombol "Salin" untuk dicari di situs resmi produsen.
- Ekspor laporan scan ke file teks/HTML untuk disimpan atau dikirim ke teknisi.

## Mesin diagnosis

Setiap temuan dicocokkan dengan aturan di `rules.json`; aturan menghasilkan tingkat (Aman / Perhatian / Bermasalah), penjelasan, dan daftar rekomendasi yang diurutkan dari paling aman. Aturan disimpan sebagai data, bukan kode, agar mudah ditambah tanpa mengubah program.

**Status keseluruhan:** Bermasalah bila ada satu temuan merah; Perhatian bila ada temuan kuning; selain itu Aman.

**Aturan cerdas "perubahan + masalah":** bila perangkat bermasalah dan versi drivernya berbeda dari snapshot sehat, rekomendasi teratas selalu "Kembalikan ke versi snapshot" dengan catatan tanggal perubahan.

**Kode error perangkat (`ConfigManagerErrorCode`):**

| Kode | Arti sederhana | Tingkat | Urutan rekomendasi |
| --- | --- | --- | --- |
| 1 | Perangkat belum dikonfigurasi dengan benar | Merah | Pasang dari backup → Windows Update → uninstall lalu scan ulang |
| 3 | Driver rusak atau memori sistem kurang | Merah | Restart Windows → uninstall lalu scan ulang → pasang dari backup |
| 10 | Perangkat gagal dijalankan | Merah | Restart perangkat → kembalikan ke snapshot → Windows Update → uninstall lalu scan ulang |
| 12 | Bentrok sumber daya dengan perangkat lain | Merah | Restart Windows → nonaktifkan perangkat yang tidak dipakai → cek pengaturan BIOS |
| 14 | Perlu restart Windows | Kuning | Tombol restart Windows |
| 18 | Driver perlu dipasang ulang | Merah | Uninstall lalu scan ulang → pasang dari backup |
| 19 | Data konfigurasi di registry rusak | Merah | Uninstall lalu scan ulang → restore point sebelumnya |
| 22 | Perangkat dinonaktifkan | Kuning | Aktifkan perangkat |
| 24 | Perangkat tidak terdeteksi atau tidak lengkap | Kuning | Cek sambungan fisik → scan ulang hardware |
| 28 | Driver belum terpasang | Merah | Pasang dari backup → Windows Update → salin Hardware ID untuk situs produsen |
| 31 | Perangkat tidak bekerja dengan benar | Merah | Restart perangkat → kembalikan ke snapshot → Windows Update |
| 32 | Layanan driver dinonaktifkan | Merah | Aktifkan ulang layanan driver → uninstall lalu scan ulang |
| 37, 39 | Driver gagal dimuat, rusak, atau hilang | Merah | Kembalikan ke snapshot → pasang dari backup → Windows Update |
| 43 | Windows menghentikan perangkat karena melaporkan masalah | Merah | Restart perangkat → kembalikan ke snapshot → Windows Update → cek hardware |
| 45 | Perangkat sedang tidak tersambung | Info | Tidak perlu aksi (sembunyikan secara bawaan) |
| 48 | Driver diblokir karena bermasalah | Merah | Windows Update untuk versi baru → situs produsen |
| 52 | Tanda tangan digital driver tidak valid | Merah | Windows Update → pasang dari backup → situs produsen |
| lainnya | Kode tidak dikenal | Kuning | Tampilkan kode, salin Hardware ID, Windows Update |

**Temuan dari log dan kondisi driver:**

| Temuan | Sumber | Tingkat | Rekomendasi |
| --- | --- | --- | --- |
| Kartu grafis berhenti merespons lalu pulih (TDR) | Event Display 4101 | Kuning; Merah bila ≥ 3 kali dalam 7 hari | Kembalikan ke snapshot → Windows Update → situs AMD/NVIDIA/Intel |
| Driver gagal dimuat saat start | Event Kernel-PnP 219 | Kuning | Tampilkan perangkat terkait → uninstall lalu scan ulang |
| Blue screen | Event BugCheck 1001 + minidump | Merah | Tampilkan driver `.sys` tersangka → kembalikan ke snapshot → Windows Update |
| Error perangkat keras | Event WHEA-Logger | Merah | Sarankan cek hardware ke teknisi; driver kemungkinan bukan penyebab |
| Mati mendadak | Event Kernel-Power 41 | Kuning | Cek baterai/charger; lihat apakah ada blue screen di waktu yang sama |
| Driver berubah dibanding snapshot, perangkat normal | Perbandingan snapshot | Info | Tawarkan "Tandai sebagai sehat" bila sudah dipakai beberapa hari tanpa masalah |
| Driver tidak bertanda tangan | `Win32_PnPSignedDriver.IsSigned` | Kuning | Windows Update untuk versi resmi |
| Driver berusia > 5 tahun | Tanggal driver | Info | Cek Windows Update; tidak wajib diganti bila normal |
| Backup belum ada atau > 90 hari | Folder data | Kuning | Tombol "Backup sekarang" |

**Contoh format aturan (`rules.json`):**

```json
{
  "id": "cm-43",
  "match": { "type": "deviceError", "code": 43 },
  "level": "critical",
  "title": "{device} dihentikan Windows (Kode 43)",
  "explanation": "Windows menghentikan perangkat ini karena perangkat melaporkan masalah.",
  "actions": ["restartDevice", "rollbackToSnapshot", "windowsUpdate", "copyHardwareId"]
}
```

## Tampilan dan alur pengguna

Tampilan modern dan tenang, dengan menu samping berisi enam halaman; halaman utama langsung menjawab "apakah komputer saya aman?".

| Halaman | Isi |
| --- | --- |
| Beranda | Status besar (hijau/kuning/merah), jumlah temuan, waktu scan terakhir, tombol "Scan sekarang", info backup terakhir |
| Masalah | Daftar kartu masalah: judul, penjelasan, "sejak kapan", rekomendasi bernomor dengan tombol aksi |
| Perangkat | Pohon perangkat per kategori, ikon status, detail driver dan Hardware ID saat diklik |
| Backup | Backup sekarang, daftar backup per tanggal, restore semua/per perangkat, snapshot sehat |
| Riwayat | Semua scan dan aksi, hasil berhasil/gagal, restore point yang dibuat |
| Pengaturan | Scan otomatis aktif/nonaktif, jam scan harian, notifikasi, tema, sembunyikan perangkat tak tersambung |

**Alur pertama kali dijalankan:**

1. Aplikasi meminta hak Administrator.
2. Mengenali komputer (merek, model, ID unik) dan membuat folder data untuknya.
3. Scan pertama, lalu bertanya: "Apakah semua perangkat saat ini berfungsi normal?" Jika ya, simpan snapshot sehat.
4. Menawarkan backup driver sekarang (disarankan).
5. Menawarkan aktifkan pemantauan otomatis.

**Alur saat ada masalah:**

1. Notifikasi toast: "My Driver!: WiFi bermasalah. Klik untuk melihat solusi."
2. Halaman Masalah terbuka pada kartu terkait.
3. Pengguna menekan tombol rekomendasi pertama; aplikasi membuat restore point, menjalankan aksi, lalu scan ulang perangkat itu.
4. Jika masalah hilang, kartu ditandai selesai; jika belum, aplikasi menyorot rekomendasi berikutnya.

**Gaya teks:** kalimat pendek, tanpa istilah teknis di judul; istilah teknis (kode error, nama `.sys`, Hardware ID) ditaruh di bagian "Detail teknis" yang bisa dibuka.

## Penyimpanan data portable

Semua data disimpan di folder `MyDriverData` di samping file exe, dipisah per komputer, sehingga satu flashdisk bisa melayani banyak komputer.

```
MyDriver.exe
MyDriverData/
  settings.json
  rules.json                     # bisa ditimpa versi terbaru
  computers/
    ASUS_X441UA_8F3A2C1D/        # merek_model_8-karakter-ID
      computer.json              # merek, model, BIOS, Windows
      snapshot-healthy.json      # acuan driver sehat
      scans/2026-11-26_1500.json
      history.json               # aksi dan hasilnya
      backups/2026-11-26/        # hasil export-driver
  logs/2026-11-26.log
```

**Aturan:**

- ID komputer = hash dari UUID motherboard (`Win32_ComputerSystemProduct.UUID`) + nomor seri BIOS, agar tetap sama walau Windows diinstal ulang.
- Bila folder di samping exe tidak bisa ditulisi (misalnya di Program Files), pakai `%LOCALAPPDATA%\MyDriver` dan beri tahu pengguna.
- Backup lama disimpan maksimal 5 versi per komputer; yang lebih lama ditawarkan untuk dihapus.
- Aplikasi tidak pernah memakai backup milik komputer lain untuk restore, kecuali pengguna memilihnya secara sadar dengan peringatan.

## Keamanan dan pengaman sistem

Prinsip utamanya: aplikasi boleh salah diagnosis, tetapi tidak boleh membuat komputer tidak bisa menyala.

- **Restore point wajib** sebelum setiap aksi yang mengubah driver. Bila restore point gagal dibuat (System Protection mati), aplikasi memperingatkan dan meminta konfirmasi ganda.
- **Daftar terlindung:** driver penyimpanan, chipset/ACPI, prosesor, dan perangkat boot (`Class` = DiskDrive, SCSIAdapter, HDC, System, Processor, Computer) tidak bisa di-uninstall atau dinonaktifkan dari aplikasi; hanya bisa "kembalikan ke snapshot" dengan peringatan merah.
- **Paket yang sedang dipakai** tidak boleh dihapus dari DriverStore; gunakan `pnputil /delete-driver` tanpa `/force`.
- **Tanpa unduhan pihak ketiga:** sumber driver hanya backup lokal dan Windows Update.
- **Konfirmasi jelas** sebelum aksi berisiko, menyebut nama perangkat dan akibatnya (contoh: "WiFi akan terputus sebentar").
- **Sekali jalan:** hanya satu aksi perbaikan dalam satu waktu; tombol lain dikunci sampai selesai.
- **Log lengkap** setiap perintah, output, dan kode keluar di folder `logs`.
- **Mode baca saja:** bila tidak dijalankan sebagai Administrator, scan tetap jalan, semua tombol aksi dinonaktifkan dengan keterangan.

## Tahapan pengerjaan dengan Claude Code

Pengerjaan dibagi tujuh tahap; setiap tahap diakhiri dengan build yang bisa dijalankan dan diuji di laptop sebelum lanjut.

1. **Kerangka proyek.** Solusi `MyDriver` dengan 4 proyek, MVVM, menu samping, tema, `CommandRunner`, logger, manifest admin, publish single-file.
2. **Scan dan halaman Perangkat (F1).** `WmiService`, model `DeviceInfo`, pohon perangkat per kategori, detail driver, info komputer.
3. **Folder data, snapshot, backup (F2, F3).** ID komputer, struktur `MyDriverData`, snapshot sehat, backup via DISM, restore via pnputil, halaman Backup.
4. **Mesin diagnosis (F4).** `rules.json`, pencocok aturan, kartu masalah, status Beranda, unit test dengan data perangkat contoh untuk setiap kode error.
5. **Aksi perbaikan dan pengaman (F5, F6).** Restore point, daftar terlindung, setiap tombol aksi, scan ulang setelah aksi, halaman Riwayat.
6. **Windows Update, log, crash (F9 + pencarian F5).** `WindowsUpdateService`, `EventLogService`, `MinidumpService`, aturan event.
7. **Pemantauan dan penyempurnaan (F7, F8, F10).** Task Scheduler, ikon tray, toast, perbandingan snapshot, pembersihan DriverStore, ekspor laporan.

**Contoh perintah untuk Claude Code di awal setiap tahap:**

```
Baca PRD.md. Kerjakan Tahap 2 saja. Jangan ubah fitur tahap lain.
Setelah selesai, jalankan build dan unit test, lalu jelaskan
file yang dibuat/diubah dan cara saya mengujinya di laptop.
```

Simpan dokumen ini sebagai `PRD.md` di akar repositori (ekspor ke Markdown) agar Claude Code selalu bisa membacanya.

## Kriteria selesai dan pengujian

Versi 1.0 dianggap selesai bila semua butir di bawah lulus di minimal dua laptop berbeda merek.

- [ ] Exe tunggal berjalan dari flashdisk di Windows 10 22H2 dan Windows 11 tanpa instal apa pun.
- [ ] Scan lengkap selesai dalam waktu kurang dari 15 detik pada laptop biasa.
- [ ] Pada komputer baru, folder data, snapshot sehat, dan backup terbentuk otomatis atas persetujuan pengguna.
- [ ] Menonaktifkan sebuah perangkat lewat Device Manager Windows memunculkan temuan Kode 22 dan tombol "Aktifkan" berhasil memulihkannya.
- [ ] Uninstall driver perangkat uji (misalnya webcam atau Bluetooth) memunculkan Kode 28, dan "Pasang dari backup" memulihkannya.
- [ ] Setiap aksi pengubah sistem tercatat di Riwayat beserta restore point-nya.
- [ ] Perangkat di daftar terlindung tidak bisa di-uninstall atau dinonaktifkan dari aplikasi.
- [ ] Unit test lulus untuk semua aturan di `rules.json`.
- [ ] Notifikasi toast muncul saat scan terjadwal menemukan masalah baru.
- [ ] Tanpa hak Administrator, aplikasi berjalan dalam mode baca saja tanpa error.

**Cara uji aman:** gunakan perangkat yang tidak penting (webcam, Bluetooth, pembaca kartu) untuk menguji aksi, dan buat restore point manual sebelum sesi pengujian.
