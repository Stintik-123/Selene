# Selene dev loop: restart the real Playnite on the theme you want to look at (~8 s).
#   dev.ps1 new      -> the Selene being built (theme\ in this repo, linked into Playnite's themes folder)
#   dev.ps1 legacy   -> Selene Legacy (the Aniki-based setup in daily use)
# Playnite allows only one running copy per Windows session (fixed mutex), so there is no side-by-side test instance.
param([ValidateSet('new', 'legacy')][string]$which = 'new')

$ids = @{
    new    = 'Selene_a98e0027-fa6a-4f5e-a087-bad8d69f2443'
    legacy = 'Selene_9c048650-2da7-4ffa-ba01-b154cc195b86'
}
$app = Join-Path $env:LOCALAPPDATA 'Playnite\Playnite.FullscreenApp.exe'
$config = Join-Path $env:APPDATA 'Playnite\fullscreenConfig.json'

$running = Get-Process Playnite.FullscreenApp -ErrorAction SilentlyContinue
if ($running) {
    Start-Process $app '--shutdown'
    $running | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue
    Get-Process Playnite.FullscreenApp -ErrorAction SilentlyContinue | Stop-Process -Force
}

$text = [IO.File]::ReadAllText($config)
$text = [regex]::Replace($text, '"Theme"\s*:\s*"[^"]*"', '"Theme": "' + $ids[$which] + '"')
[IO.File]::WriteAllText($config, $text)

Start-Process $app '--hidesplashscreen'
"Playnite restarting on " + $(if ($which -eq 'new') { 'Selene (new)' } else { 'Selene Legacy' })
