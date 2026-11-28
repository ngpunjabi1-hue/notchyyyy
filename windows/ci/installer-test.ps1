# Installs, updates and uninstalls Notchyyyy on this machine the way a person would, and checks each step.
# Run by CI with Windows PowerShell 5.1 (what every Windows PC has). Nothing is published: releases are a local folder.
#   installer-test.ps1 -Releases <dir with the 0.1.0 release> -Next <published 0.1.1 folder> -Vpk <vpk.exe> -Out <dir>
param([string]$Releases, [string]$Next, [string]$Vpk, [string]$Out, [string]$Icon)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$results = Join-Path $Out 'installer-test.txt'
Set-Content $results ''
$failed = $false
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $line = ($(if ($ok) { 'PASS ' } else { 'FAIL ' })) + $name + $(if ($detail) { "  ($detail)" } else { '' })
    Add-Content $results $line
    Write-Host $line
    if (-not $ok) { $script:failed = $true }
}
function Info([string]$line) { Add-Content $results "INFO $line"; Write-Host "INFO $line" }

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
function Shot([string]$name) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $bmp.Save((Join-Path $Out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
function WaitFor([scriptblock]$cond, [double]$seconds) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 250 }
    return [bool](& $cond)
}
function Version([string]$exe) { if (Test-Path $exe) { (Get-Item $exe).VersionInfo.ProductVersion } else { '' } }

$root = Join-Path $env:LOCALAPPDATA 'Notchyyyy'
$current = Join-Path $root 'current\Notchyyyy.exe'
$data = Join-Path $env:APPDATA 'Notchyyyy'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Notchyyyy.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Notchyyyy'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

# your data from before: a name, setup finished, and a pinned app
New-Item -ItemType Directory -Force -Path $data | Out-Null
Set-Content (Join-Path $data 'settings.json') '{ "UserName": "Robin", "Onboarded": true, "AutoUpdate": true }' -Encoding UTF8
Set-Content (Join-Path $data 'tasks.json') '[]' -Encoding UTF8

# ---- 1. install 0.1.0 with the installer, quietly ----
$setup = Join-Path $Releases 'Notchyyyy-win-Setup.exe'
Check 'the release has an installer' (Test-Path $setup) $setup
$p = Start-Process -FilePath $setup -ArgumentList '--silent', '--log', (Join-Path $Out 'setup-0.1.0.log') -Wait -PassThru
Check 'the installer finishes without asking anything' ($p.ExitCode -eq 0) "exit code $($p.ExitCode)"
Check 'installed for this user, no administrator rights' (Test-Path $current) $current
Check 'version 0.1.0 is installed' ((Version $current) -like '0.1.0*') (Version $current)
Check 'a Start-menu shortcut' (Test-Path $shortcut) $shortcut
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Notchyyyy.lnk'
Check 'no desktop shortcut' (-not (Test-Path $desktop))
$entry = Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue
Check 'listed in Settings > Apps, with its name' ($entry -and $entry.DisplayName -eq 'Notchyyyy') "$($entry.DisplayName) $($entry.DisplayVersion)"
# the quiet installer doesn't start the app (the one-line install below does): start it from the Start menu,
# as a person would
Start-Process -FilePath $shortcut
Check 'it starts from the Start-menu shortcut' (WaitFor { Get-Process Notchyyyy -ErrorAction SilentlyContinue } 15)
Start-Sleep -Seconds 3
Shot 'installed-0.1.0.png'
Get-Process Notchyyyy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# ---- 2. a new version appears; the installed app finds it, downloads it, installs it when idle, restarts ----
& $Vpk pack -u Notchyyyy -v 0.1.1 -p $Next -e Notchyyyy.exe --packTitle Notchyyyy --packAuthors Notchyyyy -i $Icon `
    --shortcuts StartMenuRoot --aumid Notchyyyy.Notchyyyy -o $Releases --skip-updates 2>&1 | Out-File (Join-Path $Out 'pack-0.1.1.log')
Check '0.1.1 is packed beside 0.1.0 (with a delta)' ((Get-ChildItem $Releases -Filter '*0.1.1*delta*').Count -ge 1) ((Get-ChildItem $Releases).Name -join ', ')
# the pointer away from the notch: an update installs only while nobody is using it
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(120, 700)
$log = Join-Path $Out 'update.log'
Start-Process -FilePath $current -ArgumentList '--update-feed', $Releases, '--update-now', '--update-log', $log
$updated = WaitFor { (Test-Path $log) -and ((Get-Content $log -Raw) -match 'running 0\.1\.1 after an update from 0\.1\.0') } 120
Check 'it finds 0.1.1, installs it while the notch is idle, and restarts as 0.1.1' $updated
if (Test-Path $log) { Get-Content $log | ForEach-Object { Info "update log: $_" } }
Start-Sleep -Milliseconds 1800
Shot 'updated-bubble.png'
Check 'version 0.1.1 is now installed' ((Version $current) -like '0.1.1*') (Version $current)
$settings = Get-Content (Join-Path $data 'settings.json') -Raw
Check 'your settings are kept across the update' ($settings -match '"UserName":\s*"Robin"') $settings.Substring(0, [Math]::Min(120, $settings.Length))
Check 'and it remembers which version ran' ($settings -match '"LastRunVersion":\s*"0\.1\.1"')
Get-Process Notchyyyy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# ---- 3. uninstall: the app goes, your data stays ----
if (-not (Test-Path $runKey)) { New-Item -Path $runKey -Force | Out-Null }   # not on every machine (the app creates it too)
New-ItemProperty -Path $runKey -Name Notchyyyy -Value "`"$current`"" -PropertyType String -Force | Out-Null   # as if Launch at login were on
$u = Start-Process -FilePath (Join-Path $root 'Update.exe') -ArgumentList 'uninstall', '--silent', '--log', (Join-Path $Out 'uninstall.log') -Wait -PassThru
Check 'uninstall finishes quietly' ($u.ExitCode -eq 0) "exit code $($u.ExitCode)"
Check 'the app is removed' (WaitFor { -not (Test-Path $current) } 20)
Check 'the Start-menu shortcut is removed' (-not (Test-Path $shortcut))
Check 'it is no longer listed in Settings > Apps' (-not (Test-Path $uninstallKey))
Check 'it is taken off the startup list' (-not (Get-ItemProperty $runKey -Name Notchyyyy -ErrorAction SilentlyContinue))
Check 'its notification identity is removed' (-not (Test-Path 'HKCU:\Software\Classes\AppUserModelId\Notchyyyy.Notchyyyy'))
Check 'your data is left for you' (Test-Path (Join-Path $data 'settings.json'))

# ---- 4. the one-line install, exactly as people will run it (served from this machine) ----
$serve = Join-Path $Out 'serve'
New-Item -ItemType Directory -Force -Path $serve | Out-Null
Copy-Item (Join-Path $PSScriptRoot '..\installer\install.ps1') $serve
Copy-Item (Join-Path $Releases '*') $serve -Recurse
$server = Start-Process -FilePath python -ArgumentList '-m', 'http.server', '8765', '--bind', '127.0.0.1' -WorkingDirectory $serve -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
$env:NOTCHYYYY_RELEASES = 'http://127.0.0.1:8765'
$installOut = (irm http://127.0.0.1:8765/install.ps1 | iex) *>&1 | Out-String
Info "install.ps1 said: $($installOut.Trim() -replace '\s+', ' ')"
Check 'irm ... | iex installs Notchyyyy' (Test-Path $current) (Version $current)
Check 'and starts it' (WaitFor { Get-Process Notchyyyy -ErrorAction SilentlyContinue } 15)
Start-Sleep -Seconds 3
Shot 'one-line-install.png'
Get-Process Notchyyyy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Process -FilePath (Join-Path $root 'Update.exe') -ArgumentList 'uninstall', '--silent' -Wait | Out-Null
Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue

Add-Content $results $(if ($failed) { 'SOME FAILED' } else { 'ALL PASS' })
if ($failed) { exit 1 } else { exit 0 }
