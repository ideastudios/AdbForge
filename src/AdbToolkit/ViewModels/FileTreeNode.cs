using System.Collections.ObjectModel;
using AdbToolkit.Models;
using AdbToolkit.Resources;

namespace AdbToolkit.ViewModels;

/// <summary>
/// 设备文件树节点（TreeView 条目）。目录懒加载：展开时才通过 adb ls 拉取子项。
/// 软链接按可展开处理（ls 时追加 "/"，指向文件的软链会在加载失败后自动变为叶子）。
/// </summary>
public class FileTreeNode : ObservableObject
{
    public string Name { get; init; } = "";

    public string FullPath { get; init; } = "";

    public bool IsDir { get; init; }

    public bool IsSymlink { get; init; }

    /// <summary>是否可展开（目录或软链）。</summary>
    public bool IsExpandable => IsDir || IsSymlink;

    public long Size { get; init; }

    public string Date { get; init; } = "";

    public string Perms { get; init; } = "";

    /// <summary>占位节点（加载中…），仅用于让展开箭头出现。</summary>
    public bool IsPlaceholder { get; init; }

    public FileTreeNode? Parent { get; set; }

    public ObservableCollection<FileTreeNode> Children { get; } = new();

    public bool ChildrenLoaded { get; set; }

    /// <summary>节点被展开时触发（UI 线程），由 ViewModel 负责加载子项。</summary>
    public event Action<FileTreeNode>? ExpandRequested;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            if (value && IsExpandable && !ChildrenLoaded) ExpandRequested?.Invoke(this);
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Glyph => IsExpandable ? "\uE8B7" : "\uE7C3"; // 文件夹 / 文件

    public string SizeText => IsPlaceholder || IsExpandable ? "" : DeviceFile.FormatSize(Size);

    public static FileTreeNode Placeholder() => new() { Name = Strings.Files_Loading, IsPlaceholder = true };
}
