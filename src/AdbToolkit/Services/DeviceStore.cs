using System.Collections.ObjectModel;
using AdbToolkit.Models;

namespace AdbToolkit.Services;

/// <summary>
/// 全局设备上下文（灵魂设计）：顶部设备栏选择后，所有页面共用当前设备。
/// </summary>
public static class DeviceStore
{
    public static ObservableCollection<AdbDevice> Devices { get; } = new();

    public static AdbDevice? Selected { get; set; }

    public static string? SelectedSerial => Selected?.Serial;
}
