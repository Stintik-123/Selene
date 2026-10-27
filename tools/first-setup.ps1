# Preview the public packages in a dedicated portable Playnite, with fresh Selene settings/profiles.
# The normal Playnite and the existing selene-fresh test copy are only read, never changed.
param(
    [string]$Root = (Join-Path $env:USERPROFILE 'selene-first-setup'),
    [string]$Source = (Join-Path $env:USERPROFILE 'selene-fresh/Playnite'),
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$previewRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
$sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
$pn = Join-Path $previewRoot 'Playnite'
if ($previewRoot -eq [IO.Path]::GetPathRoot($previewRoot).TrimEnd('\') -or $previewRoot -eq $sourceRoot -or $sourceRoot.StartsWith($previewRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or $previewRoot.StartsWith($sourceRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose a separate preview folder, outside the source Playnite.'
}
if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'Playnite.FullscreenApp.exe'))) { throw "Portable Playnite not found in $sourceRoot" }
foreach ($package in 'Selene.pthm', 'SeleneCore.pext') { if (-not (Test-Path -LiteralPath (Join-Path $repo "dist/$package"))) { throw "Build dist/$package first with tools/package.ps1." } }
if (Get-Process Playnite.FullscreenApp, Playnite.DesktopApp -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$pn\*" }) { throw 'Close the first-setup preview before resetting it.' }
New-Item -ItemType Directory -Force -Path $pn | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $pn 'Playnite.FullscreenApp.exe'))) {
    # Copy application files; do not inherit libraries, extensions, development paths or user settings.
    Get-ChildItem -LiteralPath $sourceRoot -File | Where-Object { $_.Name -notin @('config.json','fullscreenConfig.json','desktopConfig.json','safestart.flag') -and $_.Extension -ne '.log' } | Copy-Item -Destination $pn
    foreach ($folder in 'locales','Localization','Resources','Templates','Emulation') {
        $from = Join-Path $sourceRoot $folder
        if (Test-Path -LiteralPath $from) { Copy-Item -LiteralPath $from -Destination $pn -Recurse -Force }
    }
}
$baseThemes = Join-Path $pn 'Themes'
foreach ($mode in 'Desktop','Fullscreen') {
    $baseSource = Join-Path $sourceRoot "Themes/$mode/Default"
    $baseTarget = Join-Path $baseThemes $mode
    if (-not (Test-Path -LiteralPath (Join-Path $baseTarget 'Default'))) {
        New-Item -ItemType Directory -Force -Path $baseTarget | Out-Null
        Copy-Item -LiteralPath $baseSource -Destination $baseTarget -Recurse -Force
    }
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$data = [IO.Path]::GetFullPath((Join-Path $pn 'ExtensionsData/cd424c29-97f4-4dd0-aa66-4d8e01cc8724'))
if (-not $data.StartsWith([IO.Path]::GetFullPath($pn) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected preview data path.' }
if (Test-Path -LiteralPath $data) {
    $backup = [IO.Path]::GetFullPath((Join-Path $previewRoot "saved-runs/$stamp"))
    if (-not $backup.StartsWith($previewRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected preview backup path.' }
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Move-Item -LiteralPath $data -Destination (Join-Path $backup 'SeleneCore')
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Install-PublicPackage([string]$Archive, [string]$Target) {
    $targetRoot = [IO.Path]::GetFullPath($Target).TrimEnd('\')
    if (-not $targetRoot.StartsWith([IO.Path]::GetFullPath($pn) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Package target left the preview folder.' }
    New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            if (-not $entry.Name) { continue }
            $file = [IO.Path]::GetFullPath((Join-Path $targetRoot $entry.FullName))
            if (-not $file.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package path.' }
            New-Item -ItemType Directory -Force -Path (Split-Path $file -Parent) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $file, $true)
        }
    } finally { $zip.Dispose() }
}
Install-PublicPackage (Join-Path $repo 'dist/Selene.pthm') (Join-Path $pn 'Themes/Fullscreen/Selene_a98e0027-fa6a-4f5e-a087-bad8d69f2443')
Install-PublicPackage (Join-Path $repo 'dist/SeleneCore.pext') (Join-Path $pn 'Extensions/SeleneCore_cd424c29-97f4-4dd0-aa66-4d8e01cc8724')
# A normal portable Playnite with no developer extensions. Selene itself performs the first-time setup.
if (-not (Test-Path -LiteralPath (Join-Path $pn 'config.json'))) {
    @{ FirstTimeWizardComplete = $true; DatabasePath = '{PlayniteDir}/library'; DevelExtenions = @() } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $pn 'config.json') -Encoding UTF8
}
@{ Theme = 'Selene_a98e0027-fa6a-4f5e-a087-bad8d69f2443' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $pn 'fullscreenConfig.json') -Encoding UTF8
New-Item -ItemType Directory -Force -Path (Join-Path $pn 'library') | Out-Null
$previewTemp = Join-Path $previewRoot 'temp'
New-Item -ItemType Directory -Force -Path $previewTemp | Out-Null
$env:TEMP = $previewTemp; $env:TMP = $previewTemp
Write-Output "First-setup preview prepared in $pn"
Write-Output "Log: $(Join-Path $previewTemp 'selene.log')"
if (-not $PrepareOnly) { Start-Process -FilePath (Join-Path $pn 'Playnite.FullscreenApp.exe') -ArgumentList '--masterinstance' -WorkingDirectory $pn }
