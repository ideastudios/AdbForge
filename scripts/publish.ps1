# AdbToolkit 发布脚本
# 用法：powershell -ExecutionPolicy Bypass -File scripts/publish.ps1 [-Version 0.2.0]
param(
    [string]$Version = "0.2.0",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "==> 发布 AdbToolkit v$Version（自包含单文件）..." -ForegroundColor Cyan
dotnet publish src/AdbToolkit -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

# 可选：代码签名（需要已安装 signtool 与证书）
# signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a `
#     "src/AdbToolkit/bin/Release/net8.0-windows/win-x64/publish/AdbToolkit.exe"

if ($SkipZip) { Write-Host "==> 跳过打包 zip" -ForegroundColor Yellow; return }

$dist = Join-Path $root "dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item $dist -ItemType Directory | Out-Null

Write-Host "==> 打包便携版 zip..." -ForegroundColor Cyan
Compress-Archive `
    -Path "src/AdbToolkit/bin/Release/net8.0-windows/win-x64/publish/*" `
    -DestinationPath (Join-Path $dist "AdbToolkit-v$Version-portable.zip") -Force

Write-Host "==> 完成！" -ForegroundColor Green
Write-Host "便携版：dist/AdbToolkit-v$Version-portable.zip"
Write-Host "安装包：用 Inno Setup 编译 installer/AdbToolkit.iss（先跑一次本脚本生成 publish 输出）"
Write-Host "更新服务器：把 dist 里的 zip 上传后，编辑版本描述 JSON："
Write-Host '  {"version": "'$Version'", "url": "https://你的域名/AdbToolkit-v'$Version'-portable.zip", "notes": "更新说明"}'
