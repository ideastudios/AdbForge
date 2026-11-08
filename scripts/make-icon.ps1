# AdbForge icon generator (GDI+, reproducible)
# Usage: powershell -ExecutionPolicy Bypass -File scripts/make-icon.ps1
# Output: src/AdbToolkit/assets/app.ico (multi-size) + assets/logo.png (256px)
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\src\AdbToolkit\assets")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

New-Item $OutDir -ItemType Directory -Force | Out-Null

function New-RoundedRectPath([float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc(0, 0, $d, $d, 180, 90)
    $p.AddArc($w - $d, 0, $d, $d, 270, 90)
    $p.AddArc($w - $d, $h - $d, $d, $d, 0, 90)
    $p.AddArc(0, $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# ---------- draw 256px master ----------
$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([System.Drawing.Color]::Transparent)

# background: rounded square + blue gradient (matches app theme)
$bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)),
    (New-Object System.Drawing.Point($size, $size)),
    [System.Drawing.Color]::FromArgb(255, 0x4A, 0xA3, 0xFF),
    [System.Drawing.Color]::FromArgb(255, 0x1F, 0x6F, 0xDB))
$rp = New-RoundedRectPath $size $size 56
$g.FillPath($bgBrush, $rp)

$white = [System.Drawing.Color]::White
$whiteBrush = New-Object System.Drawing.SolidBrush($white)

# android head (upper half ellipse, centered top)
$head = New-Object System.Drawing.Drawing2D.GraphicsPath
$head.AddArc(68, 66, 120, 120, 180, 180)
$head.CloseFigure()
$g.FillPath($whiteBrush, $head)

# antennae (round caps)
$pen = New-Object System.Drawing.Pen($white, 9)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($pen, 92, 86, 70, 52)
$g.DrawLine($pen, 164, 86, 186, 52)

# eyes (background blue)
$eyeBrush = New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(255, 0x2F, 0x8B, 0xE6))
$g.FillEllipse($eyeBrush, 96, 92, 18, 18)
$g.FillEllipse($eyeBrush, 142, 92, 18, 18)

# terminal prompt ">_" (the adb tool essence)
$font = New-Object System.Drawing.Font("Consolas", 56,
    [System.Drawing.FontStyle]::Bold)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.DrawString(">_", $font, $whiteBrush,
    (New-Object System.Drawing.RectangleF(0, 138, $size, 112)), $sf)

$g.Dispose()

# ---------- multi-size output ----------
function Resize-Image([System.Drawing.Bitmap]$src, [int]$s) {
    $b = New-Object System.Drawing.Bitmap($s, $s)
    $gg = [System.Drawing.Graphics]::FromImage($b)
    $gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gg.DrawImage($src,
        (New-Object System.Drawing.Rectangle(0, 0, $s, $s)),
        0, 0, $src.Width, $src.Height,
        [System.Drawing.GraphicsUnit]::Pixel)
    $gg.Dispose()
    return $b
}

$sizes = 256, 128, 64, 48, 32, 16
$pngs = @()
foreach ($s in $sizes) {
    $b = Resize-Image $bmp $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -eq 256) {
        $b.Save((Join-Path $OutDir "logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $b.Dispose()
}
$bmp.Dispose()

# ---------- assemble multi-size ICO (PNG entries, Vista+) ----------
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $edge = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$edge)
    $bw.Write([Byte]$edge)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()

$icoPath = Join-Path $OutDir "app.ico"
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$logoPath = Join-Path $OutDir "logo.png"

Write-Host "Icon generated:"
Write-Host "  $icoPath"
Write-Host "  $logoPath"
