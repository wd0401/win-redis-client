using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Localization;
using WinRedisClient.Core.Models;
using WinRedisClient.Core.Services;
using WinRedisClient.Presentation.Support;

namespace WinRedisClient.Presentation.ViewModels;

/// <summary>左侧导航项。</summary>
public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(string title, string glyph, string description, object page)
    {
        Title = title;
        Glyph = glyph;
        Description = description;
        Page = page;
    }

    public string Title { get; }
    public string Glyph { get; }
    public string Description { get; }
    public object Page { get; }
}

/// <summary>全局命令面板结果项。</summary>
public sealed record PaletteItemViewModel(
    string Kind,
    string Title,
    string Subtitle,
    Func<Task> Activate);

/// <summary>
/// 应用 Shell：导航、连接生命周期、页面激活、全局搜索、通知与危险操作确认。
/// 所有命令通过 IRedisService 执行；UI 不持有假数据。
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly List<RecentKeyEntry> _recentCache = [];
    private CancellationTokenSource? _heartbeatCts;
    private CancellationTokenSource? _toastCts;
    private int _databaseGeneration;
    private bool _suppressDatabaseChange;
    private bool _heartbeatFailureNotified;
    private bool _diffInitialized;
    private bool _closingConfirmed;
    private object? _previousPage;
    private KeyItemViewModel? _activeDetailKey;
    private bool _suppressDetailSelection;
    private KeyItemViewModel? _declinedSwitchKey;
    private long _declinedSwitchAt;

    [ObservableProperty] private NavigationItemViewModel? _selectedNavigation;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private ConnectionItemViewModel? _activeConnection;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = string.Empty;
    [ObservableProperty] private bool _isSidebarCollapsed;
    [ObservableProperty] private int _databaseIndex;
    [ObservableProperty] private bool _isPaletteOpen;
    [ObservableProperty] private string _paletteQuery = string.Empty;
    [ObservableProperty] private PaletteItemViewModel? _selectedPaletteItem;
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _toastMessage = string.Empty;
    [ObservableProperty] private bool _isToastError;

    public MainWindowViewModel()
    {
        Connections = new ConnectionListViewModel();
        Browser = new KeyBrowserViewModel();
        Detail = new DetailPanelViewModel();
        BrowserPage = new BrowserPageViewModel(Browser, Detail);
        Monitor = new MonitorViewModel();
        Cli = new CliViewModel();
        Diff = new DiffViewModel();
        BigKey = new BigKeyViewModel();
        History = new HistoryViewModel();
        Settings = new SettingsViewModel();

        Navigation =
        [
            new(Loc.T("数据浏览"), "\uE8FD", Loc.T("SCAN 浏览、查看与编辑 Key"), BrowserPage),
            new(Loc.T("实例监控"), "\uE7F4", Loc.T("INFO / SLOWLOG 实时采样"), Monitor),
            new(Loc.T("命令终端"), "\uE756", Loc.T("执行 Redis 命令并查看风险级别"), Cli),
            new(Loc.T("Key 对比"), "\uE8AB", Loc.T("两个独立会话内容差异比较"), Diff),
            new(Loc.T("BigKey 分析"), "\uE9D2", Loc.T("按内存与元素数量识别大 Key"), BigKey),
            new(Loc.T("最近与历史"), "\uE81C", Loc.T("最近访问的 Key 与本地命令历史"), History),
            new(Loc.T("设置"), "\uE713", Loc.T("主题、浏览、监控、历史与安全"), Settings),
        ];

        Monitor.UpdateInterval(AppServices.Settings.MonitorIntervalSeconds);
        Monitor.IsAutoRefresh = AppServices.Settings.AutoRefreshMonitoring;
        IsSidebarCollapsed = AppServices.Settings.SidebarCollapsed;

        WireEvents();
        CurrentPage = BrowserPage;
        SelectedNavigation = Navigation[0];
        _previousPage = BrowserPage;
    }

    public ConnectionListViewModel Connections { get; }
    public KeyBrowserViewModel Browser { get; }
    public DetailPanelViewModel Detail { get; }
    public BrowserPageViewModel BrowserPage { get; }
    public MonitorViewModel Monitor { get; }
    public CliViewModel Cli { get; }
    public DiffViewModel Diff { get; }
    public BigKeyViewModel BigKey { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    public IReadOnlyList<NavigationItemViewModel> Navigation { get; }
    public ObservableCollection<PaletteItemViewModel> PaletteResults { get; } = [];
    public IReadOnlyList<int> DatabaseOptions { get; } = Enumerable.Range(0, 16).ToList();
    public ObservableCollection<DatabaseEntryViewModel> DatabaseEntries { get; } =
        new(Enumerable.Range(0, 16).Select(index => new DatabaseEntryViewModel(index)));

    private bool _databaseCountsLoading;
    private bool _databaseCountsLoaded;
    public IReadOnlyList<int> HistoryLimitOptions { get; } = [50, 100, 200, 500, 1000, 2000];

    public bool HasActiveConnection => ActiveConnection is not null;
    public bool IsBrowserPage => SelectedNavigation?.Page is BrowserPageViewModel;
    public bool IsProduction => ActiveConnection?.IsProduction ?? false;
    public bool IsReadOnlyConnection => ActiveConnection?.IsReadOnly ?? false;
    public bool HasUnsavedChanges => Detail.IsDirty;
    public bool IsPaletteEmpty => PaletteResults.Count == 0;
    public double SidebarWidth => IsSidebarCollapsed ? 76 : 228;

    public string ConnectionSummary => ActiveConnection is null
        ? Loc.T("未连接 · 从左侧选择连接并点击连接")
        : $"{ActiveConnection.Name} · {ActiveConnection.EndpointText}";

    public string ActiveConnectionName => ActiveConnection?.Name ?? Loc.T("未连接");
    public string ActiveConnectionEndpoint => ActiveConnection?.EndpointText ?? Loc.T("从左侧选择连接并点击连接");

    public string DatabaseContextText => IsConnected ? $"DB{DatabaseIndex}" : "DB—";

    // ── 事件（由 App 层实现 UI 细节） ───────────────────────
    public event EventHandler<ConfirmationRequest>? ConfirmationRequested;
    public event EventHandler<string>? CopyRequested;
    public event EventHandler<ExportRequest>? ExportRequested;
    public event EventHandler? AddConnectionDialogRequested;
    public event EventHandler<ConnectionItemViewModel>? EditConnectionDialogRequested;
    public event EventHandler? AddKeyDialogRequested;
    public event EventHandler<KeyItemViewModel>? RenameKeyDialogRequested;
    public event EventHandler<KeyItemViewModel>? SetTtlDialogRequested;
    public event EventHandler<ThemePreference>? ThemeChangeRequested;
    public event EventHandler<AppLanguage>? LanguageChangeRequested;
    public event EventHandler<double>? FontScaleChangeRequested;
    public event EventHandler? OpenDataFolderRequested;

    private void WireEvents()
    {
        Connections.AddConnectionRequested += (_, _) => AddConnectionDialogRequested?.Invoke(this, EventArgs.Empty);
        Connections.EditConnectionRequested += (_, item) => EditConnectionDialogRequested?.Invoke(this, item);
        Connections.DeleteConnectionRequested += async (_, item) => await DeleteConnectionWithConfirmAsync(item);
        Connections.ConnectRequested += async (_, item) => await ConnectAsync(item);
        Connections.DisconnectRequested += async (_, item) => await DisconnectAsync(item);
        Connections.CopyConfigRequested += (_, text) => CopyRequested?.Invoke(this, text);
        Connections.ErrorRaised += (_, message) => ShowToast(message, true);

        Browser.NotificationRequested += (_, message) => ShowToast(message);
        Browser.ConfirmationRequested += (_, request) => ForwardConfirmation(request);
        Browser.CopyRequested += (_, text) => CopyRequested?.Invoke(this, text);
        Browser.AddKeyRequested += (_, _) => AddKeyDialogRequested?.Invoke(this, EventArgs.Empty);
        Browser.RenameKeyRequested += (_, item) => RenameKeyDialogRequested?.Invoke(this, item);
        Browser.SetTtlRequested += (_, item) => SetTtlDialogRequested?.Invoke(this, item);
        Browser.DeleteKeyRequested += async (_, item) => await DeleteKeyWithConfirmAsync(item);
        Browser.PropertyChanged += OnBrowserPropertyChanged;

        Detail.NotificationRequested += (_, message) => ShowToast(message);
        Detail.ConfirmationRequested += (_, request) => ForwardConfirmation(request);
        Detail.CopyRequested += (_, text) => CopyRequested?.Invoke(this, text);
        Detail.RenameKeyRequested += (_, key) => RequestRenameKey(key);
        Detail.SetTtlRequested += (_, key) => RequestSetTtl(key);
        Detail.DeleteKeyRequested += async (_, key) => await RequestDeleteKeyAsync(key);
        Detail.KeyChanged += async (_, key) => await Browser.RefreshItemMetadataAsync(key);
        Detail.KeyMissing += (_, key) =>
        {
            ShowToast(Loc.F("Key“{0}”不存在或已过期", key), true);
            _ = Browser.RefreshItemMetadataAsync(key);
        };
        Detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DetailPanelViewModel.IsDirty))
                OnPropertyChanged(nameof(HasUnsavedChanges));
        };

        Monitor.NotificationRequested += (_, message) => ShowToast(message);

        Cli.NotificationRequested += (_, message) => ShowToast(message);
        Cli.ConfirmationRequested += (_, request) => ForwardConfirmation(request);
        Cli.CopyRequested += (_, text) => CopyRequested?.Invoke(this, text);

        Diff.NotificationRequested += (_, message) => ShowToast(message);
        Diff.CopyRequested += (_, text) => CopyRequested?.Invoke(this, text);
        Diff.ExportRequested += (_, request) => ExportRequested?.Invoke(this, request);

        BigKey.NotificationRequested += (_, message) => ShowToast(message);
        BigKey.ExportRequested += (_, request) => ExportRequested?.Invoke(this, request);
        BigKey.OpenKeyRequested += async (_, key) => await OpenKeyByNameAsync(key);

        History.NotificationRequested += (_, message) => ShowToast(message);
        History.CopyRequested += (_, text) => CopyRequested?.Invoke(this, text);
        History.OpenKeyRequested += async (_, entry) => await OpenRecentKeyAsync(entry);
        History.UseCommandRequested += (_, command) => UseCommandFromHistory(command);

        Settings.SettingsSaved += (_, settings) => ApplySettings(settings);
        Settings.ThemeChangeRequested += (_, theme) => ThemeChangeRequested?.Invoke(this, theme);
        Settings.LanguageChangeRequested += (_, language) => LanguageChangeRequested?.Invoke(this, language);
        Settings.FontScaleChangeRequested += (_, scale) => FontScaleChangeRequested?.Invoke(this, scale);
        Settings.ExportRequested += (_, request) => ExportRequested?.Invoke(this, request);
        Settings.HistoryCleared += (_, _) =>
        {
            _recentCache.Clear();
            _ = History.LoadAsync();
        };
        Settings.OpenDataFolderRequested += (_, _) => OpenDataFolderRequested?.Invoke(this, EventArgs.Empty);
        Settings.NotificationRequested += (_, message) => ShowToast(message);
        Settings.ConfirmationRequested += (_, request) => ForwardConfirmation(request);
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        BusyText = Loc.T("正在读取本地连接配置…");
        try
        {
            await Connections.LoadAsync();
            await LoadRecentCacheAsync();
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("初始化失败：") + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── 连接生命周期 ───────────────────────────────────────

    public async Task ConnectAsync(ConnectionItemViewModel item)
    {
        if (IsBusy) return;
        if (item.IsConnected && ActiveConnection?.Id == item.Id) return;

        if (!await ConfirmDiscardDirtyAsync()) return;
        await DisconnectCoreAsync(silent: true);

        var redis = AppServices.Redis;
        if (redis is null)
        {
            ShowToast(Loc.T("Redis 服务未初始化"), true);
            return;
        }

        IsBusy = true;
        BusyText = Loc.F("正在连接 {0}…", item.Name);
        item.State = ConnectionState.Connecting;
        item.LastError = string.Empty;
        item.LatencyText = "—";
        _heartbeatFailureNotified = false;

        try
        {
            var profile = item.ToProfile();
            await redis.ConnectAsync(profile);

            var versionTask = redis.GetServerVersionAsync();
            var sizeTask = redis.GetDatabaseSizeAsync();
            var pingTask = redis.PingAsync();
            await Task.WhenAll(versionTask, sizeTask, pingTask);

            item.RedisVersion = versionTask.Result ?? string.Empty;
            item.KeyCount = sizeTask.Result;
            item.LatencyText = $"{pingTask.Result.TotalMilliseconds:0} ms";
            item.State = ConnectionState.Connected;

            ActiveConnection = item;
            IsConnected = true;
            _suppressDatabaseChange = true;
            DatabaseIndex = Math.Clamp(profile.Database, 0, 15);
            _suppressDatabaseChange = false;
            ResetDatabaseEntries();
            DatabaseEntries[DatabaseIndex].SetCount(item.KeyCount);

            ConfigureFeatureViewModels(profile);
            Browser.Reset();
            Cli.Configure(true, profile.Name, profile.Database, profile.Id);
            Monitor.IsConnected = true;
            BigKey.Activate();
            await Detail.LoadAsync(null);

            await Browser.RefreshCommand.ExecuteAsync(null);
            if (profile.AutoLoadKeys)
                _ = Browser.AutoLoadAsync();
            StartHeartbeat();

            UpdateConnectionComputed();
            ShowToast(Loc.F("已连接 {0} · Redis {1} · {2}", profile.Name, item.RedisVersion, item.LatencyText));
        }
        catch (Exception ex)
        {
            item.State = ConnectionState.Failed;
            item.LastError = UiText.Redact(ex.Message, item.Password, item.SshPassword);
            item.LatencyText = "—";
            ActiveConnection = null;
            IsConnected = false;
            ConfigureDisconnectedState();
            ShowToast(Loc.F("连接失败：{0}", item.LastError), true);
        }
        finally
        {
            IsBusy = false;
            UpdateConnectionComputed();
        }
    }

    public async Task DisconnectAsync(ConnectionItemViewModel? item = null)
    {
        if (item is not null && ActiveConnection is not null && item.Id != ActiveConnection.Id)
            return;

        if (!await ConfirmDiscardDirtyAsync()) return;
        await DisconnectCoreAsync(silent: false);
    }

    private async Task DisconnectCoreAsync(bool silent)
    {
        StopHeartbeat();
        var active = ActiveConnection;

        try
        {
            if (AppServices.Redis is not null)
                await AppServices.Redis.DisconnectAsync();
        }
        catch
        {
            // 释放失败不阻断 UI
        }

        if (active is not null)
        {
            active.ResetRuntimeState();
            active.State = ConnectionState.Disconnected;
        }

        ActiveConnection = null;
        IsConnected = false;
        _suppressDatabaseChange = true;
        DatabaseIndex = 0;
        _suppressDatabaseChange = false;
        ConfigureDisconnectedState();
        UpdateConnectionComputed();

        if (!silent)
            ShowToast(Loc.T("已断开连接"));
    }

    private void ConfigureFeatureViewModels(ConnectionProfile profile)
    {
        var settings = AppServices.Settings;
        Browser.Configure(true, profile.Name, profile.Database, profile.ReadOnly,
            settings.KeySeparator, settings.UseDirectoryView, settings.ShowKeySize, profile.AutoLoadKeys, profile.Id);
        Monitor.UpdateInterval(settings.MonitorIntervalSeconds);
        Monitor.IsAutoRefresh = settings.AutoRefreshMonitoring;
        Monitor.IsConnected = true;
    }

    private void ConfigureDisconnectedState()
    {
        ResetDatabaseEntries();
        Browser.Configure(false, string.Empty, 0, false,
            AppServices.Settings.KeySeparator, AppServices.Settings.UseDirectoryView,
            AppServices.Settings.ShowKeySize);
        Browser.Reset();
        _ = Detail.LoadAsync(null);
        Cli.Configure(false, string.Empty, 0, Guid.Empty);
        Monitor.Deactivate();
        Monitor.IsConnected = false;
        BigKey.Activate();
    }

    private void UpdateConnectionComputed()
    {
        OnPropertyChanged(nameof(HasActiveConnection));
        OnPropertyChanged(nameof(IsProduction));
        OnPropertyChanged(nameof(IsReadOnlyConnection));
        OnPropertyChanged(nameof(ConnectionSummary));
        OnPropertyChanged(nameof(ActiveConnectionName));
        OnPropertyChanged(nameof(ActiveConnectionEndpoint));
        OnPropertyChanged(nameof(DatabaseContextText));
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        var cts = new CancellationTokenSource();
        _heartbeatCts = cts;
        _ = HeartbeatLoopAsync(cts);
    }

    private void StopHeartbeat()
    {
        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;
    }

    private async Task HeartbeatLoopAsync(CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                if (token.IsCancellationRequested) break;
                if (!IsConnected || ActiveConnection is null) break;

                var redis = AppServices.Redis;
                if (redis is not { IsConnected: true })
                {
                    HandleHeartbeatFailure(Loc.T("连接已断开"));
                    break;
                }

                try
                {
                    var ping = await redis.PingAsync(token);
                    var size = await redis.GetDatabaseSizeAsync(token);
                    ActiveConnection.LatencyText = $"{ping.TotalMilliseconds:0} ms";
                    ActiveConnection.KeyCount = size;
                    DatabaseEntries[Math.Clamp(DatabaseIndex, 0, 15)].SetCount(size);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    HandleHeartbeatFailure(UiText.Redact(ex.Message,
                        ActiveConnection?.Password, ActiveConnection?.SshPassword));
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
    }

    private void HandleHeartbeatFailure(string message)
    {
        if (IsConnected)
        {
            IsConnected = false;
            if (ActiveConnection is not null)
            {
                ActiveConnection.State = ConnectionState.Failed;
                ActiveConnection.LastError = message;
                ActiveConnection.LatencyText = "—";
            }
        }

        if (!_heartbeatFailureNotified)
        {
            _heartbeatFailureNotified = true;
            ShowToast(Loc.T("与 Redis 的连接已断开：") + message, true);
        }

        UpdateConnectionComputed();
    }

    partial void OnDatabaseIndexChanged(int value)
    {
        if (_suppressDatabaseChange) return;
        if (!IsConnected) return;
        _ = SwitchDatabaseAsync(value);
    }

    private async Task SwitchDatabaseAsync(int database)
    {
        var generation = Interlocked.Increment(ref _databaseGeneration);
        var redis = AppServices.Redis;
        if (redis is null || !IsConnected) return;

        if (!await ConfirmDiscardDirtyAsync())
        {
            RevertDatabaseIndex(redis.Database);
            return;
        }

        if (!redis.SelectDatabase(database))
        {
            ShowToast(Loc.F("无法切换到 DB{0}", database), true);
            RevertDatabaseIndex(redis.Database);
            return;
        }

        if (generation != _databaseGeneration) return;

        try
        {
            var size = await redis.GetDatabaseSizeAsync();
            if (ActiveConnection is not null)
                ActiveConnection.KeyCount = size;
            DatabaseEntries[Math.Clamp(database, 0, 15)].SetCount(size);
        }
        catch
        {
            // DBSIZE 失败不阻断切换
        }

        if (generation != _databaseGeneration) return;

        Browser.UpdateDatabase(database);
        Browser.Reset();
        await Detail.LoadAsync(null);
        Cli.Configure(true, ActiveConnection?.Name ?? string.Empty, database,
            ActiveConnection?.Id ?? Guid.Empty);
        BigKey.ResetResults();
        OnPropertyChanged(nameof(DatabaseContextText));

        if (CurrentPage is MonitorViewModel)
            await Monitor.RefreshAsync(includeSlowLog: false);

        await Browser.RefreshCommand.ExecuteAsync(null);
        if (ActiveConnection?.AutoLoadKeys == true)
            _ = Browser.AutoLoadAsync();
        ShowToast(Loc.F("已切换到 DB{0}", database));
    }

    /// <summary>打开数据库下拉时刷新各 DB 的 Key 数量（INFO keyspace 一次读取）。</summary>
    public async Task RefreshDatabaseKeyCountsAsync()
    {
        if (!IsConnected || AppServices.Redis is null)
        {
            ResetDatabaseEntries();
            return;
        }

        if (_databaseCountsLoading) return;
        _databaseCountsLoading = true;

        if (!_databaseCountsLoaded)
        {
            foreach (var entry in DatabaseEntries)
                entry.SetLoading();
        }

        try
        {
            var counts = await AppServices.Redis.GetDatabaseKeyCountsAsync();
            foreach (var entry in DatabaseEntries)
                entry.SetCount(counts.TryGetValue(entry.Number, out var count) ? count : 0);
            _databaseCountsLoaded = true;
        }
        catch
        {
            // 数量只是辅助信息，读取失败不打断 DB 切换；下次打开下拉时重试。
            foreach (var entry in DatabaseEntries)
                entry.SetCount(null);
            _databaseCountsLoaded = false;
        }
        finally
        {
            _databaseCountsLoading = false;
        }
    }

    private void ResetDatabaseEntries()
    {
        _databaseCountsLoaded = false;
        foreach (var entry in DatabaseEntries)
            entry.SetCount(null);
    }

    private void RevertDatabaseIndex(int actual)
    {
        _suppressDatabaseChange = true;
        DatabaseIndex = Math.Clamp(actual, 0, 15);
        _suppressDatabaseChange = false;
    }

    // ── 导航与页面 ────────────────────────────────────────

    partial void OnSelectedNavigationChanged(NavigationItemViewModel? value)
    {
        if (value is null) return;

        if (_previousPage is MonitorViewModel && !ReferenceEquals(value.Page, Monitor))
            Monitor.Deactivate();

        CurrentPage = value.Page;
        _previousPage = value.Page;
        OnPropertyChanged(nameof(IsBrowserPage));
        _ = ActivatePageAsync(value);
    }

    private async Task ActivatePageAsync(NavigationItemViewModel item)
    {
        try
        {
            switch (item.Page)
            {
                case MonitorViewModel:
                    Monitor.IsConnected = IsConnected;
                    await Monitor.ActivateAsync();
                    break;
                case CliViewModel:
                    await Cli.ActivateAsync();
                    break;
                case DiffViewModel:
                    if (!_diffInitialized)
                    {
                        _diffInitialized = true;
                        await Diff.PrefillAsync(ActiveConnection?.ToProfile(), Browser.SelectedKey?.Name);
                    }
                    else
                    {
                        await Diff.LoadConnectionsAsync();
                    }
                    break;
                case BigKeyViewModel:
                    BigKey.Activate();
                    break;
                case HistoryViewModel:
                    await History.ActivateAsync();
                    break;
                case SettingsViewModel:
                    Settings.LoadFrom(AppServices.Settings);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("打开页面失败：") + ex.Message, true);
        }
    }

    private void NavigateToPage(object page)
    {
        var item = Navigation.FirstOrDefault(n => ReferenceEquals(n.Page, page));
        if (item is not null)
            SelectedNavigation = item;
    }

    [RelayCommand]
    private void CopyEndpoint()
    {
        var endpoint = ActiveConnection?.EndpointText;
        if (!string.IsNullOrWhiteSpace(endpoint))
            CopyRequested?.Invoke(this, endpoint);
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        OnPropertyChanged(nameof(SidebarWidth));
        _ = PersistSidebarStateAsync();
    }

    private async Task PersistSidebarStateAsync()
    {
        var store = AppServices.SettingsStore;
        if (store is null) return;
        try
        {
            var updated = AppServices.Settings with { SidebarCollapsed = IsSidebarCollapsed };
            AppServices.Settings = updated;
            await store.SaveAsync(updated);
        }
        catch
        {
            // 界面状态持久化失败不影响使用
        }
    }

    [RelayCommand]
    private async Task RefreshCurrentPage()
    {
        if (!IsConnected)
        {
            ShowToast(Loc.T("未连接 Redis"), true);
            return;
        }

        switch (CurrentPage)
        {
            case BrowserPageViewModel:
                await Browser.RefreshCommand.ExecuteAsync(null);
                if (Browser.SelectedKey is not null)
                    await Detail.ReloadAsync();
                break;
            case MonitorViewModel:
                await Monitor.RefreshAsync(includeSlowLog: true);
                break;
            case CliViewModel:
                await Cli.LoadHistoryAsync();
                break;
            case BigKeyViewModel:
                await BigKey.RefreshCommand.ExecuteAsync(null);
                break;
            case HistoryViewModel:
                await History.LoadAsync();
                break;
            case DiffViewModel:
                ShowToast(Loc.T("Key 对比请点击“开始比较”刷新结果"));
                break;
        }
    }

    [RelayCommand]
    private void DismissToast()
    {
        _toastCts?.Cancel();
        IsToastVisible = false;
    }

    // ── Key 操作（浏览器 / 详情共用确认路径） ───────────────

    private KeyItemViewModel? FindLoadedKey(string key)
        => Browser.Keys.FirstOrDefault(k => string.Equals(k.Name, key, StringComparison.Ordinal));

    private void RequestRenameKey(string key)
    {
        var item = FindLoadedKey(key);
        if (item is null)
        {
            ShowToast(Loc.F("当前列表中没有 Key“{0}”，请先刷新", key), true);
            return;
        }

        RenameKeyDialogRequested?.Invoke(this, item);
    }

    private void RequestSetTtl(string key)
    {
        var item = FindLoadedKey(key);
        if (item is null)
        {
            ShowToast(Loc.F("当前列表中没有 Key“{0}”，请先刷新", key), true);
            return;
        }

        SetTtlDialogRequested?.Invoke(this, item);
    }

    private async Task RequestDeleteKeyAsync(string key)
    {
        var item = FindLoadedKey(key);
        if (item is null)
        {
            ShowToast(Loc.F("当前列表中没有 Key“{0}”，请先刷新", key), true);
            return;
        }

        await DeleteKeyWithConfirmAsync(item);
    }

    public async Task DeleteKeyWithConfirmAsync(KeyItemViewModel item)
    {
        if (!IsConnected) return;

        var production = IsProduction;
        var confirmed = await ConfirmAsync(new ConfirmationRequest
        {
            Title = Loc.T("删除 Key 确认"),
            Message = Loc.F("即将删除 Key：\\n{0}\\n\\n", item.Name) +
                      Loc.F("连接：{0} · DB{1}\\n", ActiveConnection?.Name, DatabaseIndex) +
                      Loc.T("删除立即生效，且不可自动撤销。"),
            ConfirmText = Loc.T("删除 Key"),
            CancelText = Loc.T("取消"),
            IsDanger = true,
            RequiredText = production ? item.Name : null,
        });

        if (!confirmed) return;

        var result = await Browser.DeleteAsync(item, confirmed: true);
        if (!result.IsSuccess)
        {
            ShowToast(Loc.T("删除失败：") + DescribeFailure(result), true);
            return;
        }

        if (ReferenceEquals(Browser.SelectedKey, item) || Browser.SelectedKey is null)
            await Detail.LoadAsync(null);

        ShowToast(Loc.F("已删除 Key：{0}", item.Name));
    }

    public async Task RenameKeyAsync(KeyItemViewModel item, string newName)
    {
        if (!IsConnected) return;
        if (string.IsNullOrWhiteSpace(newName))
        {
            ShowToast(Loc.T("新 Key 名称不能为空"), true);
            return;
        }

        var confirmed = false;
        if (IsProduction)
        {
            confirmed = await ConfirmAsync(new ConfirmationRequest
            {
                Title = Loc.T("生产环境重命名确认"),
                Message = Loc.F("即将在生产环境将 Key“{0}”重命名为“{1}”。\\n", item.Name, newName) +
                          Loc.T("若目标已存在，操作会被拒绝。"),
                ConfirmText = Loc.T("确认重命名"),
                CancelText = Loc.T("取消"),
                IsDanger = true,
                RequiredText = item.Name,
            });
            if (!confirmed) return;
        }

        var result = await Browser.RenameAsync(item, newName, confirmed);
        if (!result.IsSuccess)
        {
            ShowToast(Loc.T("重命名失败：") + DescribeFailure(result), true);
            return;
        }

        await Detail.LoadAsync(Browser.SelectedKey, force: true);
        ShowToast(Loc.F("已重命名为：{0}", newName));
    }

    public async Task SetTtlAsync(KeyItemViewModel item, TimeSpan? ttl)
    {
        if (!IsConnected) return;

        var result = await Browser.SetTtlAsync(item, ttl);
        if (!result.IsSuccess)
        {
            ShowToast(Loc.T("设置 TTL 失败：") + DescribeFailure(result), true);
            return;
        }

        if (ReferenceEquals(Browser.SelectedKey, item))
            await Detail.LoadAsync(item, force: true);

        ShowToast(ttl is null
            ? Loc.F("已清除 {0} 的过期时间", item.Name)
            : Loc.F("已设置 TTL：{0}", TtlFormatter.Format(ttl)));
    }

    public async Task CreateKeyAsync(string name, string type, IReadOnlyList<string> values,
        TimeSpan? ttl, bool allowOverwrite)
    {
        if (!IsConnected)
        {
            ShowToast(Loc.T("未连接 Redis"), true);
            return;
        }

        var redis = AppServices.Redis;
        if (redis is null) return;

        name = name.Trim();
        if (name.Length == 0)
        {
            ShowToast(Loc.T("Key 名称不能为空"), true);
            return;
        }

        try
        {
            var exists = await redis.KeyExistsAsync(name);
            var confirmed = false;

            if (exists && !allowOverwrite)
            {
                ShowToast(Loc.F("Key“{0}”已经存在，请修改名称或选择覆盖", name), true);
                return;
            }

            if (exists)
            {
                var overwriteConfirmed = await ConfirmAsync(new ConfirmationRequest
                {
                    Title = Loc.T("覆盖已存在的 Key"),
                    Message = Loc.F("Key“{0}”已经存在于 {1} · DB{2}。\\n", name, ActiveConnection?.Name, DatabaseIndex) +
                              Loc.T("继续将删除原 Key 并创建新内容，原数据不可恢复。"),
                    ConfirmText = Loc.T("覆盖创建"),
                    CancelText = Loc.T("取消"),
                    IsDanger = true,
                    RequiredText = IsProduction ? name : null,
                });
                if (!overwriteConfirmed) return;
                confirmed = true;
            }

            var (result, _) = await Browser.CreateAsync(name, type, values, ttl, allowOverwrite, confirmed);
            if (!result.IsSuccess)
            {
                ShowToast(Loc.T("创建失败：") + DescribeFailure(result), true);
                return;
            }

            await Detail.LoadAsync(Browser.SelectedKey, force: true);
            ShowToast(Loc.F("已创建 {0} Key：{1}", type, name));
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("创建失败：") + UiText.Redact(ex.Message, redis.CurrentProfile?.Password), true);
        }
    }

    public async Task SaveConnectionAsync(ConnectionProfile profile)
    {
        var item = await Connections.SaveAsync(profile);
        if (item is null) return;

        if (ActiveConnection?.Id == profile.Id)
            ShowToast(Loc.T("连接配置已保存；重新连接后新配置生效"));
        else
            ShowToast(Loc.F("已保存连接：{0}", profile.Name));
    }

    public async Task DeleteConnectionWithConfirmAsync(ConnectionItemViewModel item)
    {
        var message = Loc.F("将删除本地连接配置“{0}”（{1}）。\\n", item.Name, item.EndpointText) +
                      Loc.T("这不会删除 Redis 服务端的数据。");
        if (ActiveConnection?.Id == item.Id)
            message += Loc.T("\n\n该连接当前处于已连接状态，删除后将断开连接。");

        var confirmed = await ConfirmAsync(new ConfirmationRequest
        {
            Title = Loc.T("删除连接配置"),
            Message = message,
            ConfirmText = Loc.T("删除连接"),
            CancelText = Loc.T("取消"),
            IsDanger = true,
        });
        if (!confirmed) return;

        if (ActiveConnection?.Id == item.Id)
            await DisconnectCoreAsync(silent: true);

        var deleted = await Connections.DeleteAsync(item);
        if (deleted)
            ShowToast(Loc.F("已删除连接：{0}", item.Name));
    }

    // ── 最近访问 / 全局搜索 ────────────────────────────────

    private async Task LoadRecentCacheAsync()
    {
        var store = AppServices.HistoryStore;
        if (store is null) return;
        try
        {
            var recent = await store.GetRecentAsync();
            _recentCache.Clear();
            _recentCache.AddRange(recent);
        }
        catch
        {
            // 历史不可用时仅影响搜索建议
        }
    }

    private async Task HandleSelectedKeyAsync(KeyItemViewModel? key)
    {
        // 详情已显示该 Key 时不重新加载，避免点击当前 Key 覆盖未保存草稿。
        if (key is not null && ReferenceEquals(key, _activeDetailKey))
            return;

        // 关闭确认弹窗后，WPF 会把焦点还给原 TreeViewItem 并再次触发选中事件；
        // 对刚刚被拒绝的同一个 Key 静默忽略，避免重复弹窗与循环。
        if (key is not null && ReferenceEquals(key, _declinedSwitchKey) &&
            Environment.TickCount64 - _declinedSwitchAt < 1500)
        {
            return;
        }

        // 切换到另一个 Key 前保护未保存草稿；清空选择（断开、删除）由显式操作自行确认。
        if (key is not null && Detail.IsDirty)
        {
            var confirmed = await ConfirmDiscardDirtyAsync();
            if (!confirmed)
            {
                _declinedSwitchKey = key;
                _declinedSwitchAt = Environment.TickCount64;
                ShowToast(Loc.T("已取消切换，未保存的修改仍在编辑器中；点击原 Key 可继续编辑"));
                return;
            }
        }

        _declinedSwitchKey = null;
        _suppressDetailSelection = true;
        try
        {
            await Detail.LoadAsync(key);
            _activeDetailKey = key;
        }
        finally
        {
            _suppressDetailSelection = false;
        }

        if (key is null || !IsConnected) return;
        if (!ReferenceEquals(Browser.SelectedKey, key)) return;

        var settings = AppServices.Settings;
        if (!settings.RecentKeysEnabled || AppServices.HistoryStore is null) return;

        var redis = AppServices.Redis;
        var profile = redis?.CurrentProfile;
        if (redis is null || profile is null) return;

        var entry = new RecentKeyEntry(profile.Id, profile.Name, redis.Database, key.Name, DateTimeOffset.Now);
        _recentCache.RemoveAll(r => string.Equals(r.Key, key.Name, StringComparison.Ordinal) &&
                                    r.ConnectionId == profile.Id && r.Database == redis.Database);
        _recentCache.Insert(0, entry);
        while (_recentCache.Count > 100) _recentCache.RemoveAt(_recentCache.Count - 1);

        try
        {
            await AppServices.HistoryStore.AddRecentAsync(entry);
        }
        catch
        {
            // 历史写入失败不阻断浏览
        }
    }

    private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyBrowserViewModel.SelectedKey))
        {
            if (_suppressDetailSelection) return;
            _ = HandleSelectedKeyAsync(Browser.SelectedKey);
        }
    }

    private async Task OpenRecentKeyAsync(RecentKeyEntry entry)
    {
        var redis = AppServices.Redis;
        if (redis is not { IsConnected: true })
        {
            ShowToast(Loc.T("请先连接 Redis"), true);
            return;
        }

        var profile = redis.CurrentProfile;
        if (profile is null || profile.Id != entry.ConnectionId)
        {
            ShowToast(Loc.F("该记录来自连接“{0}”，请先连接对应实例", entry.ConnectionName), true);
            return;
        }

        NavigateToPage(BrowserPage);

        if (redis.Database != entry.Database)
        {
            _suppressDatabaseChange = true;
            DatabaseIndex = entry.Database;
            _suppressDatabaseChange = false;
            await SwitchDatabaseAsync(entry.Database);
            if (redis.Database != entry.Database) return;
        }

        var opened = await Browser.OpenKeyAsync(entry.Key);
        if (!opened)
            ShowToast(Loc.F("Key“{0}”不存在或已过期", entry.Key), true);
    }

    private void UseCommandFromHistory(string command)
    {
        NavigateToPage(Cli);
        Cli.Input = command;
    }

    public async Task OpenKeyByNameAsync(string key)
    {
        if (!IsConnected)
        {
            ShowToast(Loc.T("请先连接 Redis"), true);
            return;
        }

        NavigateToPage(BrowserPage);
        var opened = await Browser.OpenKeyAsync(key);
        if (!opened)
            ShowToast(Loc.F("Key“{0}”不存在或已过期", key), true);
    }

    [RelayCommand]
    private async Task OpenPalette()
    {
        IsPaletteOpen = true;
        PaletteQuery = string.Empty;
        RebuildPalette();

        try
        {
            await History.LoadAsync();
            await LoadRecentCacheAsync();
        }
        catch
        {
            // 建议数据不可用时仍可搜索页面与已加载 Key
        }

        RebuildPalette();
    }

    [RelayCommand]
    private void ClosePalette() => IsPaletteOpen = false;

    [RelayCommand]
    private async Task ExecutePaletteItem()
    {
        var item = SelectedPaletteItem;
        if (item is null) return;

        IsPaletteOpen = false;
        try
        {
            await item.Activate();
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("执行失败：") + ex.Message, true);
        }
    }

    public void MovePaletteSelection(int delta)
    {
        if (PaletteResults.Count == 0) return;
        var index = SelectedPaletteItem is null ? -1 : PaletteResults.IndexOf(SelectedPaletteItem);
        index = Math.Clamp(index + delta, 0, PaletteResults.Count - 1);
        SelectedPaletteItem = PaletteResults[index];
    }

    /// <summary>由 View 层展示操作反馈（复制、导出等）。</summary>
    public void Notify(string message, bool isError = false) => ShowToast(message, isError);

    partial void OnPaletteQueryChanged(string value) => RebuildPalette();

    private void RebuildPalette()
    {
        var results = new List<PaletteItemViewModel>();
        var query = PaletteQuery.Trim();

        if (query.Length == 0)
        {
            foreach (var nav in Navigation)
                results.Add(PageItem(nav));

            foreach (var recent in _recentCache.Take(5))
                results.Add(RecentItem(recent));
        }
        else
        {
            foreach (var nav in Navigation.Where(n =>
                         n.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                         n.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                results.Add(PageItem(nav));

            foreach (var key in Browser.VisibleKeys
                         .Where(k => k.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                         .Take(15))
            {
                var target = key;
                results.Add(new PaletteItemViewModel(
                    "Key",
                    target.Name,
                    $"{target.TypeDisplay} · TTL {target.TtlText} · {target.SizeText}",
                    async () => await OpenKeyByNameAsync(target.Name)));
            }

            foreach (var recent in _recentCache
                         .Where(r => r.Key.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                         .Take(5))
                results.Add(RecentItem(recent));

            foreach (var entry in History.Commands
                         .Where(c => c.Command.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                         .Take(5))
            {
                var target = entry;
                results.Add(new PaletteItemViewModel(
                    Loc.T("命令"),
                    target.CommandPreview,
                    target.TimeText,
                    () =>
                    {
                        UseCommandFromHistory(target.Command);
                        return Task.CompletedTask;
                    }));
            }
        }

        PaletteResults.Clear();
        foreach (var item in results) PaletteResults.Add(item);

        SelectedPaletteItem = PaletteResults.FirstOrDefault();
        OnPropertyChanged(nameof(IsPaletteEmpty));
    }

    private PaletteItemViewModel PageItem(NavigationItemViewModel nav)
        => new(Loc.T("页面"), nav.Title, nav.Description, () =>
        {
            SelectedNavigation = nav;
            return Task.CompletedTask;
        });

    private PaletteItemViewModel RecentItem(RecentKeyEntry entry)
        => new(Loc.T("最近"), entry.Key, $"{entry.ConnectionName} · DB{entry.Database} · {entry.LastAccessed.LocalDateTime:MM-dd HH:mm}",
            async () => await OpenRecentKeyAsync(entry));

    // ── 设置与通知 ────────────────────────────────────────

    private void ApplySettings(AppSettings settings)
    {
        AppServices.Settings = settings;
        Browser.Configure(IsConnected, ActiveConnection?.Name ?? string.Empty, DatabaseIndex,
            IsReadOnlyConnection, settings.KeySeparator, settings.UseDirectoryView, settings.ShowKeySize);
        Monitor.UpdateInterval(settings.MonitorIntervalSeconds);
        Monitor.IsAutoRefresh = settings.AutoRefreshMonitoring && IsConnected;
        FontScaleChangeRequested?.Invoke(this, settings.FontScale);
        OnPropertyChanged(nameof(SidebarWidth));
    }

    private void ForwardConfirmation(ConfirmationRequest request)
    {
        if (ConfirmationRequested is null)
        {
            request.Complete(false);
            return;
        }

        ConfirmationRequested.Invoke(this, request);
    }

    private Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        if (ConfirmationRequested is null) return Task.FromResult(false);
        ConfirmationRequested.Invoke(this, request);
        return request.WaitAsync();
    }

    private async Task<bool> ConfirmDiscardDirtyAsync()
    {
        if (!Detail.IsDirty) return true;

        return await ConfirmAsync(new ConfirmationRequest
        {
            Title = Loc.T("存在未保存的修改"),
            Message = Loc.F("Key“{0}”的编辑器中存在未保存的修改。\\n", Detail.KeyName) +
                      Loc.T("继续操作将丢弃这些修改。"),
            ConfirmText = Loc.T("丢弃修改并继续"),
            CancelText = Loc.T("返回编辑"),
            IsDanger = true,
        });
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        if (_closingConfirmed) return true;
        if (!Detail.IsDirty)
        {
            _closingConfirmed = true;
            return true;
        }

        var confirmed = await ConfirmAsync(new ConfirmationRequest
        {
            Title = Loc.T("退出 WinRedisClient？"),
            Message = Loc.F("Key“{0}”存在未保存的修改，退出将丢失这些修改。", Detail.KeyName),
            ConfirmText = Loc.T("退出并丢弃修改"),
            CancelText = Loc.T("继续编辑"),
            IsDanger = true,
        });

        _closingConfirmed = confirmed;
        return confirmed;
    }

    private void ShowToast(NotificationMessage message)
        => ShowToast(message.Message, message.IsError);

    private void ShowToast(string message, bool isError = false)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        var cts = new CancellationTokenSource();
        _toastCts = cts;

        ToastMessage = message;
        IsToastError = isError;
        IsToastVisible = true;

        _ = HideToastLaterAsync(cts, isError ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(4));
    }

    private async Task HideToastLaterAsync(CancellationTokenSource cts, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, cts.Token);
            if (!cts.IsCancellationRequested)
                IsToastVisible = false;
        }
        catch (OperationCanceledException)
        {
            // 新的提示替代旧提示
        }
    }

    private static string DescribeFailure(CommandResult result) => result.ErrorCode switch
    {
        "readonly_mode" => Loc.T("当前连接为只读模式"),
        "confirmation_required" or "production_confirmation_required" => Loc.T("操作被安全策略阻止（需要确认）"),
        "target_exists" => Loc.T("目标已存在"),
        "key_not_found" => Loc.T("Key 不存在或已过期"),
        "not_connected" => Loc.T("尚未连接 Redis"),
        "outcome_unknown" => Loc.T("写入结果未知，请刷新确认后重试"),
        _ => result.ErrorMessage ?? (result.ErrorCode ?? Loc.T("未知错误")),
    };

    public async ValueTask DisposeAsync()
    {
        StopHeartbeat();
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        Monitor.Deactivate();
        await Diff.DisposeAsync();

        if (AppServices.Redis is not null)
        {
            try { await AppServices.Redis.DisposeAsync(); } catch { }
        }
    }
}
