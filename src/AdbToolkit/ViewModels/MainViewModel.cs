using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AdbToolkit.Models;
using AdbToolkit.Resources;
using AdbToolkit.Services;
using AdbToolkit.Views;

namespace AdbToolkit.ViewModels;

/// <summary>侧边栏导航项。</summary>
public class NavItem
{
    public string Title { get; init; } = "";

    /// <summary>Segoe MDL2 Assets 图标字符。</summary>
    public string Glyph { get; init; } = "";

    public Type PageType { get; init; } = typeof(SettingsView);
}

/// <summary>主窗口：导航、全局设备轮询、状态栏。</summary>
public class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _pollTimer;
    private bool _refreshing;

    public MainViewModel()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1000, AppSession.Settings.DevicePollMs)),
        };
        _pollTimer.Tick += async (_, _) =>
        {
            await RefreshDevicesAsync();
            await RefreshForegroundAsync();
            await RefreshVisibleWindowsAsync();
        };
        _pollTimer.Start();

        RefreshDevicesCommand = new RelayCommand(async () => await RefreshDevicesAsync());
        CopyForegroundCommand = new RelayCommand(CopyForeground);
        OpenAppDetailsCommand = new RelayCommand(async () => await OpenAppDetailsAsync());
        CopyTextCommand = new RelayCommand(p =>
        {
            if (p is not string s || s.Length == 0) return;
            try
            {
                Clipboard.SetText(s);
                StatusText = string.Format(Strings.Common_CopiedFormat, s);
            }
            catch { /* 剪贴板被占用等极端情况 */ }
        });

        // 启动即刷一次，避免首屏空白 2 秒
        _ = RefreshDevicesAsync();
        SelectedNav = NavItems[0];
    }

    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new NavItem { Title = Strings.Nav_Devices,     Glyph = "\uE772", PageType = typeof(DeviceManagerView) },
        new NavItem { Title = Strings.Nav_Logcat,      Glyph = "\uE7C3", PageType = typeof(LogcatView) },
        new NavItem { Title = Strings.Nav_Apps,        Glyph = "\uE71D", PageType = typeof(AppsView) },
        new NavItem { Title = Strings.Nav_Files,       Glyph = "\uE8B7", PageType = typeof(FilesView) },
        new NavItem { Title = Strings.Nav_ScreenInput, Glyph = "\uE7F4", PageType = typeof(ScreenInputView) },
        new NavItem { Title = Strings.Nav_Perf,        Glyph = "\uE9D2", PageType = typeof(PerfView) },
        new NavItem { Title = Strings.Nav_SysDebug,    Glyph = "\uE756", PageType = typeof(SysDebugView) },
        new NavItem { Title = Strings.Nav_More,        Glyph = "\uE710", PageType = typeof(MoreToolsView) },
        new NavItem { Title = Strings.Nav_Settings,    Glyph = "\uE713", PageType = typeof(SettingsView) },
    };

    private NavItem? _selectedNav;
    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (!SetProperty(ref _selectedNav, value)) return;
            OnPropertyChanged(nameof(CurrentTitle));
            CurrentView = CreateView(value);
        }
    }

    private object? _currentView;
    public object? CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    public string CurrentTitle => SelectedNav?.Title ?? "";

    /// <summary>全局设备列表（与 DeviceStore 共享实例）。</summary>
    public ObservableCollection<AdbDevice> Devices => DeviceStore.Devices;

    private AdbDevice? _selectedDevice;
    public AdbDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value)) return;
            DeviceStore.Selected = value;
        }
    }

    private Brush _adbStatusBrush = Brushes.Gray;
    public Brush AdbStatusBrush
    {
        get => _adbStatusBrush;
        private set => SetProperty(ref _adbStatusBrush, value);
    }

    private string _statusText = Strings.Main_DetectingAdb;
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public RelayCommand RefreshDevicesCommand { get; private set; }

    // ---------- 前台应用 ----------

    private ForegroundInfo? _foreground;
    public ForegroundInfo? Foreground
    {
        get => _foreground;
        private set
        {
            if (!SetProperty(ref _foreground, value)) return;
            OnPropertyChanged(nameof(ForegroundText));
            OnPropertyChanged(nameof(ForegroundPackage));
            OnPropertyChanged(nameof(ForegroundComponent));
            OnPropertyChanged(nameof(ForegroundSource));
        }
    }

    public string ForegroundText => Foreground?.Component ?? "—";
    public string ForegroundPackage => Foreground?.Package ?? "—";
    public string ForegroundComponent => Foreground?.Component ?? "—";
    public string ForegroundSource => Foreground?.Source ?? "—";

    private bool _fgRefreshing;

    /// <summary>随设备轮询一起刷新前台应用（失败静默，不打扰用户）。</summary>
    private async Task RefreshForegroundAsync()
    {
        if (_fgRefreshing) return;
        var serial = DeviceStore.Selected?.State == AdbDeviceState.Device ? DeviceStore.SelectedSerial : null;
        if (serial is null)
        {
            Foreground = null;
            return;
        }

        _fgRefreshing = true;
        try
        {
            Foreground = await AppSession.Adb.GetForegroundAsync(serial);
        }
        catch
        {
            // adb 不可用等场景静默处理
        }
        finally
        {
            _fgRefreshing = false;
        }
    }

    /// <summary>点击顶栏前台指示器：复制完整组件名。</summary>
    public RelayCommand CopyForegroundCommand { get; private set; }

    private void CopyForeground()
    {
        var text = Foreground?.Component;
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            StatusText = string.Format(Strings.Main_CopiedForegroundFormat, text);
        }
        catch { /* 剪贴板被占用等极端情况 */ }
    }

    /// <summary>跳到系统的应用详情页（权限/存储/卸载入口）。</summary>
    public RelayCommand OpenAppDetailsCommand { get; private set; }

    private async Task OpenAppDetailsAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        var pkg = Foreground?.Package;
        if (serial is null || string.IsNullOrEmpty(pkg) || Foreground == null)
        {
            StatusText = Strings.Main_NoForegroundInfo;
            return;
        }
        var r = await AppSession.Adb.RunShellAsync(
            $"am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d \"package:{pkg}\"", serial);
        StatusText = r.Success
            ? string.Format(Strings.Main_OpenedAppDetailsFormat, pkg)
            : string.Format(Strings.Common_OpenFailedFormat, r.Combined.Trim());
    }

    /// <summary>通用复制命令：CommandParameter 传要复制的文本。</summary>
    public RelayCommand CopyTextCommand { get; private set; }

    // ---------- 可见窗口列表 ----------

    /// <summary>当前所有可见窗口（含系统窗口），随轮询刷新，最上层在前。</summary>
    public ObservableCollection<ForegroundInfo> VisibleWindows { get; } = new();

    private bool _winRefreshing;

    private async Task RefreshVisibleWindowsAsync()
    {
        if (_winRefreshing) return;
        var serial = DeviceStore.Selected?.State == AdbDeviceState.Device ? DeviceStore.SelectedSerial : null;
        if (serial is null)
        {
            if (VisibleWindows.Count > 0) VisibleWindows.Clear();
            return;
        }

        _winRefreshing = true;
        try
        {
            var windows = await AppSession.Adb.GetVisibleWindowsAsync(serial);
            foreach (var w in windows)
                w.IsForeground = Foreground != null && w.Component == Foreground.Component;

            VisibleWindows.Clear();
            foreach (var w in windows) VisibleWindows.Add(w);
        }
        catch
        {
            // 静默
        }
        finally
        {
            _winRefreshing = false;
        }
    }

    public async Task RefreshDevicesAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var devices = await AppSession.Adb.ListDevicesAsync();

            // 无变化时不动 UI，避免选择被重置
            if (!DevicesChanged(devices)) return;

            var oldSerial = SelectedDevice?.Serial;
            Devices.Clear();
            foreach (var d in devices) Devices.Add(d);

            SelectedDevice = devices.FirstOrDefault(d => d.Serial == oldSerial)
                             ?? devices.FirstOrDefault(d => d.State == AdbDeviceState.Device);
        }
        catch (Exception ex)
        {
            AdbStatusBrush = Brushes.Firebrick;
            StatusText = ex.Message;
            return;
        }
        finally
        {
            _refreshing = false;
        }

        UpdateStatus();
    }

    private bool DevicesChanged(List<AdbDevice> devices)
    {
        if (Devices.Count != devices.Count) return true;
        for (var i = 0; i < devices.Count; i++)
        {
            if (Devices[i].Serial != devices[i].Serial || Devices[i].State != devices[i].State)
                return true;
        }
        return false;
    }

    private void UpdateStatus()
    {
        if (Devices.Count == 0)
        {
            AdbStatusBrush = FindBrush("Status.Error");
            StatusText = string.Format(Strings.Main_StatusNoDeviceFormat, AppSession.Adb.AdbPath);
            return;
        }

        if (Devices.Any(d => d.State == AdbDeviceState.Device))
        {
            AdbStatusBrush = FindBrush("Status.OK");
            StatusText = string.Format(Strings.Main_StatusConnectedFormat,
                Devices.Count(d => d.State == AdbDeviceState.Device), AppSession.Adb.AdbPath);
            return;
        }

        AdbStatusBrush = FindBrush("Status.Warn");
        StatusText = string.Format(Strings.Main_StatusUnavailableFormat, Devices.Count, AppSession.Adb.AdbPath);
    }

    private static Brush FindBrush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

    private static object CreateView(NavItem? item)
    {
        UserControl page;
        switch (item?.PageType.Name)
        {
            case nameof(DeviceManagerView):
                page = new DeviceManagerView { DataContext = new DeviceManagerViewModel() };
                break;
            case nameof(LogcatView):
                page = new LogcatView { DataContext = new LogcatViewModel() };
                break;
            case nameof(AppsView):
                page = new AppsView { DataContext = new AppsViewModel() };
                break;
            case nameof(FilesView):
                page = new FilesView { DataContext = new FilesViewModel() };
                break;
            case nameof(ScreenInputView):
                page = new ScreenInputView { DataContext = new ScreenInputViewModel() };
                break;
            case nameof(PerfView):
                page = new PerfView { DataContext = new PerfViewModel() };
                break;
            case nameof(SysDebugView):
                page = new SysDebugView { DataContext = new SysDebugViewModel() };
                break;
            case nameof(MoreToolsView):
                page = new MoreToolsView { DataContext = new MoreToolsViewModel() };
                break;
            case nameof(SettingsView):
                page = new SettingsView { DataContext = new SettingsViewModel() };
                break;
            default:
                page = new SettingsView { DataContext = new SettingsViewModel() };
                break;
        }
        return page;
    }
}
