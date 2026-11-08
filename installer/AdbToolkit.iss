; AdbToolkit 安装包脚本（Inno Setup 6）
; 使用前先运行 scripts/publish.ps1 生成 publish 输出，然后用 ISCC 编译本文件
[Setup]
AppName=AdbToolkit
AppVersion=0.2.0
AppPublisher=AdbToolkit
AppPublisherURL=https://example.com/adbtoolkit
DefaultDirName={autopf}\AdbToolkit
DefaultGroupName=AdbToolkit
OutputDir=dist
OutputBaseFilename=AdbToolkit-0.2.0-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\AdbToolkit.exe

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Files]
Source: "src\AdbToolkit\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\ADB 工具箱"; Filename: "{app}\AdbToolkit.exe"
Name: "{group}\卸载 ADB 工具箱"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ADB 工具箱"; Filename: "{app}\AdbToolkit.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AdbToolkit.exe"; Description: "立即运行 ADB 工具箱"; Flags: nowait postinstall skipifsilent
