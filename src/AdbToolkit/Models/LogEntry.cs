namespace AdbToolkit.Models;

/// <summary>一行 logcat 日志（-v threadtime 格式解析结果）。</summary>
public class LogEntry
{
    public string Time { get; set; } = "";
    public int Pid { get; set; }
    public int Tid { get; set; }
    public char Level { get; set; } = 'I';
    public string Tag { get; set; } = "";
    public string Message { get; set; } = "";

    /// <summary>无法解析的原始行。</summary>
    public string Raw { get; set; } = "";

    /// <summary>级别字符，用于 XAML DataTrigger 着色。</summary>
    public string LevelText => Level.ToString();

    public string Display => string.IsNullOrEmpty(Tag) ? Message : $"{Tag}: {Message}";
}
