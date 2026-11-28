# Installs or updates Notchyyyy for Windows, for the current user (no administrator rights).
#   irm https://github.com/__REPO__/releases/latest/download/install.ps1 | iex
# It downloads the installer from the latest release and runs it quietly. Notchyyyy then keeps itself up to date.
# Your settings, clipboard history, tasks and pins (in %APPDATA%\Notchyyyy) are kept.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest's progress bar slows the download a lot

# where releases come from (NOTCHYYYY_RELEASES points elsewhere for testing)
$base = if ($env:NOTCHYYYY_RELEASES) { $env:NOTCHYYYY_RELEASES.TrimEnd('/') } else { 'https://github.com/__REPO__/releases/latest/download' }

# Windows 10 (October 2018 update, build 17763) or later
$build = [Environment]::OSVersion.Version.Build
if ([Environment]::OSVersion.Version.Major -lt 10 -or $build -lt 17763) {
    Write-Host "Notchyyyy needs Windows 10 (version 1809) or later; this PC has build $build." -ForegroundColor Red
    return
}
if (-not [Environment]::Is64BitOperatingSystem) {
    Write-Host 'Notchyyyy needs 64-bit Windows.' -ForegroundColor Red
    return
}

$setup = Join-Path ([IO.Path]::GetTempPath()) "Notchyyyy-Setup-$([guid]::NewGuid().ToString('N').Substring(0, 8)).exe"
try {
    Write-Host 'Downloading Notchyyyy...'
    Invoke-WebRequest -Uri "$base/Notchyyyy-win-Setup.exe" -OutFile $setup -UseBasicParsing
    # a running copy is closed first (the installer starts the new one)
    Get-Process -Name Notchyyyy -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Host 'Installing...'
    $p = Start-Process -FilePath $setup -ArgumentList '--silent' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "the installer stopped with code $($p.ExitCode)" }
}
catch {
    Write-Host "Notchyyyy couldn't be installed: $($_.Exception.Message)" -ForegroundColor Red
    return
}
finally {
    Remove-Item $setup -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host 'Notchyyyy is installed and running.' -ForegroundColor Green
Write-Host '  Move your pointer to the top-centre of the screen to open it.'
Write-Host '  It updates itself from now on. To remove it: Settings > Apps > Installed apps > Notchyyyy.'
