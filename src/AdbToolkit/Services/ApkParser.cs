using System.IO;
using System.IO.Compression;
using System.Text;

namespace AdbToolkit.Services;

/// <summary>从 APK 中解析出的 manifest 信息。组件名带 " [exported]" 后缀表示对外暴露。</summary>
public record ApkManifestInfo(
    string? Label,
    List<string> Activities,
    List<string> Services,
    List<string> Receivers,
    List<string> Providers,
    List<string> Permissions);

/// <summary>
/// 纯托管实现的最小 AXML（二进制 AndroidManifest.xml）+ ARSC（resources.arsc）解析器。
/// 用于免外部工具获取应用显示名（application-label）与页面/服务/广播/权限列表。
/// 解析失败返回 null，调用方回退显示包名。
/// </summary>
public static class ApkParser
{
    public static ApkManifestInfo? ParseFile(string apkPath)
    {
        try
        {
            using var zip = new ZipArchive(File.OpenRead(apkPath), ZipArchiveMode.Read);
            var manifest = zip.GetEntry("AndroidManifest.xml");
            if (manifest == null) return null;

            var axml = ReadEntry(manifest);
            string[]? strings = null;
            string? label = null;
            uint? labelRef = null;
            var activities = new List<string>();
            var services = new List<string>();
            var receivers = new List<string>();
            var providers = new List<string>();
            var permissions = new List<string>();

            // RES_XML 根 headerSize = 8，子 chunk 从 8 开始
            int pos = 8;
            while (pos + 8 <= axml.Length)
            {
                var type = LE16(axml, pos);
                var size = LE32(axml, pos + 4);
                if (size < 8 || pos + size > axml.Length) break;

                switch (type)
                {
                    case 0x0001: // STRING POOL
                        strings = ParsePool(axml, pos);
                        break;
                    case 0x0102: // START ELEMENT
                        ParseStartElement(axml, pos, strings, ref label, ref labelRef,
                            activities, services, receivers, providers, permissions);
                        break;
                }
                pos += (int)((size + 3) & ~3);
            }

            if (strings == null) return null;

            // label 为资源引用时，从 resources.arsc 解析默认值
            if (label == null && labelRef != null)
            {
                var arsc = zip.GetEntry("resources.arsc");
                if (arsc != null)
                    label = GetStringFromArsc(ReadEntry(arsc), labelRef.Value);
            }

            return new ApkManifestInfo(label, activities, services, receivers, providers, permissions);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var ms = new MemoryStream();
        entry.Open().CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 遍历 START_ELEMENT：application 的 label；activity/service/receiver/provider 的
    /// name + exported（导出组件标记 " [exported]"）；uses-permission 的 name。
    /// </summary>
    private static void ParseStartElement(byte[] d, int pos, string?[]? strings,
        ref string? label, ref uint? labelRef,
        List<string> activities, List<string> services, List<string> receivers,
        List<string> providers, List<string> permissions)
    {
        if (strings == null) return;

        // header(8) + lineNumber(4) + comment(4) 后是 attrExt
        int ext = pos + 16;
        var nameIdx = (int)LE32(d, ext + 4);
        var elemName = nameIdx >= 0 && nameIdx < strings.Length ? strings[nameIdx] : "";
        var attributeStart = LE16(d, ext + 8);
        var attributeSize = LE16(d, ext + 10);
        var attributeCount = LE16(d, ext + 12);
        if (attributeSize < 20 || attributeCount == 0) return;
        int attrBase = ext + attributeStart;

        // 组件元素：两趟收集 name 与 exported（属性顺序不定）
        string? compName = null;
        bool exported = false;

        for (int i = 0; i < attributeCount; i++)
        {
            int a = attrBase + i * attributeSize;
            var attrNameIdx = (int)LE32(d, a + 4);
            if (attrNameIdx < 0 || attrNameIdx >= strings.Length) continue;
            var attrName = strings[attrNameIdx];
            byte dataType = d[a + 15];
            uint dataVal = LE32(d, a + 16);

            if (elemName == "application")
            {
                if (attrName == "label")
                {
                    if (dataType == 3 && dataVal < (uint)strings.Length) label = strings[dataVal];
                    else if (dataType == 1) labelRef = dataVal;
                }
                continue;
            }

            if (attrName == "name")
            {
                if (dataType == 3 && dataVal < (uint)strings.Length) compName = strings[dataVal];
            }
            else if (attrName == "exported" && dataType == 0x12) // INT_BOOLEAN
            {
                exported = dataVal != 0;
            }
        }

        if (compName == null) return;
        var marked = exported ? $"{compName} [exported]" : compName;
        switch (elemName)
        {
            case "activity" or "activity-alias": activities.Add(marked); break;
            case "service": services.Add(marked); break;
            case "receiver": receivers.Add(marked); break;
            case "provider": providers.Add(marked); break;
            case "uses-permission": permissions.Add(compName); break;
        }
    }

    /// <summary>解析 RES_STRING_POOL_TYPE（自动识别 UTF-8 / UTF-16 编码）。</summary>
    private static string[] ParsePool(byte[] d, int pos)
    {
        var stringCount = (int)LE32(d, pos + 8);
        var flags = LE32(d, pos + 16);
        var stringsStart = (int)LE32(d, pos + 20);
        bool utf8 = (flags & 0x00000100) != 0;

        var result = new string[stringCount];
        int basePos = pos + stringsStart;
        for (int i = 0; i < stringCount; i++)
        {
            var off = (int)LE32(d, pos + 28 + i * 4);
            int p = basePos + off;
            if (p >= d.Length) continue;

            if (utf8)
            {
                SkipVarint(d, ref p);           // 字符数
                int byteLen = ReadVarint(d, ref p); // 字节数
                if (p + byteLen <= d.Length)
                    result[i] = Encoding.UTF8.GetString(d, p, byteLen);
            }
            else
            {
                int len = LE16(d, p);
                p += 2;
                if ((len & 0x8000) != 0)
                {
                    len = ((len & 0x7FFF) << 16) | LE16(d, p);
                    p += 2;
                }
                if (p + len * 2 <= d.Length)
                    result[i] = Encoding.Unicode.GetString(d, p, len * 2);
            }
        }
        return result;
    }

    private static int ReadVarint(byte[] d, ref int p)
    {
        int b = d[p];
        if ((b & 0x80) == 0) { p++; return b; }
        var v = ((b & 0x7F) << 8) | d[p + 1];
        p += 2;
        return v;
    }

    private static void SkipVarint(byte[] d, ref int p)
    {
        if ((d[p] & 0x80) == 0) p++; else p += 2;
    }

    /// <summary>从 resources.arsc 中取资源 ID 对应的默认配置字符串（label 引用解析用）。</summary>
    private static string? GetStringFromArsc(byte[] d, uint resId)
    {
        try
        {
            if (d.Length < 12 || LE16(d, 0) != 0x0002) return null;
            var globalStrings = ParsePool(d, LE16(d, 2)); // headerSize 即全局字符串池偏移
            var pkgId = resId >> 24;
            var typeIdWanted = (resId >> 16) & 0xFF;
            var entryId = resId & 0xFFFF;

            string? any = null, defaultVal = null;
            uint? refVal = null;

            int pos = LE16(d, 2);
            while (pos + 8 <= d.Length)
            {
                var type = LE16(d, pos);
                var hdrSize = LE16(d, pos + 2);
                var size = LE32(d, pos + 4);
                if (size < 8 || pos + size > d.Length) break;

                if (type == 0x0200) // RES_TABLE_PACKAGE_TYPE
                {
                    var pPkgId = LE32(d, pos + 8);
                    int inner = pos + hdrSize;
                    int end = pos + (int)size;
                    while (inner + 8 <= end)
                    {
                        var t2 = LE16(d, inner);
                        var h2 = LE16(d, inner + 2);
                        var s2 = LE32(d, inner + 4);
                        if (s2 < 8 || inner + s2 > end) break;

                        if (t2 == 0x0201 && pPkgId == pkgId) // RES_TABLE_TYPE
                        {
                            var tid = d[inner + 8];
                            var entryCount = LE32(d, inner + 12);
                            var entriesStart = LE32(d, inner + 16);
                            if (tid == typeIdWanted && entryId < entryCount)
                            {
                                var off = LE32(d, inner + h2 + (int)entryId * 4);
                                if (off != 0xFFFFFFFF)
                                {
                                    int ep = inner + (int)entriesStart + (int)off;
                                    var eFlags = LE16(d, ep + 2);
                                    if ((eFlags & 0x0001) == 0) // 非复杂条目
                                    {
                                        var eSize = LE16(d, ep);
                                        var dataType = d[ep + eSize + 3];
                                        var val = LE32(d, ep + eSize + 4);
                                        var configSize = LE32(d, inner + 20);
                                        if (dataType == 3 && val < (uint)globalStrings.Length)
                                        {
                                            var v = globalStrings[val];
                                            if (configSize <= 28) defaultVal ??= v; // 默认配置优先
                                            any ??= v;
                                        }
                                        else if (dataType == 1)
                                        {
                                            refVal ??= val; // label → @string 别名引用
                                        }
                                    }
                                }
                            }
                        }
                        inner += (int)((s2 + 3) & ~3);
                    }
                }
                pos += (int)((size + 3) & ~3);
            }

            var result = defaultVal ?? any;
            if (result == null && refVal != null && refVal != resId)
                result = GetStringFromArsc(d, refVal.Value); // 引用跟随
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static ushort LE16(byte[] d, int pos) => (ushort)(d[pos] | (d[pos + 1] << 8));

    private static uint LE32(byte[] d, int pos) =>
        (uint)(d[pos] | (d[pos + 1] << 8) | (d[pos + 2] << 16) | (d[pos + 3] << 24));
}
