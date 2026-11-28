Notchyyyy for Windows: test installer (not a release)

1. Run Notchyyyy-win-Setup.exe.
   It's not signed yet, so Windows may say "Windows protected your PC": click "More info", then "Run anyway".
   It installs for you only (no administrator rights) and adds Notchyyyy to the Start menu.
   Start it from the Start menu. The first time, it opens a short setup in the notch.

2. Move the pointer to the top-centre of the screen to open the notch.

3. Smoothness: run measure-smoothness.cmd (about 15 seconds; leave the mouse alone) and send back the summary
   it prints (also saved at %TEMP%\notchyyyy-perf\summary.txt). The line "renderer:" should say
   "ANGLE on Direct3D 11 (graphics chip)".

This test build never looks for updates. To remove it: Settings > Apps > Installed apps > Notchyyyy > Uninstall.
Your settings stay in %APPDATA%\Notchyyyy (delete that folder too for a clean slate).
