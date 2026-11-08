using System.Windows;

namespace AdbToolkit.Views;

/// <summary>应用详情窗口（只读文本展示）。</summary>
public partial class AppDetailWindow : Window
{
    public string DetailTitle { get; }
    public string Text { get; }

    public AppDetailWindow(string title, string text)
    {
        DetailTitle = title;
        Text = text;
        InitializeComponent();
        DataContext = this;
    }
}
