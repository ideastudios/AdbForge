# AdbForge English banner generator
param(
    [string]$OutFile = (Join-Path $PSScriptRoot "..\docs\images\banner-en.png"),
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

$bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)),
    (New-Object System.Drawing.Point($w, $h)),
    [System.Drawing.Color]::FromArgb(255, 0x4A, 0xA3, 0xFF),
    [System.Drawing.Color]::FromArgb(255, 0x1F, 0x6F, 0xDB))
$g.FillRectangle($bgBrush, 0, 0, $w, $h)

if (Test-Path $LogoPath) {
    $logo = [System.Drawing.Image]::FromFile($LogoPath)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($logo, 28, 30, 180, 180)
    $logo.Dispose()
}

$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)

$fontTitle = New-Object System.Drawing.Font("Segoe UI", 52,
    [System.Drawing.FontStyle]::Bold)
$g.DrawString("AdbForge", $fontTitle, $whiteBrush, 240, 46)

$subBrush = New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(235, 255, 255, 255))
$fontSub = New-Object System.Drawing.Font("Segoe UI", 20)
$g.DrawString("The all-in-one adb GUI toolbox for Android / AOSP developers on Windows",
    $fontSub, $subBrush, 246, 150)

$lineBrush = New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(200, 255, 255, 255))
$fontLine = New-Object System.Drawing.Font("Segoe UI", 18)
$g.DrawString("Devices - Logcat - Apps - Files - Screen & Input - Performance - System Debug",
    $fontLine, $lineBrush, 246, 196)

$g.Dispose()
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "banner saved: $OutFile"
