<p align="center">
  <img src="assets/logo.svg" width="112" alt="Monday Notch logo">
</p>

<h1 align="center">Monday Notch</h1>

<p align="center">
  A small, living notch at the top of your Windows screen.<br>
  Music with synced lyrics, quick controls, a focus timer, to-dos and notes — one hover away.
</p>

<p align="center">
  <a href="https://github.com/ryanmhildan/monday-notch/releases/latest"><img src="https://img.shields.io/github/v/release/ryanmhildan/monday-notch?label=download&color=6D5DF6" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/.NET-8-512BD4" alt=".NET 8">
</p>

<p align="center">
  <img src="docs/screenshots/control-center.png" width="560" alt="Monday Notch open on the Control Center">
</p>

## Download

1. Go to **[Releases](https://github.com/ryanmhildan/monday-notch/releases/latest)** and download `MondayNotch.exe`.
2. Run it. There is nothing to install — the .NET runtime is built in.

The app isn't code-signed yet, so Windows SmartScreen may say *"Windows protected your PC"*. Click **More info → Run anyway**.

Hover the notch to open it, move away to close it. Right-click the tray icon (next to the clock) for **Settings** and **Exit**.

## Features

### Closed notch

<img src="docs/screenshots/closed.png" width="320" alt="Closed notch">

Three slots — left, center and right — each showing a widget you pick: **Battery, To-Do, Clock, Date, Weather, Ping, FPS** or nothing. While music plays, the album art takes the left slot; while a focus timer runs, its countdown takes the right one. With lyrics turned on, the closed notch shows just the album art and the line being sung.

### Six pages

Switch with the arrows on either side or click the dots underneath.

| | |
|---|---|
| **Control Center** — opens first when nothing is playing. Wi-Fi, Bluetooth, Hotspot, Power mode, Lock, Sleep, Restart, Shut down, Snip, and brightness and volume sliders. Right-click a tile to open its Windows settings. | <img src="docs/screenshots/control-center.png" width="380" alt="Control Center"> |
| **Music** — opens first while something plays. Works with any player that shows up in Windows' media controls (Spotify, YouTube in the browser, Apple Music, …). Play/pause, skip, seek, and **synced lyrics** from [LRCLIB](https://lrclib.net); with lyrics on, the notch takes on the colours of the album art. | |
| **Focus** — a Pomodoro timer: 25 minutes of focus, 5 minutes of break, a long break every four sessions. The notch pops open with a chime when time is up. | <img src="docs/screenshots/focus.png" width="380" alt="Focus timer"> |
| **To-do** — type and press Enter, tick to finish, hover for ×. Finished tasks sink to the bottom. | <img src="docs/screenshots/todo.png" width="380" alt="To-do list"> |
| **Quick note** — one scratch note that saves as you type. | <img src="docs/screenshots/quick-note.png" width="380" alt="Quick note"> |
| **Settings** — everything below. | <img src="docs/screenshots/settings-idle.png" width="380" alt="Idle settings with live preview"> |

### Startup greeting

<img src="docs/screenshots/greeting.png" width="340" alt="Startup greeting">

When Monday Notch starts, the logo pops in, blinks, and says hello — with your Windows name or one you choose.

### Light theme and floating shape

<img src="docs/screenshots/light-floating.png" width="420" alt="Light theme, floating notch">

## Settings

| Tab | Options |
|---|---|
| **General** | Start with Windows · Startup greeting (name: Auto or Custom) · Auto-compact (shrinks to a thin line while a browser is the active window) |
| **Appearance** | Notch shape: Anchored (attached to the top edge) or Floating, with distance from the top · Theme: Dark, Light or Auto (follows Windows) |
| **Idle** | Live preview of the closed notch · widget for the left, center and right slot, with swap buttons |
| **Advanced** | Check for updates · Clean temporary files · Restart notch · Reset all settings |

## Privacy

Monday Notch has no account and no analytics. Everything stays on your PC except these, and only while you use them:

| Feature | What leaves your PC | Where |
|---|---|---|
| Lyrics (when shown) | Song title, artist, album and length | [LRCLIB](https://lrclib.net) |
| Weather widget | Your location, rounded to about 1 km (Windows asks for permission first) | [Open-Meteo](https://open-meteo.com) |
| Ping widget | A ping every 5 seconds | 1.1.1.1 |
| Check for updates | A request for the latest release | GitHub |

Your data lives in:

- Settings: `HKCU\Software\MondayNotch`
- To-dos: `%AppData%\MondayNotch\todos.json`
- Note: `%AppData%\MondayNotch\note.txt`

**Good to know**

- *FPS* is how many frames Windows draws per second for the whole screen — how smoothly the screen is updating, not the frame rate of a particular game.
- *Clean temporary files* deletes files in `%TEMP%` that are more than a day old. Files in use are skipped, and it asks you to click twice.
- Brightness control works on built-in laptop screens. Most external monitors don't support it.

## Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) (or newer) on Windows 10 1809+ or Windows 11.

```bash
dotnet run
```

To build the same single `.exe` as the releases:

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

`MondayNotch.exe --snapshot out.png --no-media` renders every screen to `out-*.png` and exits. That's how the screenshots above were made.

## Releasing a new version

Push a version tag. GitHub Actions ([release.yml](.github/workflows/release.yml)) builds `MondayNotch.exe` and publishes it on the Releases page. The tag also becomes the version shown in the app.

```bash
git tag v0.2.0
```

```bash
git push origin v0.2.0
```

## Project layout

| Path | What's in it |
|---|---|
| `MainWindow.xaml(.cs)` | The notch: shape and animation, greeting, idle slots, Music/Focus/To-do pages, page navigation |
| `Pages/` | Control Center, Quick note and Settings pages |
| `App.xaml` | Shared styles and theme brushes |
| `IdleWidgetView.cs`, `BatteryIcon.cs` | Idle widgets and the battery drawing |
| `Services/` | Media, lyrics, album-art colours, system controls, volume, theme, settings, Pomodoro, to-dos, note, tray icon, maintenance |
| `assets/` | Logo (`logo.svg`, PNG sizes) and app icon (`icon.ico`) |
| `docs/screenshots/` | Images used in this README |
