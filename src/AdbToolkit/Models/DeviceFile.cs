namespace AdbToolkit.Models;

/// <summary>设备端文件（ls -la 解析结果）。</summary>
public class DeviceFile
{
    public string Name { get; set; } = "";
    public bool IsDir { get; set; }
    public bool IsSymlink { get; set; }
    public long Size { get; set; }
    public string Date { get; set; } = "";
    public string Perms { get; set; } = "";

    public string DisplayName => IsDir ? $"[目录] {Name}" : Name;

    public string SizeText => IsDir ? "-" : FormatSize(Size);

    /// <summary>Android Studio Device Explorer 风格的大小文本：0 B / 17 B / 3.4 KB / 120 KB。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var kb = bytes / 1024.0;
        if (kb < 10) return $"{kb:F1} KB";
        if (kb < 1024) return $"{kb:F0} KB";
        var mb = kb / 1024.0;
        if (mb < 10) return $"{mb:F1} MB";
        if (mb < 1024) return $"{mb:F0} MB";
        return $"{mb / 1024.0:F1} GB";
    }
}

/// <summary>DropBox 崩溃/ANR 历史条目。</summary>
public class DropBoxEntry
{
    public string Time { get; set; } = "";
    public string Tag { get; set; } = "";

    public bool IsAnr => Tag.Contains("anr", StringComparison.OrdinalIgnoreCase);
    public bool IsCrash => Tag.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
                           Tag.Contains("wtf", StringComparison.OrdinalIgnoreCase);
}
