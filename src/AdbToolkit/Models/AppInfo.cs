using System.ComponentModel;

namespace AdbToolkit.Models;

/// <summary>设备上的一个应用（来自 pm list packages）。</summary>
public class AppInfo : INotifyPropertyChanged
{
    public string Package { get; init; } = "";

    public string ApkPath { get; init; } = "";

    public bool IsSystem { get; init; }

    /// <summary>应用显示名（从 APK manifest 解析，右键获取应用名/详情后填充；未解析时留空显示包名由 UI 处理）。</summary>
    private string _label = "";
    public string Label
    {
        get => _label;
        set
        {
            if (_label == value) return;
            _label = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
