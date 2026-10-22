# Builds Selene Core and packs both parts into installable files with Playnite's Toolbox:
#   dist\Selene_<id>_<version>.pthm   (theme)       dist\SeleneCore_<id>_<version>.pext   (add-on)
#   plus copies named Selene.pthm and SeleneCore.pext for the GitHub release
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$toolbox = Join-Path $env:LOCALAPPDATA 'Playnite\Toolbox.exe'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Get-ChildItem $dist -Include *.pthm, *.pext -Recurse | Remove-Item

& (Join-Path $PSScriptRoot 'build.ps1')
$stage = Join-Path $root 'SeleneCore\obj\stage'   # always the fresh build (bin\ may be held open by a running Playnite)

& $toolbox pack (Join-Path $root 'theme') $dist
& $toolbox pack $stage $dist
# fixed names for GitHub releases: the installer manifests (manifests\*.yaml) link to Selene.pthm and SeleneCore.pext
Get-ChildItem $dist -Filter 'Selene_*.pthm' | Copy-Item -Destination (Join-Path $dist 'Selene.pthm')
Get-ChildItem $dist -Filter 'SeleneCore_*.pext' | Copy-Item -Destination (Join-Path $dist 'SeleneCore.pext')
Get-ChildItem $dist | ForEach-Object { "{0}  ({1:N0} KB)" -f $_.Name, ($_.Length / 1KB) }
