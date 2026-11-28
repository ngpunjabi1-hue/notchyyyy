# Notchyyyy for Windows: build plan

Stack: C# / .NET 8 + Avalonia 12.1.6 (Skia). The Mac app in `../NotchShelf` is the spec and is never edited.
Windows-only services sit behind interfaces with Mac or stand-in versions, so every screen runs and is checked on the Mac.

Each step ends with: build, run, screenshots in dark and light, automated checks where possible, and your OK before the next step.

## Release 1

| # | Step | How it is verified |
|---|------|--------------------|
| 1 ✅ | **Scaffold + closed pill + hover open/close motion.** Transparent always-on-top overlay at top centre, the notch shape, the paired open/close springs, the content fade, hover tracking (open after the hover delay, close 0.35 s after leaving, no bounce-back). No shadow. | Spring maths unit-tested against the SwiftUI formulas; frame-by-frame renders at 60 fps measured from the pixels; a real-time recording on this Mac split into frames; screenshots in both themes. |
| 2 ✅ | **GitHub Actions on Windows.** Build, tests, and a runner that launches the app, hovers it open and takes screenshots. Click-through and always-on-top checked on real Windows. | Green run, screenshots from the runner. |
| 3 ✅ (awaiting your OK) | **Glass tab bar + close button**, tab switching with the blur cross-fade, Esc and Ctrl+1…9. | Screenshots, UI self-test clicks every tab. |
| 4 | **Sprout** (the painter, poses and morphing, gaze, states) in the closed pill and on Home; **Home** (one-line greeting, icon quick actions). | Frame strips of every mood and each transition. |
| 5 | **Settings** (Appearance, Behaviour, Extras, Tabs, About), prefs saved to `%APPDATA%`, page anchored at the top and kept across theme rebuilds. | Every page in both themes; settings survive a restart. |
| 6 | **Media**: SMTC on Windows, a demo source on the Mac; music bars in the closed pill. | `--demo` on the Mac; real SMTC on the runner and your laptop. |
| 7 | **Clipboard** (text, images, colours with HEX/RGB/HSL, SVG, kept on disk). | Sample data screenshots; persistence test. |
| 8 | **Apps** (Start-menu apps, favourites, picker, edit mode). | Screenshots; Start-menu scan on the runner. |
| 9 | **Today** (tasks, times, reminders) and **Stopwatch** (laps, keys, time in the closed pill). | UI self-test; screenshots. |
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

## Where the code lives
`ngpunjabi1-hue/notchyyyy` (public), folder `windows/`, branch `windows-port` (`main` untouched).
CI: `.github/workflows/windows.yml` runs on every push to `windows-port` that touches `windows/`.

## Verified only at 100 % display scaling
The Windows runner's screen is 1024×768 at 100 %. Laptops usually run at 125–150 %; the cursor and window
maths divide by the scaling, but that path is untested until a run at higher scaling or your laptop.
- **Icons**: Notchyyyy's own vector set (SF Symbols can't ship on Windows), drawn without pushed transforms
  (Avalonia's off-screen renderer mis-composed them at 2x, dropping icons).
- **Glass without blur**: the tab bar is a 90 % tint with a hairline edge and a top highlight; a transparent overlay
  window can't blur the desktop behind part of itself.
- **Keyboard returns** to the app you were in when the notch closes (Windows needs this explicitly).
