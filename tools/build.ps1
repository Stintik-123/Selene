# Builds Selene Core with the C# compiler that ships with Windows (.NET Framework 4.x), no Visual Studio needed.
# Output: SeleneCore\bin\ (SeleneCore.dll + extension.yaml + icon + localization) - Playnite loads it from there in dev
# (Settings > For developers > custom extensions), or `Toolbox.exe pack` turns it into an installable .pext.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$core = Join-Path $root 'SeleneCore'
$out = Join-Path $core 'bin'
$stage = Join-Path $core 'obj\stage'  # Playnite keeps bin\SeleneCore.dll open while running
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$sdk = Join-Path $env:LOCALAPPDATA 'Playnite\Playnite.SDK.dll'

New-Item -ItemType Directory -Force $out, $stage | Out-Null
$sources = @(Get-ChildItem $core -Filter *.cs) + @(Get-ChildItem (Join-Path $core 'src') -Filter *.cs) | ForEach-Object { $_.FullName }
$refs = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll',
          'Microsoft.CSharp.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', "$fw\WPF\WindowsBase.dll", "$fw\WPF\PresentationCore.dll", "$fw\WPF\PresentationFramework.dll", $sdk) |
        ForEach-Object { "/r:$_" }
& "$fw\csc.exe" /nologo /target:library /optimize+ /nowarn:1701,1702 "/out:$stage\SeleneCore.dll" $refs $sources
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Copy-Item (Join-Path $core 'extension.yaml'), (Join-Path $core 'icon.png') $stage -Force
Copy-Item (Join-Path $core 'Localization') $stage -Recurse -Force
try { Copy-Item "$stage\*" $out -Recurse -Force -ErrorAction Stop; "built and installed $out\SeleneCore.dll" }
catch { "built $stage\SeleneCore.dll (Playnite is running: dev.ps1 installs it on the next swap)" }
