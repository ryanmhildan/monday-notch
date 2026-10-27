# Monday Notch

Notch kecil di tengah atas layar Windows. Arahkan kursor ke notch untuk membukanya; jauhkan kursor dan notch menutup lagi. Teks aplikasi berbahasa Inggris.

## Download

Ambil `MondayNotch.exe` dari halaman **Releases** repo ini, lalu jalankan. Tidak perlu instal apa pun (runtime .NET sudah termasuk di dalam file).

Karena aplikasi belum ditandatangani (code signing), Windows SmartScreen mungkin menampilkan "Windows protected your PC". Klik **More info** → **Run anyway**.

## Fitur

- **Startup greeting**: saat aplikasi dibuka, logo (pil dengan dua mata) muncul dan berkedip, lalu sapaan seperti "Good evening, Ryan". Nama diambil dari akun Windows (Auto) atau diisi sendiri (Custom).
- **Notch tertutup (Idle)**: tiga slot kiri/tengah/kanan yang bisa diisi Battery, To-Do, Clock, Date, Weather, Ping, FPS, atau Empty, dan bisa ditukar posisinya. Saat musik diputar, sampul album mengambil slot kiri; saat Pomodoro berjalan, sisa waktunya mengambil slot kanan.
- **Enam halaman** saat notch terbuka, dipindah dengan panah kiri/kanan atau titik di bawah:
  1. **Control Center**: muncul pertama kalau tidak ada musik. Ada Wi-Fi, Bluetooth, Hotspot, Power mode (Efficiency/Balanced/Performance), Lock, Sleep, Restart, Shut down, Snip (screenshot), serta bar Brightness dan Volume. Klik kanan sebuah tile untuk membuka halaman Settings Windows yang sesuai. Restart dan Shut down harus diklik dua kali.
  2. **Music**: muncul pertama kalau musik sedang diputar. Mendukung pemutar apa pun, lengkap dengan lirik tersinkron dari [LRCLIB](https://lrclib.net) (tombol **Show lyrics** / **Hide lyrics**). Selama lirik tampil, warna notch menjadi gradient dari sampul album.
  3. **Focus**: Pomodoro 25/5 menit, istirahat panjang 15 menit setiap 4 sesi.
  4. **To-do**: tambah dengan Enter, centang untuk selesai, × untuk hapus.
  5. **Quick note**: satu catatan yang tersimpan otomatis.
  6. **Settings**:
     - **General**: Start with Windows, Startup greeting (Auto/Custom name), Auto-compact (notch mengecil jadi garis tipis saat browser aktif).
     - **Appearance**: Anchored (menempel ke tepi layar, dengan lengkungan) atau Floating (melayang, dengan Distance from top 0–40 px); tema Dark, Light, atau Auto (mengikuti Windows).
     - **Idle**: live preview notch tertutup dan pilihan widget per slot.
     - **Advanced**: Check for updates, Clean temporary files, Restart notch, Reset all settings.
- **Ikon tray**: Show notch, Show lyrics, Start with Windows, Settings…, Exit.

## Data dan privasi

- Pengaturan: `HKCU\Software\MondayNotch`. To-do: `%AppData%\MondayNotch\todos.json`. Catatan: `%AppData%\MondayNotch\note.txt`.
- **Lirik**: judul, artis, album, dan durasi lagu dikirim ke LRCLIB, hanya saat lirik aktif.
- **Weather** (hanya kalau widget Weather dipakai): meminta izin lokasi Windows, lalu koordinat yang dibulatkan dikirim ke [Open-Meteo](https://open-meteo.com).
- **Ping** (hanya kalau widget Ping dipakai): ping ke 1.1.1.1 setiap 5 detik.
- **FPS** adalah jumlah frame yang digambar Windows compositor per detik, jadi menunjukkan seberapa cepat layar diperbarui, bukan FPS game tertentu.
- **Check for updates** belum terhubung ke mana pun: konstanta `UpdateRepository` di `Services/Maintenance.cs` masih kosong. Isi dengan repo GitHub (`owner/name`) setelah aplikasi dirilis di sana.
- **Clean temporary files** menghapus file di `%TEMP%` yang lebih tua dari sehari; file yang sedang dipakai dilewati.

## Menjalankan

Butuh .NET 8 SDK (atau yang lebih baru) di Windows 10 1809+ / Windows 11.

```bash
dotnet run --project MondayNotch
```

Build untuk dibagikan (satu file .exe, tanpa perlu menginstal .NET):

```bash
dotnet publish MondayNotch -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Membuat rilis baru

Rilis dibuat otomatis oleh GitHub Actions (`.github/workflows/release.yml`). Setiap kali tag versi di-push, GitHub membangun `MondayNotch.exe` dan memasangnya di halaman Releases:

```bash
git tag v0.2.0
```

```bash
git push origin v0.2.0
```

Nomor versi di tag juga menjadi versi aplikasi (terlihat di Settings → Advanced).

## Struktur

| File | Isi |
|---|---|
| `MainWindow.xaml(.cs)` | Jendela notch: bentuk dan animasi, greeting, slot idle, halaman Music/Focus/To-do, navigasi halaman |
| `Pages/ControlCenterPage` | Halaman Control Center |
| `Pages/NotePage` | Halaman Quick note |
| `Pages/SettingsPage` | Halaman Settings (4 tab) |
| `App.xaml` | Style bersama dan brush tema |
| `IdleWidgetView.cs` | Menggambar widget idle (dipakai notch dan live preview) |
| `Services/ThemeManager.cs` | Tema Dark/Light/Auto |
| `Services/AppSettings.cs` | Semua pengaturan |
| `Services/SystemControls.cs` | Wi-Fi, Bluetooth, hotspot, power mode, brightness, lock/sleep/restart/shutdown, snip |
| `Services/AudioVolume.cs` | Volume lewat Core Audio |
| `Services/IdleInfo.cs` | Data cuaca, ping, dan FPS |
| `Services/Maintenance.cs` | Update check, clean temp, nama untuk greeting |
| `Services/MediaService.cs`, `LyricsService.cs`, `ArtworkPalette.cs` | Musik, lirik, warna dari sampul album |
| `Services/PomodoroTimer.cs`, `TodoStore.cs`, `NoteStore.cs` | Pomodoro, to-do, catatan |
| `Services/TrayIcon.cs`, `StartupManager.cs` | Ikon tray, mulai bersama Windows |

Untuk pengembangan, `MondayNotch.exe --snapshot hasil.png` menyimpan gambar setiap tampilan (`hasil-greeting.png`, `hasil-controlcenter.png`, dan seterusnya) lalu keluar.
