# Notchyyyy for Windows: build plan

Stack: C# / .NET 8 + Avalonia 12.1.6 (Skia). The Mac app in `../NotchShelf` is the spec and is never edited.
Windows-only services sit behind interfaces with Mac or stand-in versions, so every screen runs and is checked on the Mac.

Each step ends with: build, run, screenshots in dark and light, automated checks where possible, and your OK before the next step.

## Release 1

| # | Step | How it is verified |
|---|------|--------------------|
| 1 ✅ | **Scaffold + closed pill + hover open/close motion.** Transparent always-on-top overlay at top centre, the notch shape, the paired open/close springs, the content fade, hover tracking (open after the hover delay, close 0.35 s after leaving, no bounce-back). No shadow. | Spring maths unit-tested against the SwiftUI formulas; frame-by-frame renders at 60 fps measured from the pixels; a real-time recording on this Mac split into frames; screenshots in both themes. |
| 2 ✅ | **GitHub Actions on Windows.** Build, tests, and a runner that launches the app, hovers it open and takes screenshots. Click-through and always-on-top checked on real Windows. | Green run, screenshots from the runner. |
| 3 ✅ | **Glass tab bar + close button**, tab switching with a cross-fade, Esc and Ctrl+1…9. | Screenshots, UI self-test clicks every tab. |
| 4 ✅ (awaiting your OK; smoothness to confirm on your laptop) | **Sprout** (the painter, poses and morphing, gaze, states) in the closed pill and on Home; **Home** (one-line greeting, icon quick actions). | Frame strips of every mood and each transition. |
| 5 ✅ (awaiting your OK) | **Settings** (Appearance, Behaviour, Extras, Tabs, About), prefs saved to `%APPDATA%`, page anchored at the top and kept across theme rebuilds. | Every page in both themes; settings survive a restart. |
| 6 ✅ (awaiting your OK) | **Media**: SMTC on Windows, a demo source on the Mac; music bars in the closed pill. | `--demo` on the Mac; real SMTC on the runner and your laptop. |
| 7 (in review) | **Clipboard** (text, images, colours with HEX/RGB/HSL, SVG, kept on disk). | Sample data screenshots; persistence test. |
| 8 (in review) | **Apps** (Start-menu apps, favourites, picker, edit mode). | Screenshots; Start-menu scan on the runner. |
| 9 (in review) | **Today** (tasks, times, reminders) and **Stopwatch** (laps, keys, time in the closed pill). | UI self-test; screenshots. |
| 10 | **System** (CPU, GPU, RAM, disk via performance counters). | Runner reading real counters; stand-in values on the Mac. |
| 11 | **Onboarding** (five steps), **hydration reminders + peek bubble**, idle time and typing pace (counts only, never which keys). | Onboarding self-test; `--peek`, `--fake-typing`, `--fake-away`. |
| 12 | **Installer + auto-updates**: Velopack releases, one-line PowerShell installer (`irm … | iex`), data kept across updates. | Install, update and uninstall on the runner; final check on your laptop. Nothing is published until you say so, and I'll ask which repo first. |

## Release 2
Voice notes (on-device Whisper, English and Hinglish), the Agent tab (Claude, Codex, Gemini CLIs), the games Hop and Drop (original designs), screenshot and screen recording.

## Debug flags (same idea as the Mac app)
`--preview` (a framed stand-in for a Windows desktop, for this Mac), `--open <tab>`, `--light`, `--sample`, `--anim-test`, `--frames <dir>`; more added per step.

## Changes from the Mac app (deliberate)
- **No shadow** under the open notch (your call: notch apps don't need one). Tests fail on any pixel outside the shape.
- **Light theme fill** has its own quick ease (0.24 s open, 0.20 s close) instead of riding the width spring, so the
  closed pill is black again ~0.2 s after closing instead of lingering grey.
- **Clean content hand-off**: the closed pill's art and bars leave in 0.10 s on open and come back only after the body
  has left (0.12 s) on close, so the two layouts never overlap (the Mac cross-fades them for ~0.1 s).
- **Launch warm-up**: the open body is drawn invisibly for 3 frames at launch so the first open doesn't drop frames.
- **Grayscale text smoothing** on Windows (as on the Mac): ClearType's red/blue fringes showed on the runner.
- **Icons**: Notchyyyy's own vector set (SF Symbols can't ship on Windows), drawn without pushed transforms
  (Avalonia's off-screen renderer mis-composed them at 2x, dropping icons).
- **Glass without blur**: the tab bar is opaque with a hairline edge and a top highlight. A transparent overlay
  window can't blur the desktop behind part of itself, and a 90 % tint let text behind it show through (seen on Windows).
- **Keyboard returns** to the app you were in when the notch closes (Windows needs this explicitly).
- **Sprout without per-frame blurs**: the glow is an exactly computed blurred-disc gradient; shadow, highlight and blush
  are blurred once. Matches the live-blur original within 7/255 per pixel (tested).
- **Sprout is animated by the render thread** (a composition visual): steady 60 fps on the Mac (worst frame 18.7 ms);
  UI-thread redraws were uneven (16–25 ms). The little Sprout runs at 60 fps like the Mac's (30 was uneven on Windows).
- **Home's third row is Clipboard** (the Mac's Agent row returns with the Agent tab in release 2).
- **Screenshot / Record** press Windows' own Snipping Tool shortcuts (Win+Shift+S / Win+Shift+R) after the notch closes.
- **Tab switches cross-fade without blur** (the Mac's blurFade blurs too). At 125 % scaling and up, Avalonia's
  BlurEffect softens text by the same amount at any radius from 0.05 to 0.5 pt (measured), so a view could never
  sharpen smoothly: it snapped from soft to crisp in one frame. `--blur-swap` brings the blur back for comparison;
  a test proves the pop check catches that snap.
- **Media on Windows** reads the system's media sessions (SMTC: Spotify, Media Player, browsers…). Shuffle and repeat
  show only when the playing app supports them; with nothing playing the button opens Media Player. No app icon in
  the "Playing from" chip (the Mac shows one).
- **Clipboard on Windows** watches the clipboard's sequence number (twice a second, like the Mac's pasteboard check)
  and never keeps copies marked private (password managers; the same marks Windows' own clipboard history honours).
  Not yet: dragging a card out into another app (the Mac allows it) — a follow-up.
- **Apps on Windows** come from the Start menu's Apps folder (desktop programs and Store apps alike), open through
  shell:AppsFolder, and show the shell's own icons. Default pins are Windows' counterparts of the Mac's (File
  Explorer, Edge, Media Player, Notepad, Terminal, Settings — whichever exist). Pins are kept in
  %APPDATA%\Notchyyyy\apps.json. Not yet: dropping a shortcut onto the shelf, and the right-click menu — follow-ups.
- **Today on Windows**: reminders are Windows notifications. Notchyyyy registers its own notification identity in the
  user's registry (as unpackaged apps do), so they work before the installer exists. Times in a task's title are
  found by Notchyyyy's own reader ("5pm", "5:30 pm", "17:00", "at 5", "noon"; the Mac uses its date detector); the
  time field is a compact HH:mm field (click the hours or minutes, then arrows, the wheel or digits).
- **Raw Skia drawing applies the current opacity itself** (Sprout, the record, the colour field): without it the big
  Sprout stayed fully opaque while Home cross-faded away (an old bug from step 4, found frame by frame in step 6).

## Where the code lives
`ngpunjabi1-hue/notchyyyy` (public), folder `windows/`, branch `windows-port` (`main` untouched).
CI: `.github/workflows/windows.yml` runs on every push to `windows-port` that touches `windows/`.

## Display scaling
Verified on the Windows runner at 100 % (1024×768) and, switched live by `ci/display.ps1`, at 125 % and 150 % (1920×1080):
the full self-test passes at each (window size and centring, real-cursor hover and hot zone, clicks, keys, focus return).

## Smoothness on Windows
`--perf DIR` runs a scripted session and times every frame (src/Notchyyyy/PerfProbe.cs). The CI runner has no graphics
chip (it renders with Direct3D's software rasteriser, WARP), so its numbers are a floor, not the real experience.
Each CI run uploads **Notchyyyy-windows-test-build** (self-contained) with `measure-smoothness.cmd` for a real laptop.
