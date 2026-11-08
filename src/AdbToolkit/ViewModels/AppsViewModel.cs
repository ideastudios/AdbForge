using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using AdbToolkit.Models;
using AdbToolkit.Services;
using AdbToolkit.Views;

namespace AdbToolkit.ViewModels;

/// <summary>应用管理页：包列表（含应用名）、搜索、安装 APK、卸载、右键详情。</summary>
public partial class AppsViewModel : ObservableObject
{
    /// <summary>超过此大小的 APK 不拉取解析应用名（省流量）。</summary>
    private const long MaxApkBytes = 150L * 1024 * 1024;


    [GeneratedRegex(@"versionName=(\S+)")]
    private static partial Regex VersionNameRegex();

    [GeneratedRegex(@"versionCode=(\d+)")]
    private static partial Regex VersionCodeRegex();

    [GeneratedRegex(@"minSdk=(\d+)")]
    private static partial Regex MinSdkRegex();

    [GeneratedRegex(@"targetSdk=(\d+)")]
    private static partial Regex TargetSdkRegex();

    [GeneratedRegex(@"firstInstallTime=([0-9\- :]+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex FirstInstallRegex();

    [GeneratedRegex(@"lastUpdateTime=([0-9\- :]+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex LastUpdateRegex();

    [GeneratedRegex(@"installerPackageName=(\S+)")]
    private static partial Regex InstallerRegex();

    [GeneratedRegex(@"pkgFlags=\[([^\]]*)\]")]
    private static partial Regex PkgFlagsRegex();

    public AppsViewModel()
    {
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = FilterApp;

        LoadCommand = new RelayCommand(async () => await RunBusyAsync(LoadAsync));
        InstallCommand = new RelayCommand(async () => await RunBusyAsync(InstallAsync), () => !IsBusy);
        UninstallCommand = new RelayCommand(async p =>
        {
            if (p is AppInfo app) await RunBusyAsync(() => UninstallAsync(app));
        });
    }

    public ObservableCollection<AppInfo> Apps { get; } = new();

    public ICollectionView AppsView { get; }

    public RelayCommand LoadCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand UninstallCommand { get; }

    /// <summary>当前选中（含右键选中）的应用。</summary>
    private AppInfo? _selectedApp;
    public AppInfo? SelectedApp
    {
        get => _selectedApp;
        set => SetProperty(ref _selectedApp, value);
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) AppsView.Refresh();
        }
    }

    private bool _showSystem;
    public bool ShowSystem
    {
        get => _showSystem;
        set
        {
            if (SetProperty(ref _showSystem, value))
            {
                // 切换后重新拉取对应列表
                var serial = DeviceStore.SelectedSerial;
                if (serial != null) _ = RunBusyAsync(LoadAsync);
            }
        }
    }

    private bool _keepData;
    public bool KeepData
    {
        get => _keepData;
        set => SetProperty(ref _keepData, value);
    }

    private bool _grantPermissions;
    public bool GrantPermissions
    {
        get => _grantPermissions;
        set => SetProperty(ref _grantPermissions, value);
    }

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    private string _status = "点击「刷新」加载应用列表";
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private bool FilterApp(object obj)
    {
        if (obj is not AppInfo app) return false;
        return SearchText.Length == 0 ||
               app.Package.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
               app.Label.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Status = "错误：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null)
        {
            Status = "请先在顶部选择设备";
            return;
        }

        Status = $"正在加载{(ShowSystem ? "系统" : "第三方")}应用列表...";
        var apps = await AppSession.Adb.ListPackagesAsync(serial, ShowSystem);
        Apps.Clear();
        foreach (var app in apps)
        {
            // 有缓存的应用名直接显示；其余显示包名（右键详情/获取应用名后填充）
            var cached = AppMetaCache.Get(app.Package);
            if (cached?.Label != null) app.Label = cached.Label;
            Apps.Add(app);
        }
        Status = $"共 {apps.Count:N0} 个应用（应用名：右键「获取应用名」或「应用详情」后显示）";
    }

    /// <summary>
    /// 确保 manifest 解析结果可用：优先缓存；未缓存时拉取 APK 并字节解析（含应用名与组件）。
    /// 只在用户主动查看详情/获取应用名时调用，不做后台批量。
    /// </summary>
    private async Task<CachedAppMeta> EnsureMetaAsync(AppInfo app, string serial)
    {
        var meta = AppMetaCache.Get(app.Package);
        if (meta != null) return meta;

        var wc = await AppSession.Adb.RunShellAsync($"wc -c < {Quote(app.ApkPath)}", serial);
        long size = long.TryParse(wc.StdOut.Trim(), out var s) ? s : 0;
        if (size > MaxApkBytes)
        {
            Status = $"{app.Package} 的 APK 超过 150MB，跳过解析";
            return new CachedAppMeta();
        }

        Status = $"正在拉取并解析 {app.Package} 的 APK（{DeviceFile.FormatSize(size)}）...";
        var tmp = Path.Combine(AppMetaCache.TempApkDir, $"apk_{Guid.NewGuid():N}.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        try
        {
            var pull = await AppSession.Adb.PullAsync(serial, app.ApkPath, tmp);
            if (!pull.Success)
            {
                Status = $"拉取失败：{pull.Combined.Trim()}";
                return new CachedAppMeta();
            }

            var manifest = ApkParser.ParseFile(tmp);
            if (manifest != null)
            {
                meta = new CachedAppMeta
                {
                    Label = manifest.Label,
                    Activities = manifest.Activities,
                    Services = manifest.Services,
                    Receivers = manifest.Receivers,
                    Providers = manifest.Providers,
                    ManifestPermissions = manifest.Permissions,
                };
                AppMetaCache.Put(app.Package, meta);
                AppMetaCache.Save();
                if (manifest.Label != null) app.Label = manifest.Label;
            }
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
        meta ??= new CachedAppMeta();
        return meta;
    }

    /// <summary>右键「获取应用名」：只解析选中应用并回填第一列。</summary>
    public async Task GetLabelForSelectedAsync()
    {
        var app = SelectedApp;
        var serial = DeviceStore.SelectedSerial;
        if (app is null || serial is null) { Status = "请先选中应用"; return; }
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var meta = await EnsureMetaAsync(app, serial);
            if (meta.Label != null)
                Status = $"应用名：{meta.Label}（{app.Package}）";
            else
                Status = $"未能解析出应用名（{app.Package}），已显示包名";
        }
        catch (Exception ex)
        {
            Status = "错误：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>右键「提取 APK 到本地」：拉取应用全部 APK（含 split）到选择的文件夹。</summary>
    public async Task ExtractApkAsync()
    {
        var app = SelectedApp;
        var serial = DeviceStore.SelectedSerial;
        if (app is null || serial is null) { Status = "请先选中应用"; return; }
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = $"正在查询 {app.Package} 的 APK 路径...";
            var paths = await AppSession.Adb.GetApkPathsAsync(serial, app.Package);
            if (paths.Count == 0)
            {
                Status = $"未获取到 {app.Package} 的 APK 路径";
                return;
            }

            var folder = Dialogs.PickFolder(
                "选择 APK 保存位置（进入目标文件夹后点“打开”）",
                string.IsNullOrEmpty(AppSession.Settings.LastExtractDir) ? null : AppSession.Settings.LastExtractDir);
            if (folder is null) { Status = "已取消"; return; }
            AppSession.Settings.LastExtractDir = folder;
            AppSession.Settings.Save();

            // 命名：应用名_版本（应用名取缓存，取不到用包名）
            var dump = await AppSession.Adb.RunShellAsync($"dumpsys package {app.Package}", serial);
            var verName = VersionNameRegex().Match(dump.StdOut) is { Success: true } m
                ? m.Groups[1].Value : "";
            var label = AppMetaCache.Get(app.Package)?.Label ?? app.Package;
            var baseName = string.Join("", $"{label}_{verName}".Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(baseName)) baseName = app.Package;

            var saved = 0;
            for (var i = 0; i < paths.Count; i++)
            {
                Status = $"正在提取 ({i + 1}/{paths.Count}) {Path.GetFileName(paths[i])} ...";
                var fileName = paths.Count == 1
                    ? $"{baseName}.apk"
                    : i == 0 ? $"{baseName}_base.apk"
                    : $"{baseName}_{Path.GetFileName(paths[i])}";
                var r = await AppSession.Adb.PullAsync(serial, paths[i], Path.Combine(folder, fileName));
                if (r.Success) saved++;
            }

            Status = saved == paths.Count
                ? $"已提取 {saved} 个 APK 到 {folder}"
                : $"提取完成 {saved}/{paths.Count}（目标 {folder}）";
        }
        catch (Exception ex)
        {
            Status = "错误：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------- 详情 / 复制 / 跳转（右键菜单） ----------

    private static string Quote(string path) =>
        string.IsNullOrWhiteSpace(path) || !path.Contains(' ') ? path : $"\"{path}\"";

    /// <summary>复制应用名 / 包名 / APK 路径。</summary>
    public void CopyAppInfo(bool copyPath = false, bool copyLabel = false)
    {
        var app = SelectedApp;
        if (app is null) { Status = "请先选中应用"; return; }
        var text = copyPath ? app.ApkPath
            : copyLabel ? (app.Label is "…" or "—" ? app.Package : app.Label)
            : app.Package;
        try
        {
            Clipboard.SetText(text);
            Status = $"已复制：{text}";
        }
        catch { /* 剪贴板被占用等极端情况 */ }
    }

    /// <summary>打开系统的应用详情页。</summary>
    public async Task OpenSystemDetailAsync()
    {
        var app = SelectedApp;
        var serial = DeviceStore.SelectedSerial;
        if (app is null || serial is null) { Status = "请先选中应用"; return; }
        var r = await AppSession.Adb.RunShellAsync(
            $"am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d \"package:{app.Package}\"", serial);
        Status = r.Success ? $"已打开 {app.Package} 的系统详情页" : $"打开失败：{r.Combined.Trim()}";
    }

    /// <summary>弹出应用详情窗口（应用名/版本/时间/权限/页面/服务/广播）。</summary>
    public async Task ShowAppDetailAsync()
    {
        var app = SelectedApp;
        if (app is null) { Status = "请先选中应用"; return; }
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var serial = DeviceStore.SelectedSerial;
            if (serial is null) { Status = "请先在顶部选择设备"; return; }

            Status = $"正在获取 {app.Package} 详情...";

            // manifest 解析结果：优先缓存，未缓存现场拉取解析
            var meta = await EnsureMetaAsync(app, serial);

            var dump = await AppSession.Adb.RunShellAsync($"dumpsys package {app.Package}", serial);
            var outText = dump.StdOut;
            string G(Regex rx) => rx.Match(outText) is { Success: true } m ? m.Groups[1].Value : "—";

            // dumpsys 里的 requested permissions 块
            var dumpPerms = new List<string>();
            var lines = outText.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "requested permissions:") continue;
                for (var j = i + 1; j < lines.Length && lines[j].Trim().Length > 0; j++)
                {
                    var p = lines[j].Trim();
                    if (p.Contains('.')) dumpPerms.Add(p);
                }
                break;
            }

            // 合并 manifest 与 dumpsys 的权限并去重
            var allPerms = meta.ManifestPermissions
                .Concat(dumpPerms)
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            var wc2 = await AppSession.Adb.RunShellAsync($"wc -c < {Quote(app.ApkPath)}", serial);
            var sizeText = long.TryParse(wc2.StdOut.Trim(), out var sz)
                ? DeviceFile.FormatSize(sz) : "—";

            var flags = PkgFlagsRegex().Match(outText) is { Success: true } fm ? fm.Groups[1].Value : "";
            var flagsText = flags.Length == 0 ? "—" :
                string.Join(" ", flags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(f => f is "DEBUGGABLE" or "SYSTEM" or "PERSISTENT" or "STOPPED" or "INSTALLED"));

            var sb = new StringBuilder();
            sb.AppendLine($"应用名：      {(meta.Label ?? app.Label)}");
            sb.AppendLine($"包名：        {app.Package}");
            sb.AppendLine($"版本：        {G(VersionNameRegex())}（versionCode {G(VersionCodeRegex())}）");
            sb.AppendLine($"SDK：         minSdk {G(MinSdkRegex())} / targetSdk {G(TargetSdkRegex())}");
            sb.AppendLine($"APK 路径：    {app.ApkPath}");
            sb.AppendLine($"APK 大小：    {sizeText}");
            sb.AppendLine($"首次安装：    {G(FirstInstallRegex()).Trim()}");
            sb.AppendLine($"最近更新：    {G(LastUpdateRegex()).Trim()}");
            sb.AppendLine($"安装来源：    {G(InstallerRegex())}");
            sb.AppendLine($"标志：        {flagsText}");

            AppendSection(sb, $"申请的权限（{allPerms.Count}）", allPerms);
            AppendSection(sb, $"页面 Activities（{meta.Activities.Count}）", meta.Activities);
            AppendSection(sb, $"服务 Services（{meta.Services.Count}）", meta.Services);
            AppendSection(sb, $"广播接收器 Receivers（{meta.Receivers.Count}）", meta.Receivers);
            AppendSection(sb, $"内容提供器 Providers（{meta.Providers.Count}）", meta.Providers);

            new AppDetailWindow(
                $"{(meta.Label ?? app.Package)} 详情",
                sb.ToString())
            { Owner = Application.Current.MainWindow }
            .ShowDialog();

            Status = $"{app.Package} 详情已打开";
        }
        catch (Exception ex)
        {
            Status = "错误：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void AppendSection(StringBuilder sb, string title, List<string> items)
    {
        sb.AppendLine();
        sb.AppendLine($"{title}：");
        if (items.Count == 0)
        {
            sb.AppendLine("  （无）");
            return;
        }
        foreach (var item in items.Take(80)) sb.AppendLine($"  {item}");
        if (items.Count > 80) sb.AppendLine($"  …（其余 {items.Count - 80} 项省略）");
    }

    private async Task InstallAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null)
        {
            Status = "请先在顶部选择设备";
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要安装的 APK",
            Filter = "APK 文件 (*.apk)|*.apk|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        Status = $"正在安装 {System.IO.Path.GetFileName(dialog.FileName)} ...";
        var result = await AppSession.Adb.InstallAsync(
            serial, dialog.FileName, replace: true, grantPermissions: GrantPermissions);

        Status = result.Success
            ? $"安装成功：{System.IO.Path.GetFileName(dialog.FileName)}"
            : TranslateInstallError(result.Combined);
    }

    private async Task UninstallAsync(AppInfo app)
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) return;

        var confirm = MessageBox.Show(
            $"确定要卸载 {app.Package} 吗？{(KeepData ? "（保留数据）" : "")}",
            "卸载确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        Status = $"正在卸载 {app.Package} ...";
        var result = await AppSession.Adb.UninstallAsync(serial, app.Package, KeepData);

        if (result.Success)
        {
            Status = $"已卸载 {app.Package}";
            Apps.Remove(app);
        }
        else
        {
            Status = $"卸载失败：{result.Combined.Trim()}";
        }
    }

    /// <summary>把 adb 安装错误码翻译成人话。</summary>
    internal static string TranslateInstallError(string output)
    {
        if (output.Contains("INSTALL_FAILED_ALREADY_EXISTS")) return "应用已存在（替换安装可解决）";
        if (output.Contains("INSTALL_FAILED_INVALID_APK")) return "APK 文件无效";
        if (output.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE")) return "设备存储空间不足";
        if (output.Contains("INSTALL_FAILED_VERSION_DOWNGRADE")) return "设备上已是更高版本，无法降级安装";
        if (output.Contains("INSTALL_PARSE_FAILED_NO_CERTIFICATES")) return "APK 未签名或签名无效";
        if (output.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE")) return "签名与已装应用不一致，需先卸载";
        if (output.Contains("INCOMPATIBLE")) return "ABI / 平台不兼容";
        return output.Trim();
    }
}
