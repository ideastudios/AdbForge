# Capture the running AdbForge window to docs/images/<name>.png
# Usage: powershell -ExecutionPolicy Bypass -File scripts/capture-ui.ps1 -Name ui-devices
param(
    [string]$Name = "screenshot",
    [string]$OutDir = (Join-Path $PSScriptRoot "..\docs\images")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Cap {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[Win32Cap]::SetProcessDPIAware() | Out-Null

$p = Get-Process AdbForge -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) {
    Write-Host "ERROR: AdbForge window not found (is the app running?)"
    exit 1
}

# restore first if minimized
if ([Win32Cap]::IsIconic($p.MainWindowHandle)) {
    [Win32Cap]::ShowWindow($p.MainWindowHandle, 9) | Out-Null  # SW_RESTORE
    Start-Sleep -Milliseconds 500
}
[Win32Cap]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400

$rect = New-Object Win32Cap+RECT
[Win32Cap]::GetWindowRect($p.MainWindowHandle, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$g.Dispose()

New-Item $OutDir -ItemType Directory -Force | Out-Null
$out = Join-Path $OutDir "$Name.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "saved: $out"
