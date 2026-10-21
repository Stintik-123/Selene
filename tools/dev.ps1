# Selene dev loop: restart the real Playnite on the Selene being built, or back on the theme you were using (~8 s).
#   dev.ps1 new    -> Selene (theme\ in this repo, linked into Playnite's themes folder); remembers the current theme
#   dev.ps1 back   -> the theme that was active before `dev.ps1 new`
#   -quiet         -> start Playnite in the tray (no fullscreen pop-up over the desktop while working)
# Playnite allows only one running copy per Windows session (fixed mutex), so there is no side-by-side test instance.
# A Selene Core build waiting in SeleneCore\obj\stage (tools\build.ps1 while Playnite runs) is installed on the way.
param([ValidateSet('new', 'back')][string]$which = 'new', [switch]$quiet)

$selene = 'Selene_a98e0027-fa6a-4f5e-a087-bad8d69f2443'
$root = Split-Path $PSScriptRoot -Parent
$previousFile = Join-Path $root '.dev-previous-theme'
$app = Join-Path $env:LOCALAPPDATA 'Playnite\Playnite.FullscreenApp.exe'
$config = Join-Path $env:APPDATA 'Playnite\fullscreenConfig.json'

$text = [IO.File]::ReadAllText($config)
$current = [regex]::Match($text, '"Theme"\s*:\s*"([^"]*)"').Groups[1].Value
if ($which -eq 'new') {
    if ($current -ne $selene) { Set-Content $previousFile $current }
    $target = $selene
} else {
    if (-not (Test-Path $previousFile)) { throw "no previous theme recorded (run dev.ps1 new first)" }
    $target = (Get-Content $previousFile).Trim()
}

$running = Get-Process Playnite.FullscreenApp -ErrorAction SilentlyContinue
if ($running) {
    Start-Process $app '--shutdown'
    $running | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue
    Get-Process Playnite.FullscreenApp -ErrorAction SilentlyContinue | Stop-Process -Force
}

$stage = Join-Path $root 'SeleneCore\obj\stage'
if (Test-Path "$stage\SeleneCore.dll") { Copy-Item "$stage\*" (Join-Path $root 'SeleneCore\bin') -Recurse -Force }

$text = [IO.File]::ReadAllText($config)
$text = [regex]::Replace($text, '"Theme"\s*:\s*"[^"]*"', '"Theme": "' + $target + '"')
[IO.File]::WriteAllText($config, $text)

if ($quiet) { Start-Process $app '--hidesplashscreen', '--startclosedtotray' } else { Start-Process $app '--hidesplashscreen' }
"Playnite restarting on " + $target
