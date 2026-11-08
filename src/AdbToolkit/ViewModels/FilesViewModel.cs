using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AdbToolkit.Models;
using AdbToolkit.Services;
using AdbToolkit.Views;

namespace AdbToolkit.ViewModels;

/// <summary>
/// 文件传输页：树形目录浏览（参考 Android Studio Device Explorer）。
/// 根路径默认 /，顶层直接铺开全部目录（含系统分区）；软链可展开；目录懒加载。
/// </summary>
public class FilesViewModel : ObservableObject
{
    /// <summary>树顶层节点（即根路径下的直接子项）。</summary>
    public ObservableCollection<FileTreeNode> RootNodes { get; } = new();

    private string _rootPath = "/";
    public string RootPath
    {
        get => _rootPath;
        set => SetProperty(ref _rootPath, value);
    }

    private FileTreeNode? _selectedNode;
    public FileTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value) && value is { IsPlaceholder: false })
                Status = $"已选中：{value.FullPath}";
        }
    }

    private string _newDirName = "";
    public string NewDirName
    {
        get => _newDirName;
        set => SetProperty(ref _newDirName, value);
    }

    private string _localPath = "";
    public string LocalPath
    {
        get => _localPath;
        set => SetProperty(ref _localPath, value);
    }

    private string _localDir = "";
    public string LocalDir
    {
        get => _localDir;
        set => SetProperty(ref _localDir, value);
    }

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    private string _status = "展开目录将自动加载子项";
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public RelayCommand SetRootCommand { get; }
    public RelayCommand RefreshNodeCommand { get; }
    public RelayCommand CreateDirCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand PushCommand { get; }
    public RelayCommand BrowseLocalCommand { get; }
    public RelayCommand PullCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    private bool _loadingChildren;

    public FilesViewModel()
    {
        LocalDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AdbToolkit");

        SetRootCommand = new RelayCommand(async () => await RunBusyAsync(LoadRootAsync));
        RefreshNodeCommand = new RelayCommand(async () => await RunBusyAsync(RefreshSelectedAsync));
        CreateDirCommand = new RelayCommand(async () => await RunBusyAsync(CreateDirAsync));
        DeleteCommand = new RelayCommand(async () => await RunBusyAsync(DeleteSelectedAsync));
        PushCommand = new RelayCommand(async () => await RunBusyAsync(PushAsync));
        BrowseLocalCommand = new RelayCommand(BrowseLocal);
        PullCommand = new RelayCommand(async () => await RunBusyAsync(PullAsync));
        OpenFolderCommand = new RelayCommand(OpenLocalFolder);
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Status = "错误：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>页面首次加载时列出根目录的子项（不自动展开）。</summary>
    public async Task LoadOnNavigateAsync()
    {
        if (RootNodes.Count == 0)
            await RunBusyAsync(LoadRootAsync);
    }

    // ---------- 树构建 ----------

    /// <summary>ls 时给路径补尾斜杠：目录/软链正常列内容，软链指向文件时报错→该节点变叶子。</summary>
    private static string LsPath(string path) => path.EndsWith('/') ? path : path + "/";

    private async Task LoadRootAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null)
        {
            Status = "请先在顶部选择设备";
            return;
        }

        var root = RootPath.Trim();
        if (!root.StartsWith('/'))
        {
            Status = "路径需以 / 开头，例如 /";
            return;
        }

        RootNodes.Clear();
        SelectedNode = null;
        Status = $"正在加载 {root} ...";

        try
        {
            var files = await AppSession.Adb.ListDirectoryAsync(serial, LsPath(root));
            foreach (var f in files)
                RootNodes.Add(CreateNode(null, f, root));
            Status = $"{root} — {files.Count} 项";
        }
        catch (Exception ex)
        {
            Status = $"读取 {root} 失败：{ex.Message}";
        }
    }

    private async void OnExpandRequested(FileTreeNode node)
    {
        if (node.ChildrenLoaded || _loadingChildren) return;
        _loadingChildren = true;
        try
        {
            await LoadChildrenAsync(node);
        }
        catch (Exception ex)
        {
            Status = "错误：" + ex.Message;
        }
        finally
        {
            _loadingChildren = false;
        }
    }

    private async Task LoadChildrenAsync(FileTreeNode node)
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) return;

        Status = $"正在读取 {node.FullPath} ...";
        try
        {
            var files = await AppSession.Adb.ListDirectoryAsync(serial, LsPath(node.FullPath));
            node.Children.Clear();
            foreach (var f in files)
                node.Children.Add(CreateNode(node, f));
            node.ChildrenLoaded = true;
            Status = $"{node.FullPath} — {files.Count} 项";
        }
        catch
        {
            // 软链指向文件、或无权限：清掉占位节点，节点变叶子
            node.Children.Clear();
            node.ChildrenLoaded = true;
            Status = $"{node.FullPath} 不可展开（可能指向文件或需要 root）";
        }
    }

    private FileTreeNode CreateNode(FileTreeNode? parent, DeviceFile f, string? parentPathOverride = null)
    {
        var parentPath = parent?.FullPath ?? parentPathOverride ?? RootPath.Trim();
        var node = new FileTreeNode
        {
            Name = f.Name,
            FullPath = JoinPath(parentPath, f.Name),
            IsDir = f.IsDir,
            IsSymlink = f.IsSymlink,
            Size = f.Size,
            Date = f.Date,
            Perms = f.Perms,
            Parent = parent,
        };
        node.ExpandRequested += OnExpandRequested;
        if (node.IsExpandable) node.Children.Add(FileTreeNode.Placeholder());
        return node;
    }

    private static string JoinPath(string parent, string name) =>
        parent.EndsWith('/') ? parent + name : parent + "/" + name;

    /// <summary>强制重新加载某个目录节点的子项；null 表示重载根。</summary>
    private async Task ReloadNodeAsync(FileTreeNode? node)
    {
        if (node is null)
        {
            await LoadRootAsync();
            return;
        }
        if (!node.IsExpandable) return;
        node.ChildrenLoaded = false;
        if (node.IsExpanded) await LoadChildrenAsync(node);
    }

    private static FileTreeNode? FindNode(IEnumerable<FileTreeNode> nodes, string fullPath)
    {
        foreach (var n in nodes)
        {
            if (n.FullPath == fullPath) return n;
            var found = FindNode(n.Children, fullPath);
            if (found != null) return found;
        }
        return null;
    }

    private async Task RefreshSelectedAsync()
    {
        if (SelectedNode is { IsExpandable: true, IsPlaceholder: false })
        {
            await ReloadNodeAsync(SelectedNode);
            return;
        }
        await LoadRootAsync();
    }

    // ---------- 目录操作 ----------

    /// <summary>新建目录的父节点：选中的目录/软链，或选中文件的父目录，或根。</summary>
    private FileTreeNode? ParentNodeForCreate()
    {
        if (SelectedNode is { IsPlaceholder: false } s)
            return s.IsExpandable ? s : s.Parent;
        return null; // null 表示根
    }

    private async Task CreateDirAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = "请先在顶部选择设备"; return; }
        if (NewDirName.Trim().Length == 0) { Status = "请填写新目录名"; return; }

        var parent = ParentNodeForCreate();
        var parentPath = parent?.FullPath ?? RootPath.Trim();
        var target = JoinPath(parentPath, NewDirName.Trim());
        var r = await AppSession.Adb.RunShellAsync($"mkdir {Quote(target)}", serial);
        if (!r.Success)
        {
            Status = $"创建失败：{r.Combined.Trim()}";
            return;
        }

        Status = $"已创建 {target}";
        NewDirName = "";
        // 父节点已展开则刷新；未展开则等下次展开时自然加载
        if (parent is { ChildrenLoaded: true }) await ReloadNodeAsync(parent);
        else if (parent is null) await ReloadNodeAsync(null);
    }

    private async Task DeleteSelectedAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        var node = SelectedNode;
        if (serial is null) { Status = "请先在顶部选择设备"; return; }
        if (node is null || node.IsPlaceholder) { Status = "请先在树中选中要删除的项"; return; }

        if (MessageBox.Show($"确定删除 {(node.IsExpandable ? "目录" : "文件")} {node.FullPath} 吗？（目录会递归删除）",
                "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var r = await AppSession.Adb.RunShellAsync($"rm -r {Quote(node.FullPath)}", serial);
        if (!r.Success)
        {
            Status = $"删除失败：{r.Combined.Trim()}";
            return;
        }

        Status = $"已删除 {node.FullPath}";
        var parent = node.Parent;
        SelectedNode = null;
        await ReloadNodeAsync(parent); // parent 为 null 时重载根
    }

    // ---------- 传输 ----------

    /// <summary>推送目标目录：选中的目录/软链 > 选中文件的父目录 > 根。</summary>
    private string TargetDirForPush() =>
        SelectedNode is { IsPlaceholder: false } s
            ? (s.IsExpandable ? s.FullPath : s.Parent?.FullPath ?? RootPath.Trim())
            : RootPath.Trim();

    private async Task PushAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = "请先在顶部选择设备"; return; }

        var local = LocalPath.Trim().Trim('"');
        if (local.Length == 0 || (!File.Exists(local) && !Directory.Exists(local)))
        {
            Status = "请填写有效的本地文件/文件夹路径";
            return;
        }

        await PushCoreAsync(serial, local);
    }

    private async Task PushCoreAsync(string serial, string local)
    {
        var target = TargetDirForPush();
        Status = $"正在推送 {Path.GetFileName(local)} → {target} ...";
        var r = await AppSession.Adb.PushAsync(serial, local, target);
        if (!r.Success)
        {
            Status = $"推送失败：{r.Combined.Trim()}";
            return;
        }

        Status = $"推送完成 → {target}";
        var dirNode = Directory.Exists(local)
            ? FindNode(RootNodes, JoinPath(target, Path.GetFileName(local.TrimEnd('/'))))
              ?? FindNode(RootNodes, target)
            : FindNode(RootNodes, target);
        await ReloadNodeAsync(dirNode);
    }

    /// <summary>选文件夹对话框（OpenFileDialog 变体，无需 WinForms）。</summary>
    private static string? PickFolder(string title)
    {
        const string marker = "打开此文件夹";
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = marker,
        };
        if (dlg.ShowDialog() != true) return null;

        var path = dlg.FileName;
        if (path.EndsWith(marker, StringComparison.Ordinal))
            path = path[..^marker.Length].TrimEnd('\\');
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>右键「推送...」：弹文件/文件夹选择框，推送到选中目录。</summary>
    public async Task PushViaDialogAsync(bool isFolder)
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = "请先在顶部选择设备"; return; }
        var target = TargetDirForPush();

        string? local;
        if (isFolder)
        {
            local = PickFolder($"选择要推送的文件夹（进入文件夹后点“打开”）");
            if (local is null) { Status = "未选择文件夹"; return; }
        }
        else
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "选择要推送的文件" };
            if (dlg.ShowDialog() != true) { Status = "已取消"; return; }
            local = dlg.FileName;
        }

        await PushCoreAsync(serial, local);
    }

    /// <summary>右键「拉取...」：文件 → 另存为对话框；目录 → 选择保存位置。</summary>
    public async Task PullViaDialogAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = "请先在顶部选择设备"; return; }
        var node = SelectedNode;
        if (node is null || node.IsPlaceholder)
        {
            Status = "请先在树中选中要拉取的项";
            return;
        }

        if (!node.IsExpandable)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存文件到本机",
                FileName = node.Name,
            };
            if (dlg.ShowDialog() != true) { Status = "已取消"; return; }

            Status = $"正在拉取 {node.FullPath} ...";
            var r = await AppSession.Adb.PullAsync(serial, node.FullPath, dlg.FileName);
            Status = r.Success
                ? $"已拉取到 {dlg.FileName}"
                : $"拉取失败：{r.Combined.Trim()}";
        }
        else
        {
            var folder = PickFolder("选择保存位置（进入目标文件夹后点“打开”）");
            if (folder is null) { Status = "未选择保存位置"; return; }

            Status = $"正在拉取 {node.FullPath} ...";
            var r = await AppSession.Adb.PullAsync(serial, node.FullPath, folder);
            Status = r.Success
                ? $"已拉取到 {folder}"
                : $"拉取失败：{r.Combined.Trim()}";
        }
    }

    /// <summary>右键「新建文件夹...」：输入名称后在选中目录下创建。</summary>
    public async Task CreateDirViaDialogAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = "请先在顶部选择设备"; return; }
        var parent = ParentNodeForCreate();
        var parentPath = parent?.FullPath ?? RootPath.Trim();

        var name = InputDialogWindow.Show("新建文件夹",
            $"将在 {parentPath} 下创建：", "新建文件夹");
        if (string.IsNullOrWhiteSpace(name)) return;

        var target = JoinPath(parentPath, name.Trim());
        var r = await AppSession.Adb.RunShellAsync($"mkdir {Quote(target)}", serial);
        if (!r.Success)
        {
            Status = $"创建失败：{r.Combined.Trim()}";
            return;
        }

        Status = $"已创建 {target}";
        NewDirName = "";
        // 父节点已展开则刷新；未展开则等下次展开时自然加载
        if (parent is { ChildrenLoaded: true }) await ReloadNodeAsync(parent);
        else if (parent is null) await ReloadNodeAsync(null);
    }

    private void BrowseLocal()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择要推送的文件" };
        if (dialog.ShowDialog() == true) LocalPath = dialog.FileName;
    }

    private async Task PullAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) { Status = "请先在顶部选择设备"; return; }

        var devicePath = SelectedNode is { IsPlaceholder: false } s
            ? s.FullPath
            : RootPath.Trim();

        Directory.CreateDirectory(LocalDir);
        Status = $"正在拉取 {devicePath} ...";
        var r = await AppSession.Adb.PullAsync(serial, devicePath, LocalDir);
        Status = r.Success
            ? $"已拉取到 {LocalDir}"
            : $"拉取失败：{r.Combined.Trim()}";
    }

    private void OpenLocalFolder()
    {
        Directory.CreateDirectory(LocalDir);
        Process.Start(new ProcessStartInfo
        {
            FileName = LocalDir,
            UseShellExecute = true,
        });
    }

    private static string Quote(string path) =>
        string.IsNullOrWhiteSpace(path) || !path.Contains(' ') ? path : $"\"{path}\"";

    /// <summary>复制选中节点的完整设备路径（右键菜单）。</summary>
    public void CopySelectedPath()
    {
        if (SelectedNode is not { IsPlaceholder: false } s)
        {
            Status = "请先选中节点";
            return;
        }
        try
        {
            Clipboard.SetText(s.FullPath);
            Status = $"已复制：{s.FullPath}";
        }
        catch { /* 剪贴板被占用等极端情况 */ }
    }
}
