卓匠 AdbForge — ADB 开发者工具箱

> 中文名「卓匠」，英文名 **AdbForge**。卓 = Android 之"卓"，匠 = 工具箱之"匠"；Forge = 锻造工坊。

面向 Android / AOSP 开发者的 Windows 桌面工具，**所有功能仅基于 adb 命令封装**，不依赖 scrcpy、jadx 等任何外部工具。

- 技术栈：WPF + .NET 8（C#，MVVM，零第三方依赖）
- 外观：浅色主题（Material Light 风格）
- 平台：Windows 10/11 x64

## 运行

```bash
# 环境要求：.NET 8 SDK（winget install Microsoft.DotNet.SDK.8）
dotnet run --project src/AdbToolkit
```

发布单文件 exe：

```bash
dotnet publish src/AdbToolkit -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## 项目结构

```
src/AdbToolkit/
├── App.xaml                     # 应用入口，合并主题资源
├── MainWindow.xaml              # 主窗口：侧边栏导航 + 全局设备栏 + 状态栏
├── app.manifest                 # 长路径支持、UTF-8 代码页
├── Themes/
│   ├── Colors.Light.xaml        # 浅色调色板（背景/主题色/状态色/日志级别色）
│   └── Controls.xaml            # 按钮/文本框/侧边栏/列表控件样式
├── Models/
│   ├── AdbDevice.cs             # 设备（serial/state/model）
│   ├── AppInfo.cs               # 应用包
│   └── LogEntry.cs              # logcat 行（threadtime 解析结果）
├── Services/
│   ├── AdbService.cs            # 核心：adb.exe 进程封装（devices/pm/logcat/install…）
│   ├── AppSettings.cs           # 设置持久化 %APPDATA%\AdbToolkit\settings.json
│   ├── AppSession.cs            # 全局单例（设置 + AdbService）
│   └── DeviceStore.cs           # 全局设备上下文（所有页面共用当前设备）
├── ViewModels/                  # MVVM（ObservableObject/RelayCommand 自实现）
│   ├── MainViewModel.cs         # 导航 + 设备 2s 轮询 + 状态栏
│   ├── DeviceManagerViewModel.cs
│   ├── LogcatViewModel.cs
│   ├── AppsViewModel.cs
│   ├── SettingsViewModel.cs
│   └── PlaceholderViewModel.cs
└── Views/                       # 各页面（4 个已实现 + 占位页）
```

## 当前已实现

| 模块 | 功能 |
|---|---|
| 设备管理 | 设备列表（2s 自动轮询、状态着色）、无线连接/配对（Android 11+）、一键重启到系统/Recovery/Bootloader |
| 日志 Logcat | 多 buffer（all/main/system/crash/events/kernel/radio）、最低级别、Tag/关键字过滤、级别着色、自动滚动、清空（-b all -c）、保存到文件 |
| 应用管理 | 第三方/系统应用列表、搜索、拖对话框安装 APK（-r/-t/-g）、卸载（-k 保留数据）、安装错误码中文翻译 |
| 设置 | adb.exe 路径配置、自动探测（程序目录 → LOCALAPPDATA SDK → PATH）、adb version 检测 |

规划中的模块见 [docs/ROADMAP.md](docs/ROADMAP.md)。

## 架构约定

- **全局设备上下文**：顶部设备栏选中后写入 `DeviceStore.Selected`，各页面通过 `DeviceStore.SelectedSerial` 读取 —— 新增页面时直接复用，不要在页面里自己选设备。
- **AdbService 只做进程封装**：所有 adb 交互收敛在 `Services/AdbService.cs`，输出统一 UTF-8 读取避免中文乱码；新功能先在这里加方法，ViewModel 只做编排。
- **新增一个页面**：`Views/` 加 UserControl → `ViewModels/` 加 VM → `MainViewModel.NavItems` 加一项 → `CreateView` 加一个 case。
- adb 不存在时不崩溃：`RunCoreAsync` 启动失败抛出带指引的异常，各 VM 统一捕获展示。

## 已知注意事项

- 二进制输出（如截图）必须走 `adb exec-out`，普通 `shell` 会破坏 PNG（后续屏幕模块实现时注意）。
- 设备监听当前为轮询（2s），后续可换 `adb track-devices` 协议实现实时推送。
- logcat 流程在离开页面时会自动 Kill 子进程树（`LogcatView.Unloaded`）。
