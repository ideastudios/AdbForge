using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using AdbToolkit.Resources;
using AdbToolkit.Services;

namespace AdbToolkit.ViewModels;

/// <summary>屏幕与输入页：截图、录屏、快捷键、文本/坐标注入、UI dump、Monkey。</summary>
public class ScreenInputViewModel : ObservableObject
{
    private Process? _recordProcess;
    private Process? _monkeyProcess;

    public ScreenInputViewModel()
    {
        ShotFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AdbToolkit");

        QuickKeys = new List<QuickKey>
        {
            new(Strings.Screen_KeyWake, "input keyevent 224"),
            new(Strings.Screen_KeySleep, "input keyevent 223"),
            new(Strings.Screen_KeyPower, "input keyevent 26"),
            new(Strings.Screen_KeyBack, "input keyevent 4"),
            new(Strings.Screen_KeyHome, "input keyevent 3"),
            new(Strings.Screen_KeyMenu, "input keyevent 82"),
            new(Strings.Screen_KeyVolUp, "input keyevent 24"),
            new(Strings.Screen_KeyVolDown, "input keyevent 25"),
            new(Strings.Screen_KeyMute, "input keyevent 164"),
            new(Strings.Screen_KeyScreenshot, "input keyevent 120"),
            new(Strings.Screen_KeyUnlock, "wm dismiss-keyguard"),
            new(Strings.Screen_KeyExpandNotify, "cmd statusbar expand-notifications"),
            new(Strings.Screen_KeyCollapsePanel, "cmd statusbar collapse"),
        };

        Bitrates = new List<string> { "4", "8", "12", "20" };
        Sizes = new List<string> { Strings.Screen_DefaultSize, "1280x720", "1920x1080" };

        ShotCommand = new RelayCommand(async () => await RunBusyAsync(ShotAsync));
        OpenFolderCommand = new RelayCommand(OpenFolder);
        RecordStartCommand = new RelayCommand(async () => await RunBusyAsync(RecordStartAsync), () => !IsRecording);
        RecordStopCommand = new RelayCommand(async () => await RunBusyAsync(RecordStopAsync), () => IsRecording);
        QuickKeyCommand = new RelayCommand(async p =>
        {
            if (p is string cmd) await RunQuietAsync(cmd);
        });
        SendTextCommand = new RelayCommand(async () => await RunBusyAsync(SendTextAsync));
        TapCommand = new RelayCommand(async () => await RunQuietAsync($"input tap {TapX} {TapY}"));
        SwipeCommand = new RelayCommand(async () =>
            await RunQuietAsync($"input swipe {SwipeX1} {SwipeY1} {SwipeX2} {SwipeY2} {SwipeMs}"));
        UiDumpCommand = new RelayCommand(async () => await RunBusyAsync(UiDumpAsync));
        MonkeyStartCommand = new RelayCommand(async () => await RunBusyAsync(MonkeyStartAsync), () => !IsMonkeyRunning);
        MonkeyStopCommand = new RelayCommand(MonkeyStop, () => IsMonkeyRunning);
    }

    public record QuickKey(string Label, string Command);

    public List<QuickKey> QuickKeys { get; }
    public ObservableCollection<string> MonkeyLog { get; } = new();

    public string ShotFolder { get => _shotFolder; private set => SetProperty(ref _shotFolder, value); }
    private string _shotFolder = "";

    public List<string> Bitrates { get; }
    public List<string> Sizes { get; }

    private string _selectedBitrate = "8";
    public string SelectedBitrate
    {
        get => _selectedBitrate;
        set => SetProperty(ref _selectedBitrate, value);
    }

    private int _timeLimit = 180;
    public int TimeLimit
    {
        get => _timeLimit;
        set => SetProperty(ref _timeLimit, value);
    }

    private string _selectedSize = Strings.Screen_DefaultSize;
    public string SelectedSize
    {
        get => _selectedSize;
        set => SetProperty(ref _selectedSize, value);
    }

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        private set => SetProperty(ref _isRecording, value);
    }

    private string _inputText = "";
    public string InputText
    {
        get => _inputText;
        set => SetProperty(ref _inputText, value);
    }

    private string _tapX = "500";
    public string TapX { get => _tapX; set => SetProperty(ref _tapX, value); }
    private string _tapY = "500";
    public string TapY { get => _tapY; set => SetProperty(ref _tapY, value); }
    private string _swipeX1 = "500";
    public string SwipeX1 { get => _swipeX1; set => SetProperty(ref _swipeX1, value); }
    private string _swipeY1 = "800";
    public string SwipeY1 { get => _swipeY1; set => SetProperty(ref _swipeY1, value); }
    private string _swipeX2 = "500";
    public string SwipeX2 { get => _swipeX2; set => SetProperty(ref _swipeX2, value); }
    private string _swipeY2 = "300";
    public string SwipeY2 { get => _swipeY2; set => SetProperty(ref _swipeY2, value); }
    private string _swipeMs = "300";
    public string SwipeMs { get => _swipeMs; set => SetProperty(ref _swipeMs, value); }

    private string _monkeyPackage = "";
    public string MonkeyPackage { get => _monkeyPackage; set => SetProperty(ref _monkeyPackage, value); }
    private string _monkeyCount = "500";
    public string MonkeyCount { get => _monkeyCount; set => SetProperty(ref _monkeyCount, value); }
    private string _monkeyThrottle = "100";
    public string MonkeyThrottle { get => _monkeyThrottle; set => SetProperty(ref _monkeyThrottle, value); }
    private string _monkeySeed = "";
    public string MonkeySeed { get => _monkeySeed; set => SetProperty(ref _monkeySeed, value); }

    private bool _isMonkeyRunning;
    public bool IsMonkeyRunning
    {
        get => _isMonkeyRunning;
        private set => SetProperty(ref _isMonkeyRunning, value);
    }

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    private string _status = Strings.More_Ready;
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public RelayCommand ShotCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand RecordStartCommand { get; }
    public RelayCommand RecordStopCommand { get; }
    public RelayCommand QuickKeyCommand { get; }
    public RelayCommand SendTextCommand { get; }
    public RelayCommand TapCommand { get; }
    public RelayCommand SwipeCommand { get; }
    public RelayCommand UiDumpCommand { get; }
    public RelayCommand MonkeyStartCommand { get; }
    public RelayCommand MonkeyStopCommand { get; }

    private async Task RunBusyAsync(Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await work(); }
        catch (Exception ex) { Status = string.Format(Strings.Common_ErrorFormat, ex.Message); }
        finally { IsBusy = false; }
    }

    private async Task RunQuietAsync(string shellCommand)
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = Strings.Common_SelectDeviceFirst; return; }
        var r = await AppSession.Adb.RunShellAsync(shellCommand, serial);
        Status = r.Success
            ? string.Format(Strings.Screen_ExecutedFormat, shellCommand)
            : string.Format(Strings.Screen_ExecFailedFormat, r.Combined.Trim());
    }

    private static string Quote(string s) =>
        string.IsNullOrWhiteSpace(s) || !s.Contains(' ') ? s : $"\"{s}\"";

    private async Task ShotAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = Strings.Common_SelectDeviceFirst; return; }

        Directory.CreateDirectory(ShotFolder);
        var file = Path.Combine(ShotFolder, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        Status = Strings.Screen_TakingShot;
        var png = await AppSession.Adb.CaptureScreenAsync(serial);
        await File.WriteAllBytesAsync(file, png);
        Status = string.Format(Strings.Common_SavedFormat, file);
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(ShotFolder);
        Process.Start(new ProcessStartInfo { FileName = ShotFolder, UseShellExecute = true });
    }

    private async Task RecordStartAsync()
    {
        await Task.CompletedTask;
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = Strings.Common_SelectDeviceFirst; return; }

        var devicePath = "/sdcard/adbtoolkit_record.mp4";
        var size = SelectedSize == Strings.Screen_DefaultSize ? null : SelectedSize;
        IsRecording = true;
        _recordProcess = AppSession.Adb.StartScreenrecord(
            serial, devicePath,
            int.TryParse(SelectedBitrate, out var br) ? br : 8,
            Math.Clamp(TimeLimit, 3, 180),
            size,
            _ => { /* 到时自然结束，文件留在设备上等手动停止/拉取 */ });
        Status = string.Format(Strings.Screen_RecordingFormat, TimeLimit);
    }

    private async Task RecordStopAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        var p = _recordProcess;
        _recordProcess = null;
        IsRecording = false;
        if (p is null) return;

        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        p.Dispose();

        // 设备端需要一点时间写入文件尾
        await Task.Delay(1500);
        if (serial is null) return;

        Directory.CreateDirectory(ShotFolder);
        var local = Path.Combine(ShotFolder, $"record_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        Status = Strings.Screen_PullingRecord;
        var r = await AppSession.Adb.PullAsync(serial, "/sdcard/adbtoolkit_record.mp4", local);
        Status = r.Success
            ? string.Format(Strings.Screen_RecordSavedFormat, local)
            : string.Format(Strings.Files_PullFailedFormat, r.Combined.Trim());
        await AppSession.Adb.RunShellAsync("rm -f /sdcard/adbtoolkit_record.mp4", serial);
    }

    private async Task SendTextAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = Strings.Common_SelectDeviceFirst; return; }
        var text = InputText;
        if (text.Length == 0) return;

        if (text.Any(c => c > 127))
        {
            Status = Strings.Screen_TextAsciiOnly;
            return;
        }
        var r = await AppSession.Adb.RunShellAsync($"input text {Quote(text)}", serial);
        Status = r.Success
            ? Strings.Screen_TextInjected
            : string.Format(Strings.Common_FailedFormat, r.Combined.Trim());
    }

    private async Task UiDumpAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = Strings.Common_SelectDeviceFirst; return; }

        Status = Strings.Screen_UiDumping;
        var dump = await AppSession.Adb.RunShellAsync("uiautomator dump /sdcard/uidump.xml", serial);
        if (!dump.Success)
        {
            Status = string.Format(Strings.Screen_UiDumpFailedFormat, dump.Combined.Trim());
            return;
        }

        Directory.CreateDirectory(ShotFolder);
        var local = Path.Combine(ShotFolder, $"uidump_{DateTime.Now:yyyyMMdd_HHmmss}.xml");
        var pull = await AppSession.Adb.PullAsync(serial, "/sdcard/uidump.xml", local);
        await AppSession.Adb.RunShellAsync("rm -f /sdcard/uidump.xml", serial);
        Status = pull.Success
            ? string.Format(Strings.Screen_UiDumpSavedFormat, local)
            : string.Format(Strings.Files_PullFailedFormat, pull.Combined.Trim());
    }

    private async Task MonkeyStartAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = Strings.Common_SelectDeviceFirst; return; }
        var pkg = MonkeyPackage.Trim();
        if (pkg.Length == 0) { Status = Strings.Screen_EnterMonkeyPkg; return; }

        var seedPart = int.TryParse(MonkeySeed, out var seed) && seed > 0 ? $"-s {seed} " : "";
        var cmd = $"monkey -p {pkg} {seedPart}--throttle {MonkeyThrottle} -v {MonkeyCount}";

        MonkeyLog.Clear();
        IsMonkeyRunning = true;
        _monkeyProcess = AppSession.Adb.StartShellProcess(serial, cmd,
            line =>
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    MonkeyLog.Add(line);
                    if (MonkeyLog.Count > 300) MonkeyLog.RemoveAt(0);
                });
            },
            code =>
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    IsMonkeyRunning = false;
                    Status = code == 0
                        ? Strings.Screen_MonkeyDone
                        : string.Format(Strings.Screen_MonkeyEndedFormat, code);
                });
            });
        Status = string.Format(Strings.Screen_MonkeyRunningFormat, pkg, MonkeyCount);
        await Task.CompletedTask;
    }

    private void MonkeyStop()
    {
        var p = _monkeyProcess;
        _monkeyProcess = null;
        IsMonkeyRunning = false;
        if (p is null) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        p.Dispose();
        Status = Strings.Screen_MonkeyStopped;
    }

    /// <summary>离开页面时回收子进程。</summary>
    public void Shutdown()
    {
        MonkeyStop();
        var p = _recordProcess;
        _recordProcess = null;
        IsRecording = false;
        if (p is null) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        p.Dispose();
    }
}
