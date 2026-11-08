using System.IO;
using System.Text.Json;

namespace AdbToolkit.Services;

/// <summary>单个应用的 manifest 解析结果缓存。</summary>
public class CachedAppMeta
{
    public string? Label { get; set; }
    public List<string> Activities { get; set; } = new();
    public List<string> Services { get; set; } = new();
    public List<string> Receivers { get; set; } = new();
    public List<string> Providers { get; set; } = new();
    public List<string> ManifestPermissions { get; set; } = new();
}

/// <summary>应用名与组件列表的磁盘缓存（%APPDATA%\AdbToolkit\appmeta.json）。</summary>
public static class AppMetaCache
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdbToolkit");

    private static string FilePath => Path.Combine(Dir, "appmeta.json");

    /// <summary>缓存文件路径（设置页统计显示用）。</summary>
    public static string CacheFilePath => FilePath;

    /// <summary>临时 APK 存放目录（解析应用名时暂存）。</summary>
    public static string TempApkDir => Path.Combine(Path.GetTempPath(), "AdbToolkit");

    public static Dictionary<string, CachedAppMeta> Map { get; } = Load();

    private static Dictionary<string, CachedAppMeta> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, CachedAppMeta>>(
                    File.ReadAllText(FilePath));
                if (map != null) return map;
            }
        }
        catch { /* 损坏时重建 */ }
        return new Dictionary<string, CachedAppMeta>();
    }

    public static CachedAppMeta? Get(string package) =>
        Map.TryGetValue(package, out var m) ? m : null;

    public static void Put(string package, CachedAppMeta meta) => Map[package] = meta;

    /// <summary>清空全部缓存并删除缓存文件（设置页"清除 PC 缓存"用）。</summary>
    public static void ClearAndReset()
    {
        Map.Clear();
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch { /* 删除失败下次覆盖 */ }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Map,
                new JsonSerializerOptions { WriteIndented = false }));
        }
        catch { /* 写入失败不影响功能 */ }
    }
}
