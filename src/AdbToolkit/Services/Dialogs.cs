using System.IO;
using AdbToolkit.Resources;

namespace AdbToolkit.Services;

/// <summary>通用对话框辅助（WPF 无原生文件夹选择框，用 OpenFileDialog 变体实现）。</summary>
public static class Dialogs
{
    /// <summary>选择文件夹：导航进入目标文件夹后点“打开”。</summary>
    public static string? PickFolder(string title, string? initialDir = null)
    {
        var marker = Strings.Files_FolderPickerMarker;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = marker,
        };
        if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
            dlg.InitialDirectory = initialDir;

        if (dlg.ShowDialog() != true) return null;

        var path = dlg.FileName;
        if (path.EndsWith(marker, StringComparison.Ordinal))
            path = path[..^marker.Length].TrimEnd('\\');
        return Directory.Exists(path) ? path : null;
    }
}
