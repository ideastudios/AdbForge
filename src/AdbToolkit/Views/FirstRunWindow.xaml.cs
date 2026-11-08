using System.IO;
using System.Windows;
using AdbToolkit.Services;

namespace AdbToolkit.Views;

/// <summary>首次运行引导：欢迎、adb 环境检测、隐私与使用提示。</summary>
public partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();

        var adbPath = AppSession.Adb.AdbPath;
        if (File.Exists(adbPath))
        {
            AdbStatusText.Text = $"✓ 已检测到 adb：{adbPath}";
            AdbStatusText.Foreground = FindBrush("Status.OK");
        }
        else
        {
            AdbStatusText.Text = $"✗ 未检测到 adb（当前尝试：{adbPath}）\n" +
                                 "请先安装 Android SDK platform-tools，或到「设置」页手动指定 adb.exe 路径。";
            AdbStatusText.Foreground = FindBrush("Status.Error");
        }
    }

    private static System.Windows.Media.Brush FindBrush(string key) =>
        Application.Current.TryFindResource(key) as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Gray;

    private void Start_Click(object sender, RoutedEventArgs e) => Close();
}
