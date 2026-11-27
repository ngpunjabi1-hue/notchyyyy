# Notchyyyy for Windows

The Windows version of the Notchyyyy Mac app (`../NotchShelf`, the reference; never edited from here).
C# / .NET 8 + Avalonia 12.1.6. See [PLAN.md](PLAN.md) for the steps.

## Build, run, test (on the Mac or Windows)

```bash
dotnet build Notchyyyy.sln
dotnet test Notchyyyy.sln
dotnet run --project src/Notchyyyy -- --demo
```

On macOS the app shows itself in a framed stand-in for a Windows desktop (`--preview`, the default there),
because the top of a MacBook screen is under the hardware notch. On Windows it is the real overlay:
a transparent, always-on-top, click-through window at the top centre of the primary screen.

With Homebrew's .NET 8: `export DOTNET_ROOT=/opt/homebrew/opt/dotnet@8/libexec PATH=/opt/homebrew/opt/dotnet@8/bin:$PATH`.

## Debug flags (never change saved data)

| Flag | What it does |
|---|---|
| `--preview` / `--overlay` | force the framed preview or the real overlay |
| `--demo` | a stand-in song is playing (music live activity in the closed pill) |
| `--open` | open and pin the notch |
| `--light` / `--dark` | theme for this run only |
| `--backdrop grey` | flat grey preview backdrop, for measuring |
| `--anim-test` | open at 2.5 s, close at 4.5 s, open at 6.5 s, close at 8.5 s |
| `--frame-log FILE` | one CSV line per animation frame: time, width, height, update ms |
| `--hover-test` | drives a stand-in pointer through the hover rules; PASS/FAIL to `$TMPDIR/notchyyyy-hovertest.txt` |
| `--snap FILE@SECONDS,…` | render the window to PNG at those times |
| `--pointer-debug` | write the stage's screen position and the pointer to `$TMPDIR/notchyyyy-pointer.txt` |
| `--quit-after SECONDS` | quit |

## Tests

- `SpringTests`: the springs against an independent RK4 integration of SwiftUI's spring (Apple's mapping
  stiffness (2π/duration)², damping 4π(1−bounce)/duration); delays; interruption keeps position and speed.
- `HoverTrackerTests`: the Mac app's hover rules on a stepped clock.
- `FrameTests`: every frame rendered at exact times (60 and 120 fps, dark and light) on a magenta backdrop;
  the shape's edges are found to sub-pixel precision and compared with the springs; any pixel outside the
  shape fails (content leaking, or a shadow); open-body pixels after closing fail (ghosting); the header and
  the pill's content may never both show (overlap). Frames and CSVs go to `$NOTCHYYYY_FRAMES`.
- `WarmUpTests`: the launch warm-up changes no pixel by more than one level.
