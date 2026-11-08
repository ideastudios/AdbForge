using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace AdbToolkit.Services;

public class UpdateCheckResult
{
    public bool Success { get; init; }
    public bool HasNewVersion { get; init; }
    public string Message { get; init; } = "";
    public string? DownloadUrl { get; init; }
}

/// <summary>更新描述文件格式（托管在任意 HTTP 服务器上的 JSON）。</summary>
public class UpdateFeed
{
    public string Version { get; set; } = "";
    public string? Url { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// 更新检查客户端：从可配置的 URL 拉取版本描述 JSON（{"version":"1.0.0","url":"...","notes":"..."}），
/// 与当前程序集版本比较。服务器侧只需托管一个静态 JSON 文件即可。
/// </summary>
public static class UpdateService
{
    /// <summary>当前版本（读取 exe 的文件版本；单文件发布下 Assembly.Location 为空，需走 ProcessPath）。</summary>
    public static string CurrentVersion
    {
        get
        {
            try
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path))
                    path = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(path))
                {
                    var v = FileVersionInfo.GetVersionInfo(path).FileVersion;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { /* 回退默认 */ }
            return "0.0.0";
        }
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", $"AdbToolkit/{CurrentVersion}");
        return client;
    }

    public static async Task<UpdateCheckResult> CheckAsync(string feedUrl)
    {
        try
        {
            var json = await Http.GetStringAsync(feedUrl);
            var feed = JsonSerializer.Deserialize<UpdateFeed>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (feed == null || string.IsNullOrWhiteSpace(feed.Version))
                return new UpdateCheckResult { Success = false, Message = "更新描述格式错误（缺少 version 字段）" };

            if (!Version.TryParse(feed.Version, out var latest))
                return new UpdateCheckResult { Success = false, Message = $"更新版本号无效：{feed.Version}" };

            var current = Version.Parse(CurrentVersion);
            if (latest <= current)
                return new UpdateCheckResult { Success = true, Message = $"已是最新版本（v{CurrentVersion}）" };

            return new UpdateCheckResult
            {
                Success = true,
                HasNewVersion = true,
                DownloadUrl = feed.Url,
                Message = $"发现新版本 v{feed.Version}（当前 v{CurrentVersion}）" +
                          (string.IsNullOrEmpty(feed.Notes) ? "" : $"\n更新说明：{feed.Notes}") +
                          (string.IsNullOrEmpty(feed.Url) ? "" : $"\n下载地址：{feed.Url}"),
            };
        }
        catch (Exception ex)
        {
            LogService.Error("更新检查失败", ex);
            return new UpdateCheckResult { Success = false, Message = $"无法连接更新服务器：{ex.Message}" };
        }
    }

    /// <summary>用系统默认浏览器打开下载地址。</summary>
    public static void OpenDownloadUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Error("打开下载地址失败", ex);
        }
    }
}
