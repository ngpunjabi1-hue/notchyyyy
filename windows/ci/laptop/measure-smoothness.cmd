@echo off
rem Measures how smoothly the installed Notchyyyy runs on this PC (about 15 seconds; please leave the mouse alone).
rem It opens and closes the notch, shows Home, switches tabs, and times every frame. Your settings aren't changed.
set APP=%LOCALAPPDATA%\Notchyyyy\current\Notchyyyy.exe
if not exist "%APP%" (
  echo Notchyyyy isn't installed yet: run Notchyyyy-win-Setup.exe first.
  pause
  exit /b 1
)
taskkill /im Notchyyyy.exe /f >nul 2>&1
set OUT=%TEMP%\notchyyyy-perf
start "" /wait "%APP%" --dark --name Alex --perf "%OUT%"
echo.
type "%OUT%\summary.txt"
echo.
echo The summary is also saved at %OUT%\summary.txt - please send it back.
start "" "%LOCALAPPDATA%\Notchyyyy\Notchyyyy.exe"
pause
