using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using AdbToolkit.Models;
using AdbToolkit.Resources;
using AdbToolkit.Services;

namespace AdbToolkit.ViewModels;

/// <summary>通用设置下拉项：Code 为存储值，DisplayName 为本地化显示名。</summary>
public class SettingOption
{
    public string Code { get; init; } = "";
    public string DisplayName { get; init; } = "";
}

/// <summary>设置页：adb 路径、主题、语言、托盘行为、更新检查、应用缓存清理、关于与隐私。</summary>
public class SettingsViewModel : ObservableObject
{
    public SettingsViewModel()
    {
        BrowseCommand = new RelayCommand(Browse);
        SaveCommand = new RelayCommand(Save);
        TestCommand = new RelayCommand(async () => await RunBusyAsync(TestAsync));
        RefreshCacheCommand = new RelayCommand(RefreshCacheInfo);
        ClearPcCacheCommand = new RelayCommand(async () => await RunBusyAsync(ClearPcCacheAsync));
        ClearDeviceFilesCommand = new RelayCommand(async () => await RunBusyAsync(ClearDeviceFilesAsync));
        CheckUpdateCommand = new RelayCommand(async () => await RunBusyAsync(CheckUpdateAsync));
        OpenLogsCommand = new RelayCommand(OpenLogs);

        // 选中项必须引用 ItemsSource 列表中的实例，否则 ComboBox 显示空白
        _selectedTheme = Themes.FirstOrDefault(t => t.Code.Equals(AppSession.Settings.Theme, StringComparison.OrdinalIgnoreCase))
                         ?? Themes[0];
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code.Equals(AppSession.Settings.Language, StringComparison.OrdinalIgnoreCase))
                            ?? Languages[0];
        RefreshCacheInfo();
    }

    private string _adbPath = AppSession.Adb.AdbPath;
    public string AdbPath
    {
        get => _adbPath;
        set => SetProperty(ref _adbPath, value);
    }

    private string _testResult = "";
    public string TestResult
    {
        get => _testResult;
        set => SetProperty(ref _testResult, value);
    }

    private string _cacheInfoText = "";
    public string CacheInfoText
    {
        get => _cacheInfoText;
        private set => SetProperty(ref _cacheInfoText, value);
    }

    // ---------- 外观 ----------

    public List<SettingOption> Themes { get; } = new()
    {
        new() { Code = ThemeManager.Light, DisplayName = Strings.Settings_ThemeLight },
        new() { Code = ThemeManager.Dark, DisplayName = Strings.Settings_ThemeDark },
    };

    private SettingOption _selectedTheme = null!;
    public SettingOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value)) return;
            AppSession.Settings.Theme = value.Code;
            AppSession.Settings.Save();
            TestResult = string.Format(Strings.Settings_ThemeRestartFormat, value.DisplayName);
        }
    }

    // ---------- 语言 ----------

    public List<SettingOption> Languages { get; } = new()
    {
        new() { Code = "zh", DisplayName = Strings.Settings_LangZh },
        new() { Code = "en", DisplayName = Strings.Settings_LangEn },
    };

    private SettingOption _selectedLanguage = null!;
    public SettingOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetProperty(ref _selectedLanguage, value)) return;
            AppSession.Settings.Language = value.Code;
            AppSession.Settings.Save();
            TestResult = Strings.Settings_LanguageRestartHint;
        }
    }

    // ---------- 托盘 ----------

    private bool _hideToTrayOnClose = AppSession.Settings.HideToTrayOnClose;
    public bool HideToTrayOnClose
    {
        get => _hideToTrayOnClose;
        set
        {
            if (SetProperty(ref _hideToTrayOnClose, value))
            {
                AppSession.Settings.HideToTrayOnClose = value;
                AppSession.Settings.Save();
            }
        }
    }

    // ---------- 更新 ----------

    public string VersionText => $"v{UpdateService.CurrentVersion}";

    private string _updateUrl = AppSession.Settings.UpdateUrl;
    public string UpdateUrl
    {
        get => _updateUrl;
        set
        {
            if (SetProperty(ref _updateUrl, value))
            {
                AppSession.Settings.UpdateUrl = value;
                AppSession.Settings.Save();
            }
        }
    }

    // ---------- 命令 ----------

    public RelayCommand BrowseCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand RefreshCacheCommand { get; }
    public RelayCommand ClearPcCacheCommand { get; }
    public RelayCommand ClearDeviceFilesCommand { get; }
    public RelayCommand CheckUpdateCommand { get; }
    public RelayCommand OpenLogsCommand { get; }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Settings_BrowseTitle,
            Filter = "adb.exe|adb.exe",
            InitialDirectory = Path.GetDirectoryName(AdbPath) ?? "C:\\",
        };
        if (dialog.ShowDialog() == true) AdbPath = dialog.FileName;
    }

    private void Save()
    {
        AppSession.ApplyAdbPath(AdbPath.Trim());
        TestResult = string.Format(Strings.Settings_SavedFormat, AppSession.Adb.AdbPath);
    }

    private async Task TestAsync()
    {
        var path = AdbPath.Trim();
        AppSession.ApplyAdbPath(path);
        var result = await AppSession.Adb.RunAsync("version");
        TestResult = result.Success
            ? result.StdOut.Trim()
            : string.Format(Strings.Settings_TestFailedFormat, result.Combined.Trim());
    }

    private async Task CheckUpdateAsync()
    {
        var url = UpdateUrl.Trim();
        if (url.Length == 0)
        {
            await CheckGitHubUpdateAsync();
            return;
        }

        TestResult = Strings.Settings_CheckingUpdate;
        var result = await UpdateService.CheckAsync(url);
        TestResult = result.Message;
        if (result.HasNewVersion && !string.IsNullOrEmpty(result.DownloadUrl))
        {
            var open = MessageBox.Show(Strings.Settings_OpenDownloadPrompt, Strings.Settings_NewVersionFound,
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes) UpdateService.OpenDownloadUrl(result.DownloadUrl);
        }
    }

    /// <summary>默认来源：GitHub Releases（发现新版直接下载安装包）。</summary>
    private async Task CheckGitHubUpdateAsync()
    {
        TestResult = Strings.Update_GitHubChecking;
        var result = await UpdateService.CheckGitHubAsync();
        TestResult = result.Message;

        if (!result.HasNewVersion) return;

        if (string.IsNullOrEmpty(result.AssetUrl) || string.IsNullOrEmpty(result.AssetName))
        {
            // Release 未附带安装包：引导打开 Release 页面
            TestResult = Strings.Update_NoAsset;
            if (!string.IsNullOrEmpty(result.PageUrl)) UpdateService.OpenDownloadUrl(result.PageUrl);
            return;
        }

        var sizeMb = result.AssetSize / 1024.0 / 1024.0;
        var go = MessageBox.Show(
            string.Format(Strings.Update_DownloadPromptFormat,
                result.AssetName, sizeMb.ToString("F1"),
                string.IsNullOrEmpty(result.Notes) ? "" : "\n" + result.Notes + "\n"),
            Strings.Settings_NewVersionFound, MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (go != MessageBoxResult.Yes)
        {
            if (!string.IsNullOrEmpty(result.PageUrl)) UpdateService.OpenDownloadUrl(result.PageUrl);
            return;
        }

        try
        {
            var lastPct = -1L;
            var dest = await UpdateService.DownloadReleaseAsync(result.AssetUrl, result.AssetName, (read, total) =>
            {
                var pct = read * 100 / total;
                if (pct != lastPct)
                {
                    lastPct = pct;
                    TestResult = string.Format(Strings.Update_DownloadingFormat,
                        pct, read / 1024.0 / 1024.0, total / 1024.0 / 1024.0);
                }
            });
            TestResult = string.Format(Strings.Update_DownloadedFormat, dest);
            Process.Start("explorer.exe", $"/select,\"{dest}\"");
        }
        catch (Exception ex)
        {
            TestResult = string.Format(Strings.Update_DownloadFailedFormat, ex.Message);
            if (!string.IsNullOrEmpty(result.PageUrl)) UpdateService.OpenDownloadUrl(result.PageUrl);
        }
    }

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(LogService.LogDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = LogService.LogDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            TestResult = string.Format(Strings.Settings_OpenLogsFailedFormat, ex.Message);
        }
    }

    // ---------- 应用缓存 ----------

    private void RefreshCacheInfo()
    {
        var sb = new StringBuilder();

        var metaPath = AppMetaCache.CacheFilePath;
        if (File.Exists(metaPath))
        {
            var fi = new FileInfo(metaPath);
            sb.AppendLine(string.Format(Strings.Settings_CacheMetaFormat,
                DeviceFile.FormatSize(fi.Length), AppMetaCache.Map.Count));
        }
        else
        {
            sb.AppendLine(Strings.Settings_CacheMetaNone);
        }

        var tempDir = AppMetaCache.TempApkDir;
        if (Directory.Exists(tempDir))
        {
            var files = Directory.GetFiles(tempDir);
            var total = files.Sum(f => new FileInfo(f).Length);
            sb.AppendLine(string.Format(Strings.Settings_CacheTempFormat,
                DeviceFile.FormatSize(total), files.Length));
        }
        else
        {
            sb.AppendLine(Strings.Settings_CacheTempNone);
        }

        CacheInfoText = sb.ToString();
    }

    private async Task ClearPcCacheAsync()
    {
        var confirm = MessageBox.Show(
            Strings.Settings_ClearPcCacheConfirm,
            Strings.Settings_ClearPcCacheCaption, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        AppMetaCache.ClearAndReset();

        var tempDir = AppMetaCache.TempApkDir;
        try
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
        catch (Exception ex)
        {
            TestResult = string.Format(Strings.Common_FailedFormat, ex.Message);
            return;
        }

        RefreshCacheInfo();
        TestResult = Strings.Settings_CacheCleared;
    }

    private async Task ClearDeviceFilesAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null)
        {
            TestResult = Strings.Common_SelectDeviceFirst;
            return;
        }

        // 本工具会在设备端创建这两类文件（后台日志 / 录屏残留）
        var ls = await AppSession.Adb.RunShellAsync(
            "ls /sdcard/adbtoolkit_logcat.txt /sdcard/adbtoolkit_record.mp4 2>/dev/null", serial);
        var names = ls.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).ToList();
        if (names.Count == 0)
        {
            TestResult = Strings.Settings_NoToolFilesOnDevice;
            return;
        }

        var r = await AppSession.Adb.RunShellAsync(
            "rm -f /sdcard/adbtoolkit_logcat.txt /sdcard/adbtoolkit_record.mp4", serial);
        TestResult = r.Success
            ? string.Format(Strings.Settings_CleanedDeviceFilesFormat, string.Join("、", names))
            : string.Format(Strings.Common_FailedFormat, r.Combined.Trim());
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            TestResult = string.Format(Strings.Common_ErrorFormat, ex.Message);
        }
    }
}
