# Builds Selene Core with the C# compiler that ships with Windows (.NET Framework 4.x), no Visual Studio needed.
# Output: SeleneCore\bin\ (SeleneCore.dll + extension.yaml + icon + localization) - Playnite loads it from there in dev
# (Settings > For developers > custom extensions), or `Toolbox.exe pack` turns it into an installable .pext.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$core = Join-Path $root 'SeleneCore'
$out = Join-Path $core 'bin'
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$sdk = Join-Path $env:LOCALAPPDATA 'Playnite\Playnite.SDK.dll'

New-Item -ItemType Directory -Force $out | Out-Null
$sources = @(Get-ChildItem $core -Filter *.cs) + @(Get-ChildItem (Join-Path $core 'src') -Filter *.cs) | ForEach-Object { $_.FullName }
$refs = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll',
          'Microsoft.CSharp.dll', "$fw\WPF\WindowsBase.dll", "$fw\WPF\PresentationCore.dll", "$fw\WPF\PresentationFramework.dll", $sdk) |
        ForEach-Object { "/r:$_" }
& "$fw\csc.exe" /nologo /target:library /optimize+ /nowarn:1701,1702 "/out:$out\SeleneCore.dll" $refs $sources
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Copy-Item (Join-Path $core 'extension.yaml'), (Join-Path $core 'icon.png') $out -Force
Copy-Item (Join-Path $core 'Localization') $out -Recurse -Force
"built $out\SeleneCore.dll"
