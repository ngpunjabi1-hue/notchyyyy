@echo off
rem Measures how smoothly Notchyyyy runs on this PC (about 15 seconds; please leave the mouse alone).
rem It opens and closes the notch, shows Home, switches tabs, and times every frame.
set OUT=%TEMP%\notchyyyy-perf
start "" /wait "%~dp0Notchyyyy.exe" --dark --name Alex --perf "%OUT%"
echo.
type "%OUT%\summary.txt"
echo.
echo The summary is also saved at %OUT%\summary.txt - please send it back.
pause
