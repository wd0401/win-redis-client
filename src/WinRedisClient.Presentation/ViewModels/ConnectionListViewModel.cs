using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Models;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

/// <summary>预置常用分组，供分组选择与右键移动时快速挑选。</summary>
public static class ConnectionGroupPresets
{
    public static IReadOnlyList<string> All { get; } = [Loc.T("生产"), Loc.T("预发"), Loc.T("测试"), Loc.T("开发")];
}

/// <summary>左侧连接列表：加载/搜索/排序/持久化与增删改入口；以目录形式按分组折叠展示。</summary>
public partial class ConnectionListViewModel : ObservableObject
{
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ConnectionItemViewModel? _selectedConnection;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = [];
    public ObservableCollection<ConnectionItemViewModel> VisibleConnections { get; } = [];
    public ObservableCollection<ConnectionDirectoryItem> DirectoryItems { get; } = [];

    private readonly Dictionary<string, ConnectionGroupViewModel> _groups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);
    private CancellationTokenSource? _groupStateSaveCts;

    public bool HasConnections => Connections.Count > 0;
    public bool IsEmpty => !IsLoading && Connections.Count == 0;

    public event EventHandler? AddConnectionRequested;
    public event EventHandler<ConnectionItemViewModel>? EditConnectionRequested;
    public event EventHandler<ConnectionItemViewModel>? DeleteConnectionRequested;
    public event EventHandler<ConnectionItemViewModel>? ConnectRequested;
    public event EventHandler<ConnectionItemViewModel>? DisconnectRequested;
    public event EventHandler<string>? CopyConfigRequested;
    public event EventHandler<string>? ErrorRaised;

    public async Task LoadAsync()
    {
        var store = AppServices.ConnectionStore;
        if (store is null)
        {
            ErrorRaised?.Invoke(this, Loc.T("本地连接存储未初始化"));
            return;
        }

        IsLoading = true;
        try
        {
            var profiles = await store.GetAllAsync();
            Connections.Clear();
            foreach (var profile in profiles)
                Connections.Add(ConnectionItemViewModel.FromProfile(profile));

            LoadCollapsedGroupState();
            RebuildVisible();
            SelectedConnection = VisibleConnections.FirstOrDefault();
            OnPropertyChanged(nameof(HasConnections));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke(this, Loc.T("读取连接配置失败：") + ex.Message);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public async Task<ConnectionItemViewModel?> SaveAsync(ConnectionProfile profile)
    {
        var store = AppServices.ConnectionStore;
        if (store is null)
        {
            ErrorRaised?.Invoke(this, Loc.T("本地连接存储未初始化"));
            return null;
        }

        try
        {
            await store.SaveAsync(profile);

            var item = Connections.FirstOrDefault(c => c.Id == profile.Id);
            if (item is null)
            {
                item = ConnectionItemViewModel.FromProfile(profile);
                Connections.Add(item);
            }
            else
            {
                item.ApplyProfile(profile);
                item.LastError = string.Empty;
            }

            RebuildVisible();
            SelectedConnection = item;
            OnPropertyChanged(nameof(HasConnections));
            OnPropertyChanged(nameof(IsEmpty));
            return item;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke(this, Loc.T("保存连接失败：") + ex.Message);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(ConnectionItemViewModel item)
    {
        var store = AppServices.ConnectionStore;
        if (store is null) return false;

        try
        {
            await store.DeleteAsync(item.Id);
            Connections.Remove(item);
            RebuildVisible();

            if (SelectedConnection == item)
                SelectedConnection = VisibleConnections.FirstOrDefault();

            OnPropertyChanged(nameof(HasConnections));
            OnPropertyChanged(nameof(IsEmpty));
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke(this, Loc.T("删除连接配置失败：") + ex.Message);
            return false;
        }
    }

    public void Select(ConnectionItemViewModel? item)
    {
        if (item is null) return;
        SelectedConnection = item;
    }

    /// <summary>快速移动连接分组并持久化；空分组名表示“未分组”。</summary>
    public async Task<bool> MoveToGroupAsync(ConnectionItemViewModel item, string? group)
    {
        var normalized = string.IsNullOrWhiteSpace(group) ? string.Empty : group.Trim();
        if (string.Equals(item.Group, normalized, StringComparison.Ordinal)) return false;

        var previous = item.Group;
        item.Group = normalized;
        var saved = await SaveAsync(item.ToProfile());
        if (saved is not null) return true;

        item.Group = previous;
        RebuildVisible();
        return false;
    }

    [RelayCommand]
    private void AddConnection() => AddConnectionRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void EditConnection(ConnectionItemViewModel? item)
    {
        if (item is not null)
            EditConnectionRequested?.Invoke(this, item);
    }

    [RelayCommand]
    private void RequestDelete(ConnectionItemViewModel? item)
    {
        if (item is not null)
            DeleteConnectionRequested?.Invoke(this, item);
    }

    [RelayCommand]
    private void Connect(ConnectionItemViewModel? item)
    {
        if (item is not null)
            ConnectRequested?.Invoke(this, item);
    }

    [RelayCommand]
    private void Disconnect(ConnectionItemViewModel? item)
    {
        if (item is not null)
            DisconnectRequested?.Invoke(this, item);
    }

    // ── 分组目录（折叠/展开 + 快速选择）────────────────────

    /// <summary>分组快速选择列表：已有分组在前，预置常用分组在后，均去重。</summary>
    public IReadOnlyList<string> GetGroupOptions()
    {
        var options = Connections
            .Select(c => c.Group?.Trim())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(g => g, StringComparer.CurrentCulture)
            .ToList();

        foreach (var preset in ConnectionGroupPresets.All)
        {
            if (!options.Contains(preset, StringComparer.Ordinal))
                options.Add(preset);
        }

        return options;
    }

    /// <summary>切换分组折叠状态并持久化。</summary>
    [RelayCommand]
    private void ToggleGroup(ConnectionGroupViewModel? group)
    {
        if (group is null) return;

        group.IsExpanded = !group.IsExpanded;
        var key = group.RawGroup ?? string.Empty;
        if (group.IsExpanded)
            _collapsedGroups.Remove(key);
        else
            _collapsedGroups.Add(key);

        RebuildVisible();
        SchedulePersistGroupState();
    }

    private void LoadCollapsedGroupState()
    {
        _collapsedGroups.Clear();
        foreach (var name in AppServices.Settings.CollapsedConnectionGroups)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _collapsedGroups.Add(name);
        }
    }

    private void SchedulePersistGroupState()
    {
        _groupStateSaveCts?.Cancel();
        _groupStateSaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _groupStateSaveCts = cts;
        _ = PersistGroupStateAsync(cts);
    }

    private async Task PersistGroupStateAsync(CancellationTokenSource cts)
    {
        var store = AppServices.SettingsStore;
        if (store is null) return;

        try
        {
            await Task.Delay(400, cts.Token);
            if (cts.IsCancellationRequested) return;

            var settings = AppServices.Settings;
            var updated = settings with
            {
                CollapsedConnectionGroups = _collapsedGroups.OrderBy(g => g, StringComparer.Ordinal).ToArray(),
            };
            AppServices.Settings = updated;
            await store.SaveAsync(updated);
        }
        catch (OperationCanceledException)
        {
            // 更晚的折叠状态会再次安排写入
        }
        catch
        {
            // 分组折叠状态持久化失败不影响列表使用
        }
    }

    /// <summary>按分组重建目录列表：搜索时忽略折叠状态展示全部匹配项。</summary>
    private void RebuildDirectory(IReadOnlyList<ConnectionItemViewModel> sorted)
    {
        var isSearching = !string.IsNullOrWhiteSpace(SearchText);

        foreach (var group in _groups.Values)
            group.Items.Clear();

        foreach (var connection in sorted)
        {
            var group = GetOrCreateGroup(connection.Group);
            group.Items.Add(connection);
        }

        DirectoryItems.Clear();
        foreach (var group in _groups.Values
                     .Where(g => g.Items.Count > 0)
                     .OrderBy(g => g.Name, StringComparer.CurrentCulture))
        {
            DirectoryItems.Add(new ConnectionDirectoryItem(group));
            if (!isSearching && !group.IsExpanded)
                continue;

            foreach (var connection in group.Items)
                DirectoryItems.Add(new ConnectionDirectoryItem(group, connection));
        }
    }

    private ConnectionGroupViewModel GetOrCreateGroup(string? rawGroup)
    {
        var key = string.IsNullOrWhiteSpace(rawGroup) ? string.Empty : rawGroup.Trim();
        if (!_groups.TryGetValue(key, out var group))
        {
            group = new ConnectionGroupViewModel(key.Length == 0 ? null : key)
            {
                IsExpanded = !_collapsedGroups.Contains(key),
            };
            _groups[key] = group;
        }

        return group;
    }

    /// <summary>复制连接配置用于分享；按安全要求默认排除所有密码与凭据字段。</summary>
    [RelayCommand]
    private void CopyConfig(ConnectionItemViewModel? item)
    {
        if (item is null) return;

        var payload = new
        {
            name = item.Name,
            group = item.Group,
            warningColor = item.WarningColor.ToString(),
            environment = item.Environment.ToString(),
            host = item.Host,
            port = item.Port,
            database = item.Database,
            username = item.Username,
            useTls = item.UseTls,
            readOnly = item.IsReadOnly,
            autoLoadKeys = item.AutoLoadKeys,
            connectTimeoutSeconds = item.ConnectTimeoutSeconds,
            commandTimeoutSeconds = item.CommandTimeoutSeconds,
            ssh = item.UseSshTunnel
                ? new
                {
                    authMode = item.SshAuthMode.ToString(),
                    host = item.SshHost,
                    port = item.SshPort,
                    username = item.SshUsername,
                    privateKeyPath = item.SshPrivateKeyPath,
                }
                : null,
            notes = item.Notes,
            passwordIncluded = false,
        };

        CopyConfigRequested?.Invoke(this,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    partial void OnSearchTextChanged(string value) => RebuildVisible();

    partial void OnSelectedConnectionChanged(ConnectionItemViewModel? value)
    {
        foreach (var connection in Connections)
            connection.IsActive = connection == value;
    }

    private void RebuildVisible()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? Connections
            : Connections.Where(c =>
                c.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                c.Host.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                c.GroupName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                c.EnvironmentText.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));

        var sorted = filtered.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        VisibleConnections.Clear();
        foreach (var item in sorted)
            VisibleConnections.Add(item);

        RebuildDirectory(sorted);
    }
}
