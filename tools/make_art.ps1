# Draws Selene's own artwork (crescent moon logo, default cover, add-on icon) so no third-party images ship.
# Re-run after changing the design: tools\make_art.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$img = Join-Path $root 'theme\Images'

function New-Moon([int]$size, [bool]$background) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    if ($background) {
        $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
        $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 16, 42, 92)), ([System.Drawing.Color]::FromArgb(255, 3, 8, 26)), 60
        $g.FillEllipse($bg, 0, 0, $size - 1, $size - 1)
    }
    # crescent: a full disc with a second disc cut out of it
    $d = $size * 0.62; $x = ($size - $d) / 2 - $size * 0.04; $y = ($size - $d) / 2
    $moon = New-Object System.Drawing.Drawing2D.GraphicsPath
    $moon.AddEllipse([single]$x, [single]$y, [single]$d, [single]$d)
    $cut = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cut.AddEllipse([single]($x + $d * 0.32), [single]($y - $d * 0.12), [single]$d, [single]$d)
    $region = New-Object System.Drawing.Region $moon
    $region.Exclude($cut)
    $glow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 234, 243, 255))
    $g.FillRegion($glow, $region)
    $g.Dispose()
    return $bmp
}

function Save-Png($bmp, $path) { $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); "wrote $path" }

Save-Png (New-Moon 256 $false) (Join-Path $img 'applogo_clean.png')
Save-Png (New-Moon 256 $true) (Join-Path $img 'applogo.png')
Save-Png (New-Moon 512 $true) (Join-Path $img 'applogo_full.png')
Save-Png (New-Moon 256 $true) (Join-Path $root 'SeleneCore\icon.png')

# default cover (games without one): navy gradient with a faint moon, 600x900
$cover = New-Object System.Drawing.Bitmap 600, 900
$g = [System.Drawing.Graphics]::FromImage($cover)
$g.SmoothingMode = 'AntiAlias'
$rect = New-Object System.Drawing.Rectangle 0, 0, 600, 900
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 16, 42, 92)), ([System.Drawing.Color]::FromArgb(255, 3, 8, 26)), 90
$g.FillRectangle($bg, $rect)
$moon = New-Moon 300 $false
$cm = New-Object System.Drawing.Imaging.ColorMatrix
$cm.Matrix33 = 0.35
$ia = New-Object System.Drawing.Imaging.ImageAttributes
$ia.SetColorMatrix($cm)
$g.DrawImage($moon, (New-Object System.Drawing.Rectangle 150, 300, 300, 300), 0, 0, 300, 300, 'Pixel', $ia)
$g.Dispose()
Save-Png $cover (Join-Path $img 'custom_cover_background.png')

# window icon
$ico = New-Moon 64 $true
$icon = [System.Drawing.Icon]::FromHandle($ico.GetHicon())
$fs = [IO.File]::Create((Join-Path $img 'applogo.ico')); $icon.Save($fs); $fs.Close()
"wrote applogo.ico"
