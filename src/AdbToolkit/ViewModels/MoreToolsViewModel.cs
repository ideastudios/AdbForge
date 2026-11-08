using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AdbToolkit.Services;

namespace AdbToolkit.ViewModels;

/// <summary>更多工具页：输入法、通知、代理、多用户、权限、Bugreport、Sideload、后台日志、ADB Server。</summary>
public class MoreToolsViewModel : ObservableObject
{
    public MoreToolsViewModel()
    {
        ImeRefreshCommand = new RelayCommand(async () => await RunBusyAsync(LoadImeAsync));
        ImeSetCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            if (SelectedIme is null) return;
            var r = await RunShell($"ime set {SelectedIme}");
            Status = r.Success ? $"输入法已切换：{SelectedIme}" : $"失败：{r.Combined.Trim()}";
        }));

        NotifyPostCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            var r = await RunShell(
                $"cmd notification post -S bigtext -t {Quote(NotifyTitle.Trim())} {Quote(NotifyTag.Trim())} {Quote(NotifyContent.Trim())}");
            Status = r.Success ? "测试通知已发送" : $"失败（需 Android 10+）：{r.Combined.Trim()}";
        }));
        NotifyViewCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            var r = await RunShell("dumpsys notification --noredact");
            NotifyOutput = r.Combined.Length > 8000 ? r.Combined[..8000] + "\n...(已截断)" : r.Combined;
        }));

        ProxySetCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            var r = await RunShell($"settings put global http_proxy {ProxyTarget.Trim()}");
            await LoadProxyAsync();
            Status = r.Success ? $"代理已设置：{ProxyTarget}" : $"失败：{r.Combined.Trim()}";
        }));
        ProxyClearCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            await RunShell("settings delete global http_proxy");
            await LoadProxyAsync();
            Status = "代理已清除";
        }));

        UserRefreshCommand = new RelayCommand(async () => await RunBusyAsync(LoadUsersAsync));
        UserCreateCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            if (NewUserName.Trim().Length == 0) return;
            var r = await RunShell($"pm create-user {Quote(NewUserName.Trim())}");
            Status = r.Success ? r.Combined.Trim() : $"失败：{r.Combined.Trim()}";
            await LoadUsersAsync();
        }));
        UserRemoveCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            if (SelectedUserText is null) return;
            var id = ExtractUserId(SelectedUserText);
            if (id is null) return;
            var r = await RunShell($"pm remove-user {id}");
            Status = r.Success ? $"已删除用户 {id}" : $"失败：{r.Combined.Trim()}";
            await LoadUsersAsync();
        }));

        PermissionGrantCommand = new RelayCommand(async p =>
        {
            var grant = p as string == "grant";
            await RunBusyAsync(async () =>
            {
                var r = await RunShell($"pm {(grant ? "grant" : "revoke")} {PermPackage.Trim()} {PermName.Trim()}");
                Status = r.Success
                    ? $"已{(grant ? "授予" : "撤销")} {PermName} → {PermPackage}"
                    : $"失败：{r.Combined.Trim()}";
            });
        });

        BugreportCommand = new RelayCommand(async () => await RunBusyAsync(BugreportAsync));
        OpenBugreportFolderCommand = new RelayCommand(OpenBugreportFolder);
        ServerKillCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            await AppSession.Adb.RunAsync("kill-server");
            Status = "adb server 已停止";
        }));
        ServerStartCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            await AppSession.Adb.RunAsync("start-server");
            Status = "adb server 已启动";
        }));

        SideloadCommand = new RelayCommand(async () => await RunBusyAsync(SideloadAsync));

        LogBufStartCommand = new RelayCommand(async () => await RunBusyAsync(DeviceLogStartAsync), () => !IsDeviceLogRunning);
        LogBufStopCommand = new RelayCommand(async () => await RunBusyAsync(DeviceLogStopAsync), () => IsDeviceLogRunning);
        LogBufPullCommand = new RelayCommand(async () => await RunBusyAsync(DeviceLogPullAsync));
    }

    // ---------- 输入法 ----------
    public ObservableCollection<string> Imes { get; } = new();
    private string? _selectedIme;
    public string? SelectedIme { get => _selectedIme; set => SetProperty(ref _selectedIme, value); }

    // ---------- 通知 ----------
    private string _notifyTitle = "测试通知";
    public string NotifyTitle { get => _notifyTitle; set => SetProperty(ref _notifyTitle, value); }
    private string _notifyContent = "来自 AdbToolkit 的测试通知";
    public string NotifyContent { get => _notifyContent; set => SetProperty(ref _notifyContent, value); }
    private string _notifyTag = "adbtoolkit";
    public string NotifyTag { get => _notifyTag; set => SetProperty(ref _notifyTag, value); }
    private string _notifyOutput = "";
    public string NotifyOutput { get => _notifyOutput; private set => SetProperty(ref _notifyOutput, value); }

    // ---------- 代理 ----------
    private string _proxyTarget = "";
    public string ProxyTarget { get => _proxyTarget; set => SetProperty(ref _proxyTarget, value); }
    private string _proxyCurrent = "";
    public string ProxyCurrent { get => _proxyCurrent; private set => SetProperty(ref _proxyCurrent, value); }

    // ---------- 多用户 ----------
    public ObservableCollection<string> Users { get; } = new();
    private string? _selectedUserText;
    public string? SelectedUserText { get => _selectedUserText; set => SetProperty(ref _selectedUserText, value); }
    private string _newUserName = "";
    public string NewUserName { get => _newUserName; set => SetProperty(ref _newUserName, value); }

    // ---------- 权限 ----------
    private string _permPackage = "";
    public string PermPackage { get => _permPackage; set => SetProperty(ref _permPackage, value); }
    private string _permName = "";
    public string PermName { get => _permName; set => SetProperty(ref _permName, value); }

    // ---------- Bugreport ----------
    private string _bugreportPath = "";
    public string BugreportPath { get => _bugreportPath; private set => SetProperty(ref _bugreportPath, value); }

    // ---------- Sideload ----------
    private string _sideloadZip = "";
    public string SideloadZip { get => _sideloadZip; set => SetProperty(ref _sideloadZip, value); }

    // ---------- 后台日志 ----------
    public ObservableCollection<string> LogBuffers { get; } = new() { "main", "system", "crash", "kernel", "all" };
    private string _selectedLogBuffer = "main";
    public string SelectedLogBuffer { get => _selectedLogBuffer; set => SetProperty(ref _selectedLogBuffer, value); }
    private bool _isDeviceLogRunning;
    public bool IsDeviceLogRunning
    {
        get => _isDeviceLogRunning;
        private set => SetProperty(ref _isDeviceLogRunning, value);
    }

    private bool _busy;
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    private string _status = "就绪";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public RelayCommand ImeRefreshCommand { get; }
    public RelayCommand ImeSetCommand { get; }
    public RelayCommand NotifyPostCommand { get; }
    public RelayCommand NotifyViewCommand { get; }
    public RelayCommand ProxySetCommand { get; }
    public RelayCommand ProxyClearCommand { get; }
    public RelayCommand UserRefreshCommand { get; }
    public RelayCommand UserCreateCommand { get; }
    public RelayCommand UserRemoveCommand { get; }
    public RelayCommand PermissionGrantCommand { get; }
    public RelayCommand BugreportCommand { get; }
    public RelayCommand OpenBugreportFolderCommand { get; }
    public RelayCommand ServerKillCommand { get; }
    public RelayCommand ServerStartCommand { get; }
    public RelayCommand SideloadCommand { get; }
    public RelayCommand LogBufStartCommand { get; }
    public RelayCommand LogBufStopCommand { get; }
    public RelayCommand LogBufPullCommand { get; }

    private async Task RunBusyAsync(Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await work(); }
        catch (Exception ex) { Status = "错误：" + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task<AdbResult> RunShell(string cmd)
    {
        var serial = DeviceStore.SelectedSerial ?? throw new InvalidOperationException("请先在顶部选择设备");
        return await AppSession.Adb.RunShellAsync(cmd, serial);
    }

    private static string Quote(string s) =>
        string.IsNullOrWhiteSpace(s) || !s.Contains(' ') ? s : $"\"{s}\"";

    public async Task LoadOnNavigateAsync()
    {
        if (_proxyCurrent.Length > 0) return;
        await RunBusyAsync(async () =>
        {
            await LoadImeAsync();
            await LoadProxyAsync();
            await LoadUsersAsync();
        });
    }

    // ---------- 输入法 ----------

    private async Task LoadImeAsync()
    {
        var r = await RunShell("ime list -s");
        Imes.Clear();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Imes.Add(line.Trim());
        Status = $"共 {Imes.Count} 个输入法";
    }

    // ---------- 代理 ----------

    private async Task LoadProxyAsync()
    {
        var r = await RunShell("settings get global http_proxy");
        ProxyCurrent = r.StdOut.Trim().Length == 0 || r.StdOut.Trim() == ":0" ? "（未设置）" : r.StdOut.Trim();
    }

    // ---------- 多用户 ----------

    private async Task LoadUsersAsync()
    {
        var users = await RunShell("pm list users");
        var current = await RunShell("am get-current-user");
        Users.Clear();
        foreach (var raw in users.StdOut.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("UserInfo{")) continue;
            Users.Add(line + (line.Contains($":{current.StdOut.Trim()}:") ? "  ← 当前" : ""));
        }
        if (Users.Count == 0) Users.Add("（未能解析用户列表）");
    }

    private static int? ExtractUserId(string userInfo)
    {
        var start = userInfo.IndexOf('{');
        var end = userInfo.IndexOf(':', start);
        return start >= 0 && end > start && int.TryParse(userInfo[(start + 1)..end], out var id) ? id : null;
    }

    // ---------- Bugreport ----------

    private async Task BugreportAsync()
    {
        var serial = DeviceStore.SelectedSerial!;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AdbToolkit");
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, $"bugreport_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

        Status = "正在采集 Bugreport（约 1-3 分钟，请保持连接）...";
        var r = await AppSession.Adb.RunAsync($"bugreport \"{zip}\"", serial);
        BugreportPath = zip;
        Status = r.Success ? $"Bugreport 已保存：{zip}" : $"采集失败：{r.Combined.Trim()}";
    }

    private void OpenBugreportFolder()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AdbToolkit");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    // ---------- Sideload ----------

    private async Task SideloadAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        var zip = SideloadZip.Trim().Trim('"');
        if (serial is null) { Status = "请先在顶部选择设备"; return; }
        if (zip.Length == 0 || !File.Exists(zip)) { Status = "请选择有效的 OTA zip 包"; return; }

        if (MessageBox.Show("将重启设备到 Sideload 模式并推送 OTA 包。确认继续？",
                "Sideload", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var rb = await AppSession.Adb.RunAsync("reboot sideload", serial);
        if (!rb.Success) { Status = $"重启失败：{rb.Combined.Trim()}"; return; }

        Status = "等待设备进入 Sideload 模式...";
        await AppSession.Adb.RunAsync("wait-for-sideload", serial);
        Status = "正在推送 OTA 包（进度请看设备屏幕）...";
        var r = await AppSession.Adb.RunAsync($"sideload \"{zip}\"", serial);
        Status = r.Success ? "OTA 推送完成" : $"Sideload 失败：{r.Combined.Trim()}";
    }

    // ---------- 设备端后台日志 ----------

    private async Task DeviceLogStartAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = "请先在顶部选择设备"; return; }

        var buf = SelectedLogBuffer == "all" ? "-b all" : $"-b {SelectedLogBuffer}";
        await AppSession.Adb.RunShellAsync(
            $"sh -c \"nohup logcat -v time {buf} > /sdcard/adbtoolkit_logcat.txt 2>&1 &\"", serial);
        IsDeviceLogRunning = true;
        Status = "设备端后台日志已启动（拔线也能继续采集）";
    }

    private async Task DeviceLogStopAsync()
    {
        var serial = DeviceStore.SelectedSerial!;
        await AppSession.Adb.RunShellAsync("kill $(pidof logcat) 2>/dev/null", serial);
        IsDeviceLogRunning = false;
        Status = "设备端后台日志已停止，可点击「拉取日志」";
    }

    private async Task DeviceLogPullAsync()
    {
        var serial = DeviceStore.SelectedSerial!;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AdbToolkit");
        Directory.CreateDirectory(dir);
        var local = Path.Combine(dir, $"device_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        var r = await AppSession.Adb.PullAsync(serial, "/sdcard/adbtoolkit_logcat.txt", local);
        Status = r.Success ? $"日志已拉取：{local}" : $"拉取失败：{r.Combined.Trim()}";
    }
}
