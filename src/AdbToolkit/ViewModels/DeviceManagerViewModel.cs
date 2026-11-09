using System.Collections.ObjectModel;
using AdbToolkit.Models;
using AdbToolkit.Resources;
using AdbToolkit.Services;

namespace AdbToolkit.ViewModels;

/// <summary>设备管理页：设备列表、无线连接/配对、重启模式。</summary>
public class DeviceManagerViewModel : ObservableObject
{
    public ObservableCollection<AdbDevice> Devices => DeviceStore.Devices;

    private AdbDevice? _selectedDevice;
    public AdbDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
                DeviceStore.Selected = value;
        }
    }

    /// <summary>无线连接目标，形如 192.168.11.148:5555。</summary>
    private string _connectTarget = "";
    public string ConnectTarget
    {
        get => _connectTarget;
        set => SetProperty(ref _connectTarget, value);
    }

    /// <summary>Android 11+ 无线配对码（可选）。</summary>
    private string _pairCode = "";
    public string PairCode
    {
        get => _pairCode;
        set => SetProperty(ref _pairCode, value);
    }

    private string _resultText = "";
    public string ResultText
    {
        get => _resultText;
        set => SetProperty(ref _resultText, value);
    }

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand RebootCommand { get; }
    public RelayCommand RecoveryCommand { get; }
    public RelayCommand BootloaderCommand { get; }

    public DeviceManagerViewModel()
    {
        ConnectCommand = new RelayCommand(async () => await RunBusyAsync(ConnectAsync));
        DisconnectCommand = new RelayCommand(async () => await RunBusyAsync(DisconnectAsync));
        RebootCommand = new RelayCommand(async () => await RunBusyAsync(() => RebootAsync("")));
        RecoveryCommand = new RelayCommand(async () => await RunBusyAsync(() => RebootAsync("recovery")));
        BootloaderCommand = new RelayCommand(async () => await RunBusyAsync(() => RebootAsync("bootloader")));
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
            ResultText = string.Format(Strings.Common_ErrorFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectAsync()
    {
        var target = ConnectTarget.Trim();
        if (target.Length == 0)
        {
            ResultText = Strings.DevMgr_EnterAddr;
            return;
        }

        ResultText = "";
        if (!string.IsNullOrWhiteSpace(PairCode))
        {
            // 注意：Android 11+ 无线调试的配对端口与连接端口不同，
            // 需在手机「开发者选项 → 无线调试 → 使用配对码配对设备」中查看实际端口后填入目标框
            var pair = await AppSession.Adb.RunAsync($"pair {target} {PairCode.Trim()}");
            ResultText += string.Format(Strings.DevMgr_PairResultFormat, pair.Combined.Trim())
                          + Environment.NewLine;
        }

        var r = await AppSession.Adb.RunAsync($"connect {target}");
        ResultText += string.Format(Strings.DevMgr_ConnectResultFormat, r.Combined.Trim());
    }

    private async Task DisconnectAsync()
    {
        var target = ConnectTarget.Trim();
        var r = target.Length == 0
            ? await AppSession.Adb.RunAsync("disconnect")
            : await AppSession.Adb.RunAsync($"disconnect {target}");
        ResultText = string.Format(Strings.DevMgr_DisconnectResultFormat, r.Combined.Trim());
    }

    private async Task RebootAsync(string mode)
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null)
        {
            ResultText = Strings.Common_SelectDeviceFirst;
            return;
        }

        var label = mode.Length == 0 ? Strings.DevMgr_RebootModeSystem : mode;
        var r = await AppSession.Adb.RunAsync($"reboot {mode}".Trim(), serial);
        ResultText = r.Success
            ? string.Format(Strings.DevMgr_RebootSentFormat, label)
            : r.Combined.Trim();
    }
}
