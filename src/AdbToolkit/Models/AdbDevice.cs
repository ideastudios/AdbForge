using AdbToolkit.Resources;

namespace AdbToolkit.Models;

public enum AdbDeviceState
{
    Device,
    Offline,
    Unauthorized,
    Recovery,
    Bootloader,
    Sideload,
    Unknown,
}

/// <summary>一台通过 adb 连接的设备。</summary>
public class AdbDevice
{
    public string Serial { get; set; } = "";
    public AdbDeviceState State { get; set; } = AdbDeviceState.Unknown;
    public string Model { get; set; } = "";
    public string Product { get; set; } = "";
    public string TransportId { get; set; } = "";

    /// <summary>是否为 WiFi 网络连接（形如 192.168.x.x:5555）。</summary>
    public bool IsWireless => Serial.Contains(':');

    public string DisplayName => string.IsNullOrEmpty(Model) ? Serial : $"{Model} ({Serial})";

    public string StateText => State switch
    {
        AdbDeviceState.Device => Strings.DevMgr_StateConnected,
        AdbDeviceState.Offline => Strings.DevMgr_StateOffline,
        AdbDeviceState.Unauthorized => Strings.DevMgr_StateUnauthorized,
        AdbDeviceState.Recovery => "Recovery",
        AdbDeviceState.Bootloader => "Bootloader",
        AdbDeviceState.Sideload => "Sideload",
        _ => Strings.DevMgr_StateUnknown,
    };
}
