using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdbToolkit.Resources;

namespace AdbToolkit.Services;

public class UpdateCheckResult
{
    public bool Success { get; init; }
    public bool HasNewVersion { get; init; }
    public string Message { get; init; } = "";
    public string? DownloadUrl { get; init; }

    // ---------- GitHub Releases 专用 ----------
    /// <summary>Release 页面地址（无安装包或下载失败时引导用户打开）。</summary>
    public string? PageUrl { get; init; }
    /// <summary>安装包文件名（如 AdbForge-0.3.1-portable.zip）。</summary>
    public string? AssetName { get; init; }
    /// <summary>安装包字节数。</summary>
    public long AssetSize { get; init; }
    /// <summary>安装包下载地址。</summary>
    public string? AssetUrl { get; init; }
    /// <summary>Release 说明（已截断）。</summary>
    public string? Notes { get; init; }
}

/// <summary>更新描述文件格式（托管在任意 HTTP 服务器上的 JSON）。</summary>
public class UpdateFeed
{
    public string Version { get; set; } = "";
    public string? Url { get; set; }
    public string? Notes { get; set; }
}

/// <summary>GitHub Releases API 返回结构（只取用到的字段）。</summary>
internal class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }

    [JsonPropertyName("assets")]
    public GitHubAsset[]? Assets { get; set; }
}

internal class GitHubAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
}

/// <summary>
/// 更新检查客户端，两种来源：
/// 1. GitHub Releases（默认）：api.github.com/repos/{repo}/releases/latest，无需维护描述文件；
/// 2. 自定义 JSON 描述：{"version":"1.0.0","url":"...","notes":"..."}（设置页填写 URL 时启用）。
/// 与当前程序集版本比较，支持直接下载 Release 安装包。
/// </summary>
public static class UpdateService
{
    private const string GitHubRepo = "ideastudios/AdbForge";

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

    private static readonly HttpClient Http = CreateClient(TimeSpan.FromSeconds(10));
    private static readonly HttpClient DownloadHttp = CreateClient(TimeSpan.FromMinutes(5));

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", $"AdbToolkit/{CurrentVersion}");
        return client;
    }

    /// <summary>解析版本描述 JSON（自定义 URL 来源）。</summary>
    public static async Task<UpdateCheckResult> CheckAsync(string feedUrl)
    {
        try
        {
            var json = await Http.GetStringAsync(feedUrl);
            var feed = JsonSerializer.Deserialize<UpdateFeed>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (feed == null || string.IsNullOrWhiteSpace(feed.Version))
                return new UpdateCheckResult { Success = false, Message = Strings.Update_BadFormat };

            if (!Version.TryParse(feed.Version, out var latest))
                return new UpdateCheckResult { Success = false, Message = string.Format(Strings.Update_InvalidVersion, feed.Version) };

            var current = Version.Parse(CurrentVersion);
            if (latest <= current)
                return new UpdateCheckResult { Success = true, Message = string.Format(Strings.Update_UpToDate, CurrentVersion) };

            return new UpdateCheckResult
            {
                Success = true,
                HasNewVersion = true,
                DownloadUrl = feed.Url,
                Message = string.Format(Strings.Update_NewVersionFormat, feed.Version, CurrentVersion) +
                          (string.IsNullOrEmpty(feed.Notes) ? "" : "\n" + string.Format(Strings.Update_NotesPrefix, feed.Notes)) +
                          (string.IsNullOrEmpty(feed.Url) ? "" : "\n" + string.Format(Strings.Update_UrlPrefix, feed.Url)),
            };
        }
        catch (Exception ex)
        {
            LogService.Error("更新检查失败", ex);
            return new UpdateCheckResult { Success = false, Message = string.Format(Strings.Update_CannotConnect, ex.Message) };
        }
    }

    /// <summary>检查 GitHub Releases 最新版本（UpdateUrl 留空时的默认来源）。</summary>
    public static async Task<UpdateCheckResult> CheckGitHubAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{GitHubRepo}/releases/latest");
            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return new UpdateCheckResult { Success = false, Message = Strings.Update_GitHubNoRelease };
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = string.Format(Strings.Update_GitHubHttpErrorFormat, (int)resp.StatusCode),
                };
            }

            var json = await resp.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                return new UpdateCheckResult { Success = false, Message = Strings.Update_GitHubBadFormat };

            if (!Version.TryParse(release.TagName.TrimStart('v', 'V'), out var latest))
                return new UpdateCheckResult { Success = false, Message = string.Format(Strings.Update_InvalidVersion, release.TagName) };

            var current = Version.Parse(CurrentVersion);
            if (latest <= current)
                return new UpdateCheckResult { Success = true, Message = string.Format(Strings.Update_UpToDate, CurrentVersion) };

            // 安装包：优先 AdbForge*.zip，否则任一 zip
            var asset = release.Assets
                ?.FirstOrDefault(a => a.Name != null
                    && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && a.Name.StartsWith("AdbForge", StringComparison.OrdinalIgnoreCase))
                ?? release.Assets?.FirstOrDefault(a => a.Name != null
                    && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            var notes = Truncate(release.Body);
            var msg = string.Format(Strings.Update_NewVersionFormat, release.TagName, CurrentVersion) +
                      (string.IsNullOrEmpty(notes) ? "" : "\n" + string.Format(Strings.Update_NotesPrefix, notes)) +
                      (asset != null
                          ? "\n" + string.Format(Strings.Update_AssetFormat, asset.Name, asset.Size / 1024.0 / 1024.0)
                          : "");

            return new UpdateCheckResult
            {
                Success = true,
                HasNewVersion = true,
                PageUrl = release.HtmlUrl,
                AssetName = asset?.Name,
                AssetSize = asset?.Size ?? 0,
                AssetUrl = asset?.BrowserDownloadUrl,
                Notes = notes,
                Message = msg,
            };
        }
        catch (Exception ex)
        {
            LogService.Error("GitHub 更新检查失败", ex);
            return new UpdateCheckResult { Success = false, Message = string.Format(Strings.Update_CannotConnect, ex.Message) };
        }
    }

    /// <summary>下载 Release 安装包到 Downloads\AdbForge，progress 每 80KB 回调一次（字节数）。</summary>
    public static async Task<string> DownloadReleaseAsync(
        string url, string fileName, Action<long, long>? progress)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AdbForge");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, fileName);

        using var resp = await DownloadHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;

        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer)) > 0)
        {
            await fs.WriteAsync(buffer, 0, n);
            read += n;
            if (total > 0) progress?.Invoke(read, total);
        }
        return dest;
    }

    private static string? Truncate(string? text, int max = 400)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }

    /// <summary>用系统默认浏览器打开下载地址 / Release 页面。</summary>
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
