# Dev: picture of Playnite's fullscreen window (even when other windows cover it), to look at Selene while building it.
# Usage: screenshot.ps1 <out.png> [width]
param([string]$out = "$env:TEMP\selene-shot.png", [int]$width = 1600)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  public struct RECT { public int L, T, R, B; }
}
"@
[Win]::SetProcessDPIAware() | Out-Null
$p = Get-Process Playnite.FullscreenApp -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
$h = $p.MainWindowHandle
$r = New-Object Win+RECT
[Win]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $ht = $r.B - $r.T
$full = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($full)
$dc = $g.GetHdc()
[Win]::PrintWindow($h, $dc, 2) | Out-Null   # PW_RENDERFULLCONTENT: works for covered windows
$g.ReleaseHdc($dc)
$sh = [int]($ht * $width / $w)
(New-Object System.Drawing.Bitmap $full, $width, $sh).Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
"$out (${w}x$ht)"
