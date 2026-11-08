# AdbForge full README screenshot pipeline
# Usage: powershell -ExecutionPolicy Bypass -File scripts/take-screenshots.ps1
param(
    [string]$ExePath = (Join-Path $PSScriptRoot "..\src\AdbToolkit\bin\Debug\net8.0-windows\AdbForge.exe"),
    [string]$OutDir = (Join-Path $PSScriptRoot "..\docs\images")
)
$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Cap {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[Cap]::SetProcessDPIAware() | Out-Null

function Get-AppProcess {
    Get-Process AdbForge -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}

function Start-App {
    if (-not (Get-AppProcess)) {
        Start-Process $ExePath
        Start-Sleep -Seconds 4
    }
}

function Stop-App {
    Get-Process AdbForge -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

function Get-AppRect {
    $p = Get-AppProcess
    $r = New-Object Cap+RECT
    [Cap]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    return $r
}

function Focus-App {
    $p = Get-AppProcess
    if ((Get-AppProcess) -and [Cap]::IsIconic($p.MainWindowHandle)) {
        [Cap]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
        Start-Sleep -Milliseconds 500
    }
    [Cap]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300
}

function Click-At([int]$x, [int]$y) {
    [Cap]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Cap]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
    [Cap]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
    Start-Sleep -Milliseconds 200
}

# sidebar item vertical offsets (physical px from window top, matches current layout)
$navOffsets = @{
    "设备管理"   = 234
    "日志 Logcat" = 290
    "应用管理"   = 346
    "文件传输"   = 401
    "屏幕与输入" = 457
    "性能诊断"   = 513
    "系统调试"   = 569
    "更多工具"   = 625
    "设置"       = 681
}

function Navigate([string]$page) {
    Focus-App
    $r = Get-AppRect
    Click-At ($r.Left + 164) ($r.Top + $navOffsets[$page])
    Start-Sleep -Milliseconds 600
}

function Invoke-Button([string]$name) {
    $p = Get-AppProcess
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($el) {
        $invoke = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $invoke.Invoke()
        Start-Sleep -Milliseconds 300
        return $true
    }
    return $false
}

function Toggle-Checkbox([string]$name) {
    $p = Get-AppProcess
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($el) {
        $toggle = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $toggle.Toggle()
        Start-Sleep -Milliseconds 300
        return $true
    }
    return $false
}

function Capture([string]$name) {
    Focus-App
    Start-Sleep -Milliseconds 300
    $p = Get-AppProcess
    $r = New-Object Cap+RECT
    [Cap]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    New-Item $OutDir -ItemType Directory -Force | Out-Null
    $out = Join-Path $OutDir "$name.png"
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "saved: $out"
}

function Set-Theme([string]$theme) {
    Stop-App
    $settingsPath = "$env:APPDATA\AdbToolkit\settings.json"
    $json = Get-Content $settingsPath -Raw | ConvertFrom-Json
    $json.Theme = $theme
    $json | ConvertTo-Json | Set-Content $settingsPath -Encoding UTF8
    Start-Process $ExePath
    Start-Sleep -Seconds 4
}

# ================= pipeline =================

Write-Host "== 1/6 设备管理页 =="
Start-App
Navigate "设备管理"
Start-Sleep -Seconds 3
Capture "ui-devices"

Write-Host "== 2/6 日志 Logcat 页（抓取中截图）=="
Navigate "日志 Logcat"
Start-Sleep -Seconds 1
Invoke-Button "开始抓取" | Out-Null
Start-Sleep -Seconds 4
Capture "ui-logcat"
Invoke-Button "停止" | Out-Null

Write-Host "== 3/6 文件传输页 =="
Navigate "文件传输"
Start-Sleep -Seconds 4
Capture "ui-files"

Write-Host "== 4/6 应用管理页（系统应用模式，第三方列表为空的设备）=="
Navigate "应用管理"
Start-Sleep -Seconds 1
Toggle-Checkbox "显示系统应用" | Out-Null
Start-Sleep -Seconds 4
Capture "ui-apps"

Write-Host "== 5/6 深色模式 =="
Set-Theme "dark"
Navigate "设备管理"
Start-Sleep -Seconds 2
Capture "ui-dark"

Write-Host "== 6/6 恢复浅色主题 =="
Set-Theme "light"
Start-Sleep -Seconds 2

Write-Host "ALL DONE - screenshots in $OutDir"
