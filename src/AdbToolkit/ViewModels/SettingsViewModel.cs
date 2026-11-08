using System.IO;
using System.Text;
using System.Windows;
using AdbToolkit.Models;
using AdbToolkit.Services;

namespace AdbToolkit.ViewModels;

/// <summary>设置页：adb.exe 路径配置、检测、应用缓存清理。</summary>
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

    public RelayCommand BrowseCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand RefreshCacheCommand { get; }
    public RelayCommand ClearPcCacheCommand { get; }
    public RelayCommand ClearDeviceFilesCommand { get; }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 adb.exe",
            Filter = "adb.exe|adb.exe",
            InitialDirectory = System.IO.Path.GetDirectoryName(AdbPath) ?? "C:\\",
        };
        if (dialog.ShowDialog() == true) AdbPath = dialog.FileName;
    }

    private void Save()
    {
        AppSession.ApplyAdbPath(AdbPath.Trim());
        TestResult = $"已保存：{AppSession.Adb.AdbPath}";
    }

    private async Task TestAsync()
    {
        var path = AdbPath.Trim();
        AppSession.ApplyAdbPath(path);
        var result = await AppSession.Adb.RunAsync("version");
        TestResult = result.Success
            ? result.StdOut.Trim()
            : $"检测失败：{result.Combined.Trim()}";
    }

    // ---------- 应用缓存 ----------

    private void RefreshCacheInfo()
    {
        var sb = new StringBuilder();

        var metaPath = AppMetaCache.CacheFilePath;
        if (File.Exists(metaPath))
        {
            var fi = new FileInfo(metaPath);
            sb.AppendLine($"应用名/组件缓存（appmeta.json）：{DeviceFile.FormatSize(fi.Length)}，{AppMetaCache.Map.Count} 个应用");
        }
        else
        {
            sb.AppendLine("应用名/组件缓存（appmeta.json）：无");
        }

        var tempDir = AppMetaCache.TempApkDir;
        if (Directory.Exists(tempDir))
        {
            var files = Directory.GetFiles(tempDir);
            var total = files.Sum(f => new FileInfo(f).Length);
            sb.AppendLine($"临时 APK 文件（%TEMP%\\AdbToolkit）：{DeviceFile.FormatSize(total)}，{files.Length} 个文件");
        }
        else
        {
            sb.AppendLine("临时 APK 文件（%TEMP%\\AdbToolkit）：无");
        }

        CacheInfoText = sb.ToString();
    }

    private async Task ClearPcCacheAsync()
    {
        var confirm = MessageBox.Show(
            "将清除以下本工具生成的缓存：\n" +
            "· 应用名/组件缓存（appmeta.json）——清除后应用名需重新右键获取\n" +
            "· 临时 APK 文件（%TEMP%\\AdbToolkit）\n\n" +
            "设置（adb 路径等）不会清除。继续？",
            "清除 PC 缓存", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        AppMetaCache.ClearAndReset();

        var tempDir = AppMetaCache.TempApkDir;
        try
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
        catch (Exception ex)
        {
            TestResult = "缓存清除失败：" + ex.Message;
            return;
        }

        RefreshCacheInfo();
        TestResult = "PC 缓存已清除";
    }

    private async Task ClearDeviceFilesAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null)
        {
            TestResult = "请先在顶部选择设备";
            return;
        }

        // 本工具会在设备端创建这两类文件（后台日志 / 录屏残留）
        var ls = await AppSession.Adb.RunShellAsync(
            "ls /sdcard/adbtoolkit_logcat.txt /sdcard/adbtoolkit_record.mp4 2>/dev/null", serial);
        var names = ls.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).ToList();
        if (names.Count == 0)
        {
            TestResult = "设备端没有本工具创建的文件";
            return;
        }

        var r = await AppSession.Adb.RunShellAsync(
            "rm -f /sdcard/adbtoolkit_logcat.txt /sdcard/adbtoolkit_record.mp4", serial);
        TestResult = r.Success
            ? $"已清理设备端文件：{string.Join("、", names)}"
            : $"清理失败：{r.Combined.Trim()}";
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            TestResult = "错误：" + ex.Message;
        }
    }
}
