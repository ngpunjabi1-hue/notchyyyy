# Notchyyyy

Your MacBook's notch, made useful. Hover the notch and it opens into a small, calm home for the things you reach for all day.

- **Home**: a greeting, a sleepy little sprout that wakes up when you come near, and quick Screenshot / Record buttons
- **Media**: whatever's playing (Music, Spotify, YouTube in any browser) on a spinning record, with a waveform scrubber
- **Today**: plan the day; type "Call mom at 5pm" and get a reminder at 5
- **Apps**: your favourite apps, one click away
- **Clipboard**: text, images, colour codes (copy as HEX / RGB / HSL) and SVG icons (copy as SVG / PNG)
- **Agent**: run Claude, ChatGPT (Codex) or Gemini with your own account, and read their latest replies
- **Notes**: voice notes that type themselves, in English or Hinglish, even from across the room
- **System**: CPU, GPU, memory and storage at a glance

## Install

Paste this into Terminal:

```bash
curl -fsSL https://github.com/ngpunjabi1-hue/notchyyyy/releases/latest/download/install.sh | bash
```

Or download the `.dmg` from the [latest release](https://github.com/ngpunjabi1-hue/notchyyyy/releases/latest), drag Notchyyyy to Applications, and open it.
If macOS says it can't be opened, go to **System Settings › Privacy & Security** and click **Open Anyway** (once per Mac).

Notchyyyy updates itself: it checks once a day and shows "Update x.y" in the notch when a new version is ready.

**Requirements:** macOS 14 Sonoma or later, Apple silicon or Intel. On Macs without a notch it appears as a slim pill under the menu bar.

**Privacy:** everything stays on your Mac. Notes, tasks and clipboard history are stored locally; the only network requests are the daily update check and the AI agent you choose.
