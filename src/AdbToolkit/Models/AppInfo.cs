using System.ComponentModel;

namespace AdbToolkit.Models;

/// <summary>设备上的一个应用（来自 pm list packages）。</summary>
public class AppInfo : INotifyPropertyChanged
{
    public string Package { get; init; } = "";

    public string ApkPath { get; init; } = "";

    public bool IsSystem { get; init; }

    /// <summary>应用显示名（从 APK manifest 解析，后台懒加载填充）。</summary>
    private string _label = "…";
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
