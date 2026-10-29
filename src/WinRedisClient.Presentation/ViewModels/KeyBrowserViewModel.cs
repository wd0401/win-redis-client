using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;
using WinRedisClient.Core.Services;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

/// <summary>Key 浏览器中的单个 Key。</summary>
public sealed class KeyItemViewModel : ObservableObject
{
    private string _name;
    private string _type;
    private TimeSpan? _ttl;
    private long? _sizeBytes;
    private long? _elementCount;

    public KeyItemViewModel(KeyMetadata metadata)
    {
        _name = metadata.Key;
        _type = metadata.Type;
        _ttl = metadata.TimeToLive;
        _sizeBytes = metadata.SizeBytes;
        _elementCount = metadata.ElementCount;
    }

    public string Name
    {
        get => _name;
        private set
        {
            if (!SetProperty(ref _name, value)) return;
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => Name;
    public string Type => _type;
    public TimeSpan? Ttl => _ttl;
    public long? SizeBytes => _sizeBytes;
    public long? ElementCount => _elementCount;

    public string TypeDisplay => Type switch
    {
        "string" => "String",
        "hash" => "Hash",
        "list" => "List",
        "set" => "Set",
        "zset" => "ZSet",
        "stream" => "Stream",
        _ => Type,
    };

    public string TypeShort => Type switch
    {
        "string" => "STR",
        "hash" => "HASH",
        "list" => "LIST",
        "set" => "SET",
        "zset" => "ZSET",
        "stream" => "STREAM",
        _ => "?",
    };

    public string TtlText => TtlFormatter.Format(Ttl);

    public string SizeText => SizeBytes is null ? "—" : TtlFormatter.FormatBytes(SizeBytes);

    public string ElementCountText => ElementCount switch
    {
        null => "—",
        var count when Type == "string" => TtlFormatter.FormatBytes(count),
        var count => count.Value.ToString("N0"),
    };

    public string DetailText => $"{TypeDisplay} · {TtlText} · {SizeText}";

    public void UpdateMetadata(KeyMetadata metadata)
    {
        _type = metadata.Type;
        _ttl = metadata.TimeToLive;
        _sizeBytes = metadata.SizeBytes;
        _elementCount = metadata.ElementCount;
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(TypeDisplay));
        OnPropertyChanged(nameof(TypeShort));
        OnPropertyChanged(nameof(Ttl));
        OnPropertyChanged(nameof(TtlText));
        OnPropertyChanged(nameof(SizeBytes));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(ElementCount));
        OnPropertyChanged(nameof(ElementCountText));
        OnPropertyChanged(nameof(DetailText));
    }

    public void Rename(string newName) => Name = newName;
}

/// <summary>树形视图节点（客户端虚拟目录，不是 Redis 真实目录）。</summary>
public sealed partial class KeyTreeNodeViewModel : ObservableObject
{
    [ObservableProperty] private bool _isExpanded;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public KeyItemViewModel? Key { get; init; }
    public IReadOnlyList<KeyTreeNodeViewModel> Children { get; init; } = [];
    public bool IsFolder => Children.Count > 0;

    public string CountText => IsFolder ? $"({LeafCount})" : string.Empty;
    public int LeafCount { get; init; }

    /// <summary>分类颜色键由 XAML 根据 Key 的类型 DataTrigger 处理。</summary>
    public string TypeShort => Key?.TypeShort ?? string.Empty;
}

/// <summary>Key 浏览器：SCAN 分页、搜索防抖、类型/TTL 筛选、列表/树形视图与 Key 级操作。</summary>
public partial class KeyBrowserViewModel : ObservableObject
{
    private const int PageSize = 500;
    private const int AutoLoadTarget = 2000;
    private static readonly string SearchModeLocal = Loc.T("模糊匹配");
    private static readonly string SearchModeExact = Loc.T("全匹配");
    private static readonly string SearchModeDatabase = Loc.T("数据库模糊");

    private readonly HashSet<string> _loadedKeys = new(StringComparer.Ordinal);
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _searchCts;
    private int _scanGeneration;
    private long _nextCursor;
    private string _lastStableSearchMode = SearchModeLocal;
    private bool _suppressSearchModeChange;
    private SearchSnapshot? _searchSnapshot;
    private Guid _connectionId;
    private string _treeStateKey = string.Empty;
    private readonly HashSet<string> _expandedFolders = new(StringComparer.Ordinal);
    private CancellationTokenSource? _treeStateSaveCts;

    private sealed record SearchSnapshot(
        List<KeyItemViewModel> Keys,
        HashSet<string> LoadedKeys,
        long NextCursor,
        bool HasMore,
        KeyItemViewModel? Selected);

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _searchMode = SearchModeLocal;
    [ObservableProperty] private string _typeFilter = Loc.T("全部");
    [ObservableProperty] private string _ttlFilter = Loc.T("全部");
    [ObservableProperty] private bool _isTreeView;
    [ObservableProperty] private KeyItemViewModel? _selectedKey;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private string _scanStatus = Loc.T("尚未扫描");
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private string _connectionName = string.Empty;
    [ObservableProperty] private int _database;
    [ObservableProperty] private string _keySeparator = ":";
    [ObservableProperty] private bool _showKeySize = true;
    [ObservableProperty] private bool _autoLoadKeys = true;

    public ObservableCollection<KeyItemViewModel> Keys { get; } = [];
    public ObservableCollection<KeyItemViewModel> VisibleKeys { get; } = [];
    public ObservableCollection<KeyTreeNodeViewModel> TreeRoots { get; } = [];

    public IReadOnlyList<string> TypeFilters { get; } = [Loc.T("全部"), "string", "hash", "list", "set", "zset", "stream"];
    public IReadOnlyList<string> TtlFilters { get; } = [Loc.T("全部"), Loc.T("有 TTL"), Loc.T("永不过期")];
    public IReadOnlyList<string> SearchModes { get; } = [SearchModeLocal, SearchModeExact, SearchModeDatabase];

    public string ContextText => IsConnected ? $"{ConnectionName} · DB{Database}" : Loc.T("未连接");
    public bool IsLocalSearchMode => SearchMode == SearchModeLocal;
    public string LoadedText => Keys.Count == 0 ? Loc.T("0 个 Key") : Loc.F("已加载 {0:N0} 个 Key", Keys.Count);
    public bool IsEmpty => !IsScanning && VisibleKeys.Count == 0;

    public event EventHandler<NotificationMessage>? NotificationRequested;
    public event EventHandler<ConfirmationRequest>? ConfirmationRequested;
    public event EventHandler<string>? CopyRequested;
    public event EventHandler? AddKeyRequested;
    public event EventHandler<KeyItemViewModel>? RenameKeyRequested;
    public event EventHandler<KeyItemViewModel>? SetTtlRequested;
    public event EventHandler<KeyItemViewModel>? DeleteKeyRequested;

    public void Configure(bool connected, string connectionName, int database, bool readOnly, string separator, bool isTreeView, bool showKeySize, bool autoLoadKeys = true, Guid connectionId = default)
    {
        IsConnected = connected;
        ConnectionName = connectionName;
        Database = database;
        IsReadOnly = readOnly;
        KeySeparator = string.IsNullOrEmpty(separator) ? ":" : separator;
        IsTreeView = isTreeView;
        ShowKeySize = showKeySize;
        AutoLoadKeys = autoLoadKeys;
        _connectionId = connectionId;
        RebuildTreeStateKey();
        LoadExpandedFolderState();
        OnPropertyChanged(nameof(ContextText));
    }

    public void Reset()
    {
        CancelScan();
        // 让仍在飞行中的批量扫描/加载任务失效，避免切换连接或 DB 后追加旧数据。
        Interlocked.Increment(ref _scanGeneration);
        _loadedKeys.Clear();
        Keys.Clear();
        VisibleKeys.Clear();
        TreeRoots.Clear();
        SelectedKey = null;
        _nextCursor = 0;
        HasMore = false;
        IsScanning = false;
        ErrorText = string.Empty;
        ScanStatus = IsConnected ? Loc.T("点击刷新开始浏览") : Loc.T("未连接");
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsConnected || AppServices.Redis is null)
        {
            ScanStatus = Loc.T("请先连接 Redis");
            return;
        }

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _scanCts, cts);
        old?.Cancel();

        var generation = Interlocked.Increment(ref _scanGeneration);
        IsScanning = true;
        ErrorText = string.Empty;
        ScanStatus = IsExactSearchActive ? Loc.T("正在精确查找…")
            : SearchMode == SearchModeDatabase ? Loc.T("正在扫描数据库（模糊匹配）…")
            : Loc.T("正在扫描…");
        var previousSelection = SelectedKey?.Name;

        try
        {
            _loadedKeys.Clear();
            Keys.Clear();
            VisibleKeys.Clear();
            TreeRoots.Clear();
            _nextCursor = 0;
            OnPropertyChanged(nameof(IsEmpty));

            var redis = AppServices.Redis!;
            if (IsExactSearchActive)
            {
                // 全匹配不走 SCAN：直接读取目标 Key 的元数据，复杂度 O(1)。
                var exactName = SearchText.Trim();
                KeyMetadata? meta = null;
                try
                {
                    meta = await redis.GetMetadataAsync(exactName, ShowKeySize);
                }
                catch (Exception ex)
                {
                    if (generation != _scanGeneration) return;
                    ErrorText = Loc.T("精确查找失败：") + ex.Message;
                    ScanStatus = Loc.T("查找失败");
                    return;
                }

                if (generation != _scanGeneration) return;

                HasMore = false;
                if (meta is not null && meta.Type is not "none" and not "unknown")
                {
                    var item = new KeyItemViewModel(meta);
                    _loadedKeys.Add(item.Name);
                    Keys.Add(item);
                    ApplyFilters();
                    if (previousSelection is not null)
                        SelectedKey = VisibleKeys.FirstOrDefault(k => k.Name == previousSelection);
                    ScanStatus = Loc.F("精确匹配：{0}", exactName);
                }
                else
                {
                    ApplyFilters();
                    ScanStatus = Loc.T("没有键名完全匹配的 Key");
                }

                return;
            }

            var page = await FetchNonEmptyPageAsync(_nextCursor, cts.Token);
            if (generation != _scanGeneration) return;

            await AppendPageAsync(page, cts.Token);
            if (generation != _scanGeneration) return;

            _nextCursor = page.NextCursor;
            HasMore = !page.IsComplete;

            if (previousSelection is not null)
                SelectedKey = VisibleKeys.FirstOrDefault(k => k.Name == previousSelection);

            UpdateScanStatus();
        }
        catch (OperationCanceledException)
        {
            if (generation == _scanGeneration)
                ScanStatus = Loc.T("扫描已取消");
        }
        catch (Exception ex)
        {
            if (generation == _scanGeneration)
            {
                ErrorText = Loc.T("扫描失败：") + ex.Message;
                ScanStatus = Loc.T("扫描失败");
                NotificationRequested?.Invoke(this, new NotificationMessage(ErrorText, true));
            }
        }
        finally
        {
            if (generation == _scanGeneration)
            {
                IsScanning = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || IsScanning || !IsConnected || AppServices.Redis is null) return;

        var cts = _scanCts ?? new CancellationTokenSource();
        var generation = _scanGeneration;
        IsScanning = true;
        ErrorText = string.Empty;
        ScanStatus = Loc.T("正在加载更多…");

        try
        {
            var page = await FetchNonEmptyPageAsync(_nextCursor, cts.Token);
            if (generation != _scanGeneration) return;

            await AppendPageAsync(page, cts.Token);
            if (generation != _scanGeneration) return;

            _nextCursor = page.NextCursor;
            HasMore = !page.IsComplete;
            UpdateScanStatus();
        }
        catch (OperationCanceledException)
        {
            if (generation == _scanGeneration) ScanStatus = Loc.T("加载已取消");
        }
        catch (Exception ex)
        {
            if (generation == _scanGeneration)
            {
                ErrorText = Loc.T("加载失败：") + ex.Message;
                NotificationRequested?.Invoke(this, new NotificationMessage(ErrorText, true));
            }
        }
        finally
        {
            if (generation == _scanGeneration)
            {
                IsScanning = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _searchCts?.Cancel();
        _scanCts?.Cancel();
        IsScanning = false;
        ScanStatus = Loc.T("已取消扫描");
    }

    /// <summary>
    /// 连接后按批次自动加载（最多 AutoLoadTarget 个），避免大库一次性拉取导致卡顿；
    /// 达到目标后保留游标，用户可继续点击“加载下一批”。
    /// </summary>
    public async Task AutoLoadAsync()
    {
        if (!AutoLoadKeys || !IsConnected || IsScanning || !HasMore ||
            AppServices.Redis is null || IsExactSearchActive ||
            SearchMode != SearchModeLocal ||
            !string.IsNullOrWhiteSpace(SearchText))
            return;

        var cts = _scanCts;
        if (cts is null || cts.IsCancellationRequested)
        {
            cts = new CancellationTokenSource();
            _scanCts = cts;
        }

        var generation = _scanGeneration;
        IsScanning = true;
        ErrorText = string.Empty;

        try
        {
            while (HasMore && Keys.Count < AutoLoadTarget)
            {
                ScanStatus = Loc.F("批量加载中… 已加载 {0:N0} 个（目标 {1:N0}）", Keys.Count, AutoLoadTarget);
                var page = await FetchNonEmptyPageAsync(_nextCursor, cts.Token);
                if (generation != _scanGeneration) return;

                await AppendPageAsync(page, cts.Token);
                if (generation != _scanGeneration) return;

                _nextCursor = page.NextCursor;
                HasMore = !page.IsComplete;
                await Task.Yield();
            }

            if (HasMore)
                ScanStatus = Loc.F("已批量加载 {0:N0} 个 Key（还有更多，可继续加载）", Keys.Count);
            else
                UpdateScanStatus();
        }
        catch (OperationCanceledException)
        {
            if (generation == _scanGeneration) ScanStatus = Loc.T("批量加载已取消");
        }
        catch (Exception ex)
        {
            if (generation == _scanGeneration)
            {
                ErrorText = Loc.T("批量加载失败：") + ex.Message;
                NotificationRequested?.Invoke(this, new NotificationMessage(ErrorText, true));
            }
        }
        finally
        {
            if (generation == _scanGeneration)
            {
                IsScanning = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>本地模糊搜索按 Enter 立即过滤，不访问 Redis。</summary>
    public void ApplyLocalSearchNow()
    {
        _searchCts?.Cancel();
        ApplyFilters();
        UpdateScanStatus();
    }

    [RelayCommand]
    private void AddKey() => AddKeyRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RenameKey(KeyItemViewModel? item)
    {
        if (item is not null) RenameKeyRequested?.Invoke(this, item);
    }

    [RelayCommand]
    private void SetTtl(KeyItemViewModel? item)
    {
        if (item is not null) SetTtlRequested?.Invoke(this, item);
    }

    [RelayCommand]
    private void DeleteKey(KeyItemViewModel? item)
    {
        if (item is not null) DeleteKeyRequested?.Invoke(this, item);
    }

    [RelayCommand]
    private void CopyKey(KeyItemViewModel? item)
    {
        if (item is not null)
        {
            CopyRequested?.Invoke(this, item.Name);
        }
    }

    public async Task<CommandResult> RenameAsync(KeyItemViewModel item, string newName, bool confirmed)
    {
        if (AppServices.Redis is null)
            return new CommandResult { Outcome = ExecutionOutcome.Failure, ErrorCode = "not_connected" };

        var result = await AppServices.Redis.RenameKeyAsync(item.Name, newName, confirmed);
        if (result.IsSuccess)
        {
            _loadedKeys.Remove(item.Name);
            _loadedKeys.Add(newName);
            item.Rename(newName);
            ApplyFilters();
            SelectedKey = item;
            ScanStatus = Loc.F("已重命名：{0}", newName);
            OnPropertyChanged(nameof(LoadedText));
        }

        return result;
    }

    public async Task<CommandResult> DeleteAsync(KeyItemViewModel item, bool confirmed)
    {
        if (AppServices.Redis is null)
            return new CommandResult { Outcome = ExecutionOutcome.Failure, ErrorCode = "not_connected" };

        var result = await AppServices.Redis.DeleteKeyAsync(item.Name, confirmed);
        if (result.IsSuccess)
        {
            _loadedKeys.Remove(item.Name);
            Keys.Remove(item);
            if (SelectedKey == item) SelectedKey = null;
            ApplyFilters();
            UpdateScanStatus();
            OnPropertyChanged(nameof(LoadedText));
        }

        return result;
    }

    public async Task<CommandResult> SetTtlAsync(KeyItemViewModel item, TimeSpan? ttl)
    {
        if (AppServices.Redis is null)
            return new CommandResult { Outcome = ExecutionOutcome.Failure, ErrorCode = "not_connected" };

        var result = await AppServices.Redis.SetTtlAsync(item.Name, ttl);
        if (result.IsSuccess)
        {
            var meta = await AppServices.Redis.GetMetadataAsync(item.Name, ShowKeySize);
            if (meta is not null) item.UpdateMetadata(meta);
            ApplyFilters();
            ScanStatus = Loc.F("TTL 已更新：{0}", item.TtlText);
        }

        return result;
    }

    public async Task<(CommandResult Result, string? Key)> CreateAsync(
        string name, string type, IReadOnlyList<string> values, TimeSpan? ttl,
        bool overwrite, bool confirmed)
    {
        if (AppServices.Redis is null)
            return (new CommandResult { Outcome = ExecutionOutcome.Failure, ErrorCode = "not_connected" }, null);

        var redis = AppServices.Redis;
        var exists = await redis.KeyExistsAsync(name);
        if (exists && !overwrite)
            return (new CommandResult
            {
                Outcome = ExecutionOutcome.Failure,
                ErrorCode = "target_exists",
                ErrorMessage = Loc.T("Key 已存在；请勾选覆盖或修改名称"),
            }, null);

        if (exists)
        {
            var deleted = await redis.DeleteKeyAsync(name, confirmed);
            if (!deleted.IsSuccess) return (deleted, null);
        }

        var result = await redis.CreateKeyAsync(name, type, values, ttl);
        if (!result.IsSuccess) return (result, null);

        // 刷新列表并定位新 Key；若当前搜索条件不含新 Key，则清空搜索
        await RefreshAsync();
        var created = Keys.FirstOrDefault(k => k.Name == name);
        if (created is null && !string.IsNullOrWhiteSpace(SearchText))
        {
            SearchText = string.Empty;
            await RefreshAsync();
            created = Keys.FirstOrDefault(k => k.Name == name);
        }

        if (created is not null) SelectedKey = created;
        return (result, name);
    }

    public async Task RefreshItemMetadataAsync(string key)
    {
        var redis = AppServices.Redis;
        if (redis is not { IsConnected: true }) return;
        var item = Keys.FirstOrDefault(k => k.Name == key);
        if (item is null) return;

        try
        {
            var meta = await redis.GetMetadataAsync(key, ShowKeySize);
            if (meta is null || meta.Type == "none")
            {
                Keys.Remove(item);
                _loadedKeys.Remove(key);
                if (SelectedKey == item) SelectedKey = null;
                ApplyFilters();
                return;
            }

            item.UpdateMetadata(meta);
            ApplyFilters();
            OnPropertyChanged(nameof(LoadedText));
        }
        catch
        {
            // 元数据刷新失败时保留列表已有信息
        }
    }

    public void UpdateDatabase(int database)
    {
        Database = database;
        RebuildTreeStateKey();
        LoadExpandedFolderState();
        OnPropertyChanged(nameof(ContextText));
    }

    /// <summary>按名称打开 Key（不存在时读取一次元数据加入列表）。</summary>
    public async Task<bool> OpenKeyAsync(string key)
    {
        if (!IsConnected || AppServices.Redis is null || string.IsNullOrWhiteSpace(key)) return false;

        var item = Keys.FirstOrDefault(k => string.Equals(k.Name, key, StringComparison.Ordinal));
        if (item is null)
        {
            KeyMetadata? meta;
            try
            {
                meta = await AppServices.Redis.GetMetadataAsync(key, ShowKeySize);
            }
            catch
            {
                return false;
            }

            if (meta is null || meta.Type is "none" or "unknown") return false;

            item = new KeyItemViewModel(meta);
            _loadedKeys.Add(meta.Key);
            Keys.Add(item);
        }

        if (TypeFilter != Loc.T("全部") &&
            !string.Equals(item.Type, TypeFilter, StringComparison.OrdinalIgnoreCase))
            TypeFilter = Loc.T("全部");
        if (TtlFilter != Loc.T("全部"))
            TtlFilter = Loc.T("全部");

        ApplyFilters();
        if (!VisibleKeys.Contains(item))
            VisibleKeys.Add(item);

        SelectedKey = item;
        OnPropertyChanged(nameof(LoadedText));
        return true;
    }

    /// <summary>用最新元数据更新列表中已有的 Key（不重新扫描）。</summary>
    public void ApplyMetadata(KeyMetadata metadata)
    {
        var item = Keys.FirstOrDefault(k => string.Equals(k.Name, metadata.Key, StringComparison.Ordinal));
        if (item is null || metadata.Type is "none" or "unknown") return;
        item.UpdateMetadata(metadata);
        ApplyFilters();
    }

    public void Notify(string message, bool isError = false)
        => NotificationRequested?.Invoke(this, new NotificationMessage(message, isError));

    private async Task<ScanPage> FetchNonEmptyPageAsync(long cursor, CancellationToken cancellationToken)
    {
        var redis = AppServices.Redis ?? throw new InvalidOperationException(Loc.T("尚未连接 Redis"));
        var page = await redis.ScanPageAsync(BuildPattern(), PageSize, cursor, cancellationToken);
        var attempts = 0;

        // SCAN 可能返回空页但游标未结束，继续取下一页，避免 UI 误判“无数据”
        while (page.Keys.Count == 0 && !page.IsComplete && attempts < 20)
        {
            cancellationToken.ThrowIfCancellationRequested();
            page = await redis.ScanPageAsync(BuildPattern(), PageSize, page.NextCursor, cancellationToken);
            attempts++;
        }

        return page;
    }

    private async Task AppendPageAsync(ScanPage page, CancellationToken cancellationToken)
    {
        var redis = AppServices.Redis ?? throw new InvalidOperationException(Loc.T("尚未连接 Redis"));

        var newKeys = page.Keys
            .Where(k => _loadedKeys.Add(k.DisplayKey))
            .Select(k => k.DisplayKey)
            .ToList();

        if (newKeys.Count == 0) return;

        var metadata = await redis.GetMetadataBatchAsync(newKeys, ShowKeySize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var meta in metadata)
        {
            if (meta.Type is "none" or "unknown") continue;
            Keys.Add(new KeyItemViewModel(meta));
        }

        ApplyFilters();
        OnPropertyChanged(nameof(LoadedText));
    }

    private string BuildPattern()
    {
        // 模糊匹配默认是纯前端过滤：刷新/续载只扫描 *，结果由 ApplyFilters 在本地筛。
        if (SearchMode == SearchModeLocal) return "*";

        var text = SearchText.Trim();
        if (text.Length == 0) return "*";
        if (text.IndexOfAny(['*', '?', '[']) >= 0) return text;
        return $"*{text}*";
    }

    private bool IsExactSearchActive =>
        SearchMode == SearchModeExact && !string.IsNullOrWhiteSpace(SearchText);

    private bool IsLocalFuzzyActive =>
        SearchMode == SearchModeLocal && !string.IsNullOrWhiteSpace(SearchText);

    private void ApplyFilters()
    {
        var previousSelection = SelectedKey;
        var localQuery = IsLocalFuzzyActive ? SearchText.Trim() : null;
        var filtered = Keys.Where(k =>
            (TypeFilter == Loc.T("全部") || string.Equals(k.Type, TypeFilter, StringComparison.OrdinalIgnoreCase)) &&
            (TtlFilter != Loc.T("有 TTL") || k.Ttl is not null) &&
            (TtlFilter != Loc.T("永不过期") || k.Ttl is null) &&
            (localQuery is null || k.Name.Contains(localQuery, StringComparison.Ordinal)))
            .OrderBy(k => k.Name, StringComparer.Ordinal)
            .ToList();

        VisibleKeys.Clear();
        foreach (var key in filtered)
            VisibleKeys.Add(key);

        RebuildTree();
        OnPropertyChanged(nameof(IsEmpty));

        // 列表刷新会清空 ItemsSource，折叠的列表视图会把 SelectedItem 的 null
        // 双向写回 SelectedKey，导致写入后详情面板被关闭。这里在重建完成后恢复。
        SelectedKey = previousSelection is not null && VisibleKeys.Contains(previousSelection)
            ? previousSelection
            : null;
    }

    private void RebuildTree()
    {
        TreeRoots.Clear();
        if (!IsTreeView) return;

        var roots = KeyTreeBuilder.Build(VisibleKeys, KeySeparator, _expandedFolders);
        foreach (var node in roots)
        {
            TrackTreeNode(node);
            TreeRoots.Add(node);
        }
    }

    private void TrackTreeNode(KeyTreeNodeViewModel node)
    {
        node.PropertyChanged += OnTreeNodePropertyChanged;
        foreach (var child in node.Children)
            TrackTreeNode(child);
    }

    private void OnTreeNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(KeyTreeNodeViewModel.IsExpanded) ||
            sender is not KeyTreeNodeViewModel node || node.Children.Count == 0)
            return;

        if (node.IsExpanded)
            _expandedFolders.Add(node.FullPath);
        else
            _expandedFolders.Remove(node.FullPath);

        SchedulePersistExpandedFolders();
    }

    private void RebuildTreeStateKey()
        => _treeStateKey = _connectionId == Guid.Empty ? string.Empty : $"{_connectionId:N}:{Database}";

    private void LoadExpandedFolderState()
    {
        _expandedFolders.Clear();
        if (string.IsNullOrEmpty(_treeStateKey)) return;

        if (AppServices.Settings.ExpandedFolders.TryGetValue(_treeStateKey, out var paths))
        {
            foreach (var path in paths)
                _expandedFolders.Add(path);
        }
    }

    private void SchedulePersistExpandedFolders()
    {
        if (string.IsNullOrEmpty(_treeStateKey)) return;

        _treeStateSaveCts?.Cancel();
        _treeStateSaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _treeStateSaveCts = cts;
        _ = PersistExpandedFoldersAsync(cts);
    }

    private async Task PersistExpandedFoldersAsync(CancellationTokenSource cts)
    {
        var store = AppServices.SettingsStore;
        if (store is null) return;

        try
        {
            await Task.Delay(400, cts.Token);
            if (cts.IsCancellationRequested) return;

            var settings = AppServices.Settings;
            var folders = new Dictionary<string, string[]>(settings.ExpandedFolders, StringComparer.Ordinal)
            {
                [_treeStateKey] = _expandedFolders.OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            };

            var updated = settings with { ExpandedFolders = folders };
            AppServices.Settings = updated;
            await store.SaveAsync(updated);
        }
        catch (OperationCanceledException)
        {
            // 更晚的展开/折叠状态会再次安排写入
        }
        catch
        {
            // 目录展开状态持久化失败不影响浏览
        }
    }

    private void UpdateScanStatus()
    {
        if (ErrorText.Length > 0) return;

        if (IsLocalFuzzyActive)
        {
            ScanStatus = Loc.F("本地过滤：显示 {0:N0} / 已加载 {1:N0} 个 Key（仅当前已加载数据）", VisibleKeys.Count, Keys.Count);
            return;
        }

        ScanStatus = Keys.Count == 0
            ? Loc.T("没有找到匹配的 Key")
            : HasMore
                ? Loc.F("已加载 {0:N0} 个 Key（还有更多，可继续加载）", Keys.Count)
                : Loc.F("已加载 {0:N0} 个 Key（扫描完成）", Keys.Count);
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!IsConnected) return;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();

        if (SearchMode == SearchModeLocal)
            _ = DebouncedLocalFilterAsync(_searchCts);
        else
            _ = DebouncedSearchAsync(_searchCts);
    }

    partial void OnSearchModeChanged(string value)
    {
        if (_suppressSearchModeChange) return;

        if (!IsConnected)
        {
            if (value == SearchModeDatabase)
                RevertSearchMode(_lastStableSearchMode, Loc.T("请先连接 Redis，再启用数据库模糊搜索"));
            else
                _lastStableSearchMode = value;
            return;
        }

        if (value == SearchModeDatabase)
        {
            _ = ConfirmAndStartDatabaseSearchAsync();
            return;
        }

        _lastStableSearchMode = value;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();

        if (value == SearchModeLocal)
        {
            ActivateLocalSearch();
        }
        else
        {
            CaptureSearchSnapshot();
            _ = DebouncedSearchAsync(_searchCts);
        }
    }

    private async Task DebouncedLocalFilterAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(180, cts.Token);
            if (cts.IsCancellationRequested) return;

            ApplyFilters();
            UpdateScanStatus();
        }
        catch (OperationCanceledException)
        {
            // 输入仍在继续
        }
    }

    /// <summary>切回纯前端模糊搜索：恢复进入服务端搜索前的 Key 集合，再在本地过滤。</summary>
    private void ActivateLocalSearch()
    {
        _scanCts?.Cancel();
        Interlocked.Increment(ref _scanGeneration);
        IsScanning = false;

        if (!RestoreSearchSnapshot())
        {
            // 没有快照（例如刚启动就切换）时重新加载基础 Key 集合。
            _ = RefreshAsync();
            return;
        }

        ApplyFilters();
        UpdateScanStatus();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void CaptureSearchSnapshot()
    {
        if (_searchSnapshot is not null) return;

        _searchSnapshot = new SearchSnapshot(
            Keys.ToList(),
            new HashSet<string>(_loadedKeys, StringComparer.Ordinal),
            _nextCursor,
            HasMore,
            SelectedKey);
    }

    private bool RestoreSearchSnapshot()
    {
        var snapshot = _searchSnapshot;
        if (snapshot is null) return false;

        _searchSnapshot = null;
        _loadedKeys.Clear();
        foreach (var key in snapshot.LoadedKeys)
            _loadedKeys.Add(key);

        Keys.Clear();
        foreach (var key in snapshot.Keys)
            Keys.Add(key);

        _nextCursor = snapshot.NextCursor;
        HasMore = snapshot.HasMore;
        ApplyFilters();
        SelectedKey = snapshot.Selected is not null && VisibleKeys.Contains(snapshot.Selected)
            ? snapshot.Selected
            : null;
        OnPropertyChanged(nameof(LoadedText));
        return true;
    }

    private async Task ConfirmAndStartDatabaseSearchAsync()
    {
        // 让 ComboBox 的选中操作先完成，再弹模态确认，避免控件选择状态与 VM 不一致。
        await Task.Yield();

        var previousMode = _lastStableSearchMode;

        if (ConfirmationRequested is null)
        {
            RevertSearchMode(previousMode, Loc.T("当前无法弹出性能确认，已保持本地搜索"));
            return;
        }

        var request = new ConfirmationRequest
        {
            Title = Loc.T("启用数据库模糊搜索？"),
            Message =
                Loc.F("当前模糊搜索是纯前端过滤，只搜索已加载的 {0:N0} 个 Key，不会访问 Redis。\\n\\n", Keys.Count) +
                Loc.T("启用“数据库模糊搜索”后，将使用 SCAN MATCH 扫描 Redis 数据库；") +
                Loc.T("Key 数量很大时可能耗时较长，并增加服务器压力。\n\n是否继续？"),
            ConfirmText = Loc.T("启用数据库搜索"),
            CancelText = Loc.T("保持本地搜索"),
            IsDanger = true,
        };

        ConfirmationRequested.Invoke(this, request);
        var confirmed = await request.WaitAsync();

        if (SearchMode != SearchModeDatabase) return;

        if (!confirmed)
        {
            RevertSearchMode(previousMode, Loc.T("已取消数据库搜索，继续使用本地过滤"));
            return;
        }

        _lastStableSearchMode = SearchModeDatabase;
        CaptureSearchSnapshot();
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        await RefreshAsync();
    }

    private void RevertSearchMode(string previousMode, string message)
    {
        _suppressSearchModeChange = true;
        try
        {
            SearchMode = previousMode;
        }
        finally
        {
            _suppressSearchModeChange = false;
        }

        _lastStableSearchMode = previousMode;
        if (previousMode == SearchModeLocal)
        {
            if (_searchSnapshot is not null)
            {
                ActivateLocalSearch();
            }
            else
            {
                ApplyFilters();
                UpdateScanStatus();
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        Notify(message);
    }

    private async Task DebouncedSearchAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(320, cts.Token);
            if (!cts.IsCancellationRequested)
                await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            // 输入仍在继续
        }
    }

    partial void OnTypeFilterChanged(string value) => ApplyFilters();
    partial void OnTtlFilterChanged(string value) => ApplyFilters();

    partial void OnIsTreeViewChanged(bool value)
    {
        RebuildTree();
    }

    partial void OnKeySeparatorChanged(string value) => RebuildTree();

    partial void OnShowKeySizeChanged(bool value)
    {
        // 下一次刷新时生效，避免逐项重新请求
    }

    partial void OnSelectedKeyChanged(KeyItemViewModel? value)
    {
        // 联动由 Shell 订阅 PropertyChanged 处理
    }
}

/// <summary>按分隔符构建客户端虚拟目录树。展开/折叠不触发重新扫描。</summary>
public static class KeyTreeBuilder
{
    public static IReadOnlyList<KeyTreeNodeViewModel> Build(
        IEnumerable<KeyItemViewModel> keys, string separator, ISet<string>? expandedFolders = null)
    {
        var sep = string.IsNullOrEmpty(separator) ? ":" : separator;
        var roots = new Dictionary<string, Node>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var segments = key.Name.Split(sep, StringSplitOptions.None);
            if (segments.Length == 0) segments = [key.Name];

            var current = roots;
            var path = string.Empty;
            Node? node = null;

            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.Length == 0) segment = Loc.T("(空)");

                path = path.Length == 0 ? segment : path + sep + segment;
                if (!current.TryGetValue(segment, out node))
                {
                    node = new Node(segment, path);
                    current[segment] = node;
                }

                if (i == segments.Length - 1)
                    node.Key = key;

                current = node.Children;
            }
        }

        return SortNodes(roots.Values)
            .Select(n => ToViewModel(n, expandedFolders))
            .ToList();
    }

    /// <summary>目录节点优先展示，其次才是普通 Key；同级按名称升序。</summary>
    private static IEnumerable<Node> SortNodes(IEnumerable<Node> nodes)
        => nodes
            .OrderBy(n => n.Children.Count == 0 ? 1 : 0)
            .ThenBy(n => n.Name, StringComparer.Ordinal);

    private static KeyTreeNodeViewModel ToViewModel(Node node, ISet<string>? expandedFolders)
    {
        var childNodes = SortNodes(node.Children.Values)
            .Select(n => ToViewModel(n, expandedFolders))
            .ToList();

        var leafCount = node.Key is null ? 0 : 1;
        leafCount += childNodes.Sum(c => c.LeafCount);

        return new KeyTreeNodeViewModel
        {
            Name = node.Name,
            FullPath = node.Path,
            Key = node.Key,
            Children = childNodes,
            LeafCount = leafCount,
            IsExpanded = childNodes.Count > 0 && expandedFolders?.Contains(node.Path) == true,
        };
    }

    private sealed class Node
    {
        public Node(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public string Name { get; }
        public string Path { get; }
        public KeyItemViewModel? Key { get; set; }
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
    }
}
