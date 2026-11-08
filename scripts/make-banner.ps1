# AdbForge README banner generator
param(
    [string]$OutFile = (Join-Path $PSScriptRoot "..\docs\images\banner.png"),
    [string]$LogoPath = (Join-Path $PSScriptRoot "..\src\AdbToolkit\assets\logo.png")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
New-Item (Split-Path $OutFile) -ItemType Directory -Force | Out-Null

$w = 1200; $h = 240
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

# gradient background
$bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)),
    (New-Object System.Drawing.Point($w, $h)),
    [System.Drawing.Color]::FromArgb(255, 0x4A, 0xA3, 0xFF),
    [System.Drawing.Color]::FromArgb(255, 0x1F, 0x6F, 0xDB))
$g.FillRectangle($bgBrush, 0, 0, $w, $h)

# logo (scaled)
if (Test-Path $LogoPath) {
    $logo = [System.Drawing.Image]::FromFile($LogoPath)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($logo, 28, 30, 180, 180)
    $logo.Dispose()
}

$white = [System.Drawing.Color]::White
$whiteBrush = New-Object System.Drawing.SolidBrush($white)

# title
$fontTitle = New-Object System.Drawing.Font("Microsoft YaHei UI", 52,
    [System.Drawing.FontStyle]::Bold)
$g.DrawString("卓匠 AdbForge", $fontTitle, $whiteBrush, 240, 46)

# subtitle
$subBrush = New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(235, 255, 255, 255))
$fontSub = New-Object System.Drawing.Font("Microsoft YaHei UI", 24)
$g.DrawString("面向 Android / AOSP 开发者的 Windows 工具箱 · 纯 adb 实现",
    $fontSub, $subBrush, 246, 148)

# feature line
$lineBrush = New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(200, 255, 255, 255))
$fontLine = New-Object System.Drawing.Font("Microsoft YaHei UI", 19)
$g.DrawString("设备管理 · Logcat · 应用管理 · 文件传输 · 屏幕输入 · 性能诊断 · 系统调试",
    $fontLine, $lineBrush, 246, 194)

$g.Dispose()
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "banner saved: $OutFile"
