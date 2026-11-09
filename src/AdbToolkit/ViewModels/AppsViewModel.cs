using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using AdbToolkit.Models;
using AdbToolkit.Resources;
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

    private string _status = Strings.Apps_ClickRefreshHint;
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
            Status = string.Format(Strings.Common_ErrorFormat, ex.Message);
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
            Status = Strings.Common_SelectDeviceFirst;
            return;
        }

        Status = ShowSystem ? Strings.Apps_LoadingSystemList : Strings.Apps_LoadingThirdPartyList;
        var apps = await AppSession.Adb.ListPackagesAsync(serial, ShowSystem);
        Apps.Clear();
        foreach (var app in apps)
        {
            // 有缓存的应用名直接显示；其余显示包名（右键详情/获取应用名后填充）
            var cached = AppMetaCache.Get(app.Package);
            if (cached?.Label != null) app.Label = cached.Label;
            Apps.Add(app);
        }
        Status = string.Format(Strings.Apps_LoadedCountFormat, apps.Count);
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
            Status = string.Format(Strings.Apps_ApkTooLargeFormat, app.Package);
            return new CachedAppMeta();
        }

        Status = string.Format(Strings.Apps_PullingApkFormat, app.Package, DeviceFile.FormatSize(size));
        var tmp = Path.Combine(AppMetaCache.TempApkDir, $"apk_{Guid.NewGuid():N}.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        try
        {
            var pull = await AppSession.Adb.PullAsync(serial, app.ApkPath, tmp);
            if (!pull.Success)
            {
                Status = string.Format(Strings.Common_FailedFormat, pull.Combined.Trim());
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
        if (app is null || serial is null) { Status = Strings.Apps_SelectAppFirst; return; }
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var meta = await EnsureMetaAsync(app, serial);
            if (meta.Label != null)
                Status = string.Format(Strings.Apps_LabelResultFormat, meta.Label, app.Package);
            else
                Status = string.Format(Strings.Apps_LabelParseFailedFormat, app.Package);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.Common_ErrorFormat, ex.Message);
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
        if (app is null || serial is null) { Status = Strings.Apps_SelectAppFirst; return; }
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = string.Format(Strings.Apps_QueryApkPathsFormat, app.Package);
            var paths = await AppSession.Adb.GetApkPathsAsync(serial, app.Package);
            if (paths.Count == 0)
            {
                Status = string.Format(Strings.Apps_NoApkPathsFormat, app.Package);
                return;
            }

            var folder = Dialogs.PickFolder(
                Strings.Apps_PickExtractFolder,
                string.IsNullOrEmpty(AppSession.Settings.LastExtractDir) ? null : AppSession.Settings.LastExtractDir);
            if (folder is null) { Status = Strings.Common_Cancelled; return; }
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
                Status = string.Format(Strings.Apps_ExtractingFormat, i + 1, paths.Count,
                    Path.GetFileName(paths[i]));
                var fileName = paths.Count == 1
                    ? $"{baseName}.apk"
                    : i == 0 ? $"{baseName}_base.apk"
                    : $"{baseName}_{Path.GetFileName(paths[i])}";
                var r = await AppSession.Adb.PullAsync(serial, paths[i], Path.Combine(folder, fileName));
                if (r.Success) saved++;
            }

            Status = saved == paths.Count
                ? string.Format(Strings.Apps_ExtractedFormat, saved, folder)
                : string.Format(Strings.Apps_ExtractDoneFormat, saved, paths.Count, folder);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.Common_ErrorFormat, ex.Message);
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
        if (app is null) { Status = Strings.Apps_SelectAppFirst; return; }
        var text = copyPath ? app.ApkPath
            : copyLabel ? (string.IsNullOrWhiteSpace(app.Label) ? app.Package : app.Label)
            : app.Package;
        try
        {
            Clipboard.SetText(text);
            Status = string.Format(Strings.Common_CopiedFormat, text);
        }
        catch { /* 剪贴板被占用等极端情况 */ }
    }

    /// <summary>打开系统的应用详情页。</summary>
    public async Task OpenSystemDetailAsync()
    {
        var app = SelectedApp;
        var serial = DeviceStore.SelectedSerial;
        if (app is null || serial is null) { Status = Strings.Apps_SelectAppFirst; return; }
        var r = await AppSession.Adb.RunShellAsync(
            $"am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d \"package:{app.Package}\"", serial);
        Status = r.Success
            ? string.Format(Strings.Apps_SystemDetailOpenedFormat, app.Package)
            : string.Format(Strings.Common_OpenFailedFormat, r.Combined.Trim());
    }

    /// <summary>弹出应用详情窗口（应用名/版本/时间/权限/页面/服务/广播）。</summary>
    public async Task ShowAppDetailAsync()
    {
        var app = SelectedApp;
        if (app is null) { Status = Strings.Apps_SelectAppFirst; return; }
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var serial = DeviceStore.SelectedSerial;
            if (serial is null) { Status = Strings.Common_SelectDeviceFirst; return; }

            Status = string.Format(Strings.Apps_FetchingDetailFormat, app.Package);

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
            sb.AppendLine(string.Format(Strings.Apps_DtlNameFormat, meta.Label ?? app.Label));
            sb.AppendLine(string.Format(Strings.Apps_DtlPkgFormat, app.Package));
            sb.AppendLine(string.Format(Strings.Apps_DtlVersionFormat, G(VersionNameRegex()), G(VersionCodeRegex())));
            sb.AppendLine(string.Format(Strings.Apps_DtlSdkFormat, G(MinSdkRegex()), G(TargetSdkRegex())));
            sb.AppendLine(string.Format(Strings.Apps_DtlApkPathFormat, app.ApkPath));
            sb.AppendLine(string.Format(Strings.Apps_DtlApkSizeFormat, sizeText));
            sb.AppendLine(string.Format(Strings.Apps_DtlFirstInstallFormat, G(FirstInstallRegex()).Trim()));
            sb.AppendLine(string.Format(Strings.Apps_DtlLastUpdateFormat, G(LastUpdateRegex()).Trim()));
            sb.AppendLine(string.Format(Strings.Apps_DtlInstallerFormat, G(InstallerRegex())));
            sb.AppendLine(string.Format(Strings.Apps_DtlFlagsFormat, flagsText));

            AppendSection(sb, string.Format(Strings.Apps_SecPermsFormat, allPerms.Count), allPerms);
            AppendSection(sb, string.Format(Strings.Apps_SecActivitiesFormat, meta.Activities.Count), meta.Activities);
            AppendSection(sb, string.Format(Strings.Apps_SecServicesFormat, meta.Services.Count), meta.Services);
            AppendSection(sb, string.Format(Strings.Apps_SecReceiversFormat, meta.Receivers.Count), meta.Receivers);
            AppendSection(sb, string.Format(Strings.Apps_SecProvidersFormat, meta.Providers.Count), meta.Providers);

            new AppDetailWindow(
                string.Format(Strings.Apps_DtlWindowTitleFormat, meta.Label ?? app.Package),
                sb.ToString())
            { Owner = Application.Current.MainWindow }
            .ShowDialog();

            Status = string.Format(Strings.Apps_DtlOpenedFormat, app.Package);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.Common_ErrorFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void AppendSection(StringBuilder sb, string title, List<string> items)
    {
        sb.AppendLine();
        sb.AppendLine(string.Format(Strings.Apps_SectionHeaderFormat, title));
        if (items.Count == 0)
        {
            sb.AppendLine(Strings.Apps_SectionEmpty);
            return;
        }
        foreach (var item in items.Take(80)) sb.AppendLine($"  {item}");
        if (items.Count > 80) sb.AppendLine(string.Format(Strings.Apps_SectionTruncatedFormat, items.Count - 80));
    }

    private async Task InstallAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null)
        {
            Status = Strings.Common_SelectDeviceFirst;
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Apps_PickApkTitle,
            Filter = Strings.Apps_ApkFilter,
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;

        await InstallBatchCoreAsync(serial, dialog.FileNames);
    }

    /// <summary>拖拽入口：批量安装拖进页面的 APK 文件（文件夹会展开取其中全部 APK）。</summary>
    public async Task InstallFilesFromDropAsync(IReadOnlyList<string> apkFiles)
    {
        await RunBusyAsync(() => InstallBatchCoreAsync(DeviceStore.SelectedSerial, apkFiles));
    }

    private async Task InstallBatchCoreAsync(string? serial, IReadOnlyList<string> apkFiles)
    {
        if (serial is null)
        {
            Status = Strings.Common_SelectDeviceFirst;
            return;
        }
        if (apkFiles.Count == 0) return;

        var ok = new List<string>();
        var failed = new List<string>();

        for (var i = 0; i < apkFiles.Count; i++)
        {
            var name = Path.GetFileName(apkFiles[i]);
            Status = string.Format(Strings.Apps_InstallingFormat, i + 1, apkFiles.Count, name);
            var result = await AppSession.Adb.InstallAsync(
                serial, apkFiles[i], replace: true, grantPermissions: GrantPermissions);

            if (result.Success) ok.Add(name);
            else failed.Add(string.Format(Strings.Apps_InstallFailedItemFormat, name,
                TranslateInstallError(result.Combined)));
        }

        Status = failed.Count == 0
            ? string.Format(Strings.Apps_BatchInstallOkFormat, ok.Count, apkFiles.Count)
            : string.Format(Strings.Apps_BatchInstallFailFormat, ok.Count, apkFiles.Count, failed.Count,
                string.Join(Strings.Apps_BatchFailSeparator, failed));
    }

    private async Task UninstallAsync(AppInfo app)
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) return;

        var confirm = MessageBox.Show(
            string.Format(Strings.Apps_UninstallConfirmFormat, app.Package,
                KeepData ? Strings.Apps_KeepDataSuffix : ""),
            Strings.Apps_UninstallConfirmCaption, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        Status = string.Format(Strings.Apps_UninstallingFormat, app.Package);
        var result = await AppSession.Adb.UninstallAsync(serial, app.Package, KeepData);

        if (result.Success)
        {
            Status = string.Format(Strings.Apps_UninstalledFormat, app.Package);
            Apps.Remove(app);
        }
        else
        {
            Status = string.Format(Strings.Apps_UninstallFailedFormat, result.Combined.Trim());
        }
    }

    /// <summary>把 adb 安装错误码翻译成人话。</summary>
    internal static string TranslateInstallError(string output)
    {
        if (output.Contains("INSTALL_FAILED_ALREADY_EXISTS")) return Strings.Apps_ErrAlreadyExists;
        if (output.Contains("INSTALL_FAILED_INVALID_APK")) return Strings.Apps_ErrInvalidApk;
        if (output.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE")) return Strings.Apps_ErrStorage;
        if (output.Contains("INSTALL_FAILED_VERSION_DOWNGRADE")) return Strings.Apps_ErrDowngrade;
        if (output.Contains("INSTALL_PARSE_FAILED_NO_CERTIFICATES")) return Strings.Apps_ErrNoCert;
        if (output.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE")) return Strings.Apps_ErrUpdateIncompatible;
        if (output.Contains("INCOMPATIBLE")) return Strings.Apps_ErrIncompatible;
        return output.Trim();
    }
}
