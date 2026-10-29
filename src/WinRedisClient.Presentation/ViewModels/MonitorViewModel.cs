using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Models;
using WinRedisClient.Core.Services;
using WinRedisClient.Presentation.Support;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

/// <summary>Keyspace 行。</summary>
public sealed record KeyspaceRowViewModel(
    string Database,
    long Keys,
    long Expires,
    string AvgTtlText);

/// <summary>命令统计行。</summary>
public sealed record CommandStatRowViewModel(
    string Command,
    long Calls,
    long Usec,
    double UsecPerCall);

/// <summary>SlowLog 行。</summary>
public sealed record SlowLogRowViewModel(
    long Id,
    string TimeText,
    string DurationText,
    string CommandText,
    string ClientText);

/// <summary>实例监控：真实 INFO / SLOWLOG 采样，自动刷新与曲线数据。</summary>
public partial class MonitorViewModel : ObservableObject
{
    private const int MaxSamples = 60;

    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private CancellationTokenSource? _loopCts;
    private bool _loadedOnce;
    private int _sampleCount;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isAutoRefresh = true;
    [ObservableProperty] private int _intervalSeconds = 5;
    [ObservableProperty] private string _statusText = Loc.T("进入监控页后自动采样");
    [ObservableProperty] private string _lastUpdatedText = "—";
    [ObservableProperty] private string _errorText = string.Empty;

    [ObservableProperty] private string _usedMemoryText = "—";
    [ObservableProperty] private string _memoryDetailText = "—";
    [ObservableProperty] private string _connectedClientsText = "—";
    [ObservableProperty] private string _clientsDetailText = "—";
    [ObservableProperty] private string _opsText = "—";
    [ObservableProperty] private string _hitRateText = "—";
    [ObservableProperty] private string _hitDetailText = "—";
    [ObservableProperty] private string _currentDbKeysText = "—";
    [ObservableProperty] private string _totalKeysText = "—";
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private string _versionText = "—";
    [ObservableProperty] private string _modeText = "—";
    [ObservableProperty] private string _roleText = "—";
    [ObservableProperty] private string _networkText = "—";
    [ObservableProperty] private string _evictedText = "—";
    [ObservableProperty] private string _expiredText = "—";
    [ObservableProperty] private string _rejectedText = "—";
    [ObservableProperty] private string _totalCommandsText = "—";
    [ObservableProperty] private string _totalConnectionsText = "—";
    [ObservableProperty] private string _fragmentationText = "—";
    [ObservableProperty] private string _persistenceText = "—";
    [ObservableProperty] private string _slowLogStatus = Loc.T("尚未读取");

    public IReadOnlyList<int> IntervalOptions { get; } = [1, 2, 5, 10, 30, 60];

    public ObservableCollection<MetricSample> Samples { get; } = [];
    public ObservableCollection<double> MemorySeries { get; } = [];
    public ObservableCollection<double> OpsSeries { get; } = [];
    public ObservableCollection<double> ClientsSeries { get; } = [];
    public ObservableCollection<KeyspaceRowViewModel> Keyspaces { get; } = [];
    public ObservableCollection<CommandStatRowViewModel> CommandStats { get; } = [];
    public ObservableCollection<SlowLogRowViewModel> SlowLog { get; } = [];

    public event EventHandler<NotificationMessage>? NotificationRequested;

    public string SamplingButtonText => IsAutoRefresh ? Loc.T("暂停采样") : Loc.T("开始采样");
    public bool HasCommandStats => CommandStats.Count > 0;
    public bool HasSlowLog => SlowLog.Count > 0;
    public bool HasSamples => Samples.Count > 1;

    public async Task ActivateAsync()
    {
        IsConnected = AppServices.Redis?.IsConnected ?? false;
        if (!IsConnected)
        {
            StatusText = Loc.T("未连接 Redis");
            ErrorText = string.Empty;
            return;
        }

        if (!_loadedOnce)
            await RefreshAsync(includeSlowLog: true);

        if (IsAutoRefresh && _loopCts is null)
            StartLoop();
    }

    public void Deactivate()
    {
        StopLoop();
        StatusText = Loc.T("采样已暂停（离开监控页）");
    }

    public void UpdateInterval(int seconds)
    {
        IntervalSeconds = Math.Clamp(seconds, 1, 60);
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync(includeSlowLog: true);

    [RelayCommand]
    private async Task RefreshSlowLog()
    {
        if (!IsConnected) return;
        await LoadSlowLogAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ToggleSampling()
    {
        IsAutoRefresh = !IsAutoRefresh;
    }

    partial void OnIsAutoRefreshChanged(bool value)
    {
        OnPropertyChanged(nameof(SamplingButtonText));
        if (!IsConnected) return;
        if (value) StartLoop();
        else
        {
            StopLoop();
            StatusText = Loc.T("采样已暂停");
        }
    }

    partial void OnIsConnectedChanged(bool value)
    {
        if (!value) StopLoop();
    }

    public async Task RefreshAsync(bool includeSlowLog = false)
    {
        var redis = AppServices.Redis;
        if (redis is not { IsConnected: true })
        {
            IsConnected = false;
            StatusText = Loc.T("未连接 Redis");
            return;
        }

        IsConnected = true;
        IsLoading = true;
        ErrorText = string.Empty;
        StatusText = Loc.T("正在采集 INFO…");

        try
        {
            var snapshot = await redis.GetInfoAsync();

            // Redis 5 的默认 INFO 不含 Commandstats，需单独请求；失败时不拖垮主监控数据。
            InfoSnapshot? commandStats = null;
            try
            {
                commandStats = await redis.GetInfoAsync("commandstats");
            }
            catch
            {
                // 命令统计非关键路径，忽略本次失败。
            }

            ApplySnapshot(snapshot, commandStats);
            _loadedOnce = true;
            _sampleCount++;
            StatusText = IsAutoRefresh ? Loc.F("自动采样中 · 每 {0}s", IntervalSeconds) : Loc.T("采样已暂停");
            LastUpdatedText = snapshot.RetrievedAt.ToLocalTime().ToString("HH:mm:ss");

            if (includeSlowLog || _sampleCount % 6 == 1)
                await LoadSlowLogAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("采集失败：") + UiText.Redact(ex.Message, redis.CurrentProfile?.Password);
            StatusText = Loc.T("采集失败");
            NotificationRequested?.Invoke(this, new NotificationMessage(ErrorText, true));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplySnapshot(InfoSnapshot snapshot, InfoSnapshot? commandStats = null)
    {
        var usedMemory = snapshot.GetLong("used_memory") ?? 0;
        var peakMemory = snapshot.GetLong("used_memory_peak") ?? 0;
        var maxMemory = snapshot.GetLong("maxmemory") ?? 0;
        UsedMemoryText = snapshot.Get("used_memory_human") ?? TtlFormatter.FormatBytes(usedMemory);
        MemoryDetailText = maxMemory > 0
            ? Loc.F("峰值 {0} · 上限 {1}", snapshot.Get("used_memory_peak_human") ?? TtlFormatter.FormatBytes(peakMemory), snapshot.Get("maxmemory_human") ?? TtlFormatter.FormatBytes(maxMemory))
            : Loc.F("峰值 {0} · 未设置 maxmemory", snapshot.Get("used_memory_peak_human") ?? TtlFormatter.FormatBytes(peakMemory));

        var clients = snapshot.GetLong("connected_clients") ?? 0;
        var blocked = snapshot.GetLong("blocked_clients") ?? 0;
        ConnectedClientsText = clients.ToString("N0");
        ClientsDetailText = Loc.F("阻塞 {0:N0} · 总连接 {1}", blocked, snapshot.GetLong("total_connections_received")?.ToString("N0") ?? "—");

        var ops = snapshot.Get("instantaneous_ops_per_sec") ?? "—";
        OpsText = ops;
        var hits = snapshot.GetLong("keyspace_hits") ?? 0;
        var misses = snapshot.GetLong("keyspace_misses") ?? 0;
        var total = hits + misses;
        HitRateText = total > 0 ? $"{hits * 100.0 / total:F1}%" : "—";
        HitDetailText = Loc.F("命中 {0:N0} · 未命中 {1:N0}", hits, misses);

        VersionText = snapshot.Get("redis_version") ?? "—";
        ModeText = snapshot.Get("redis_mode") ?? "—";
        RoleText = snapshot.Get("role") ?? "—";
        UptimeText = UiText.FormatUptime(snapshot.GetLong("uptime_in_seconds") ?? -1);

        NetworkText = Loc.F("入 {0} · 出 {1}", FormatOptionalBytes(snapshot.GetLong("total_net_input_bytes")), FormatOptionalBytes(snapshot.GetLong("total_net_output_bytes")));
        EvictedText = snapshot.GetLong("evicted_keys")?.ToString("N0") ?? "—";
        ExpiredText = snapshot.GetLong("expired_keys")?.ToString("N0") ?? "—";
        RejectedText = snapshot.GetLong("rejected_connections")?.ToString("N0") ?? "—";
        TotalCommandsText = snapshot.GetLong("total_commands_processed")?.ToString("N0") ?? "—";
        TotalConnectionsText = snapshot.GetLong("total_connections_received")?.ToString("N0") ?? "—";
        FragmentationText = snapshot.GetDouble("mem_fragmentation_ratio")?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";

        var aof = string.Equals(snapshot.Get("aof_enabled"), "1", StringComparison.Ordinal) ? Loc.T("AOF 已开启") : Loc.T("AOF 关闭");
        var saveTime = snapshot.GetLong("rdb_last_save_time");
        var lastSave = saveTime is > 0
            ? DateTimeOffset.FromUnixTimeSeconds(saveTime.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "—";
        PersistenceText = Loc.F("{0} · 最后 RDB 保存 {1}", aof, lastSave);

        var keyspaces = BuildKeyspaceRows(snapshot);
        Keyspaces.Clear();
        long totalKeys = 0;
        foreach (var row in keyspaces)
        {
            Keyspaces.Add(row);
            totalKeys += row.Keys;
        }

        var currentDb = AppServices.Redis?.Database ?? 0;
        var current = keyspaces.FirstOrDefault(k =>
            string.Equals(k.Database, "db" + currentDb, StringComparison.OrdinalIgnoreCase));
        CurrentDbKeysText = current?.Keys.ToString("N0") ?? "0";
        TotalKeysText = totalKeys.ToString("N0");

        BuildCommandStats(commandStats ?? snapshot);

        var sample = new MetricSample(
            snapshot.RetrievedAt,
            usedMemory / 1024.0 / 1024.0,
            double.TryParse(ops, NumberStyles.Float, CultureInfo.InvariantCulture, out var opsValue) ? opsValue : 0,
            clients,
            total > 0 ? hits * 100.0 / total : 0,
            totalKeys);

        Samples.Add(sample);
        while (Samples.Count > MaxSamples) Samples.RemoveAt(0);
        AppendSeries(MemorySeries, sample.UsedMemoryMb);
        AppendSeries(OpsSeries, sample.OpsPerSec);
        AppendSeries(ClientsSeries, sample.ConnectedClients);
        OnPropertyChanged(nameof(HasSamples));
    }

    private static string FormatOptionalBytes(long? value)
        => value is null ? "—" : TtlFormatter.FormatBytes(value.Value);

    private static List<KeyspaceRowViewModel> BuildKeyspaceRows(InfoSnapshot snapshot)
    {
        var rows = new List<KeyspaceRowViewModel>();
        var section = snapshot.Sections.FirstOrDefault(s =>
            string.Equals(s.Name, "Keyspace", StringComparison.OrdinalIgnoreCase));
        if (section is null) return rows;

        foreach (var (db, value) in section.Entries)
        {
            long keys = 0, expires = 0, avgTtl = -1;
            foreach (var part in value.Split(','))
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length != 2) continue;
                var name = pieces[0].Trim();
                if (!long.TryParse(pieces[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    continue;
                if (name == "keys") keys = parsed;
                else if (name == "expires") expires = parsed;
                else if (name == "avg_ttl") avgTtl = parsed;
            }

            string ttlText = expires > 0 && avgTtl > 0
                ? TtlFormatter.Format(TimeSpan.FromMilliseconds(avgTtl))
                : "—";
            rows.Add(new KeyspaceRowViewModel(db, keys, expires, ttlText));
        }

        return rows.OrderBy(r => r.Database, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void BuildCommandStats(InfoSnapshot snapshot)
    {
        CommandStats.Clear();
        var section = snapshot.Sections.FirstOrDefault(s =>
            string.Equals(s.Name, "Commandstats", StringComparison.OrdinalIgnoreCase));
        if (section is null)
        {
            OnPropertyChanged(nameof(HasCommandStats));
            return;
        }

        var rows = new List<CommandStatRowViewModel>();
        foreach (var (name, value) in section.Entries)
        {
            long calls = 0, usec = 0;
            double perCall = 0;
            foreach (var part in value.Split(','))
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length != 2) continue;
                var key = pieces[0].Trim();
                var text = pieces[1].Trim();
                if (key == "calls") long.TryParse(text, out calls);
                else if (key == "usec") long.TryParse(text, out usec);
                else if (key == "usec_per_call")
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out perCall);
            }

            var command = name.StartsWith("cmdstat_", StringComparison.OrdinalIgnoreCase)
                ? name["cmdstat_".Length..]
                : name;
            rows.Add(new CommandStatRowViewModel(command, calls, usec, perCall));
        }

        foreach (var row in rows.OrderByDescending(r => r.Calls).Take(10))
            CommandStats.Add(row);
        OnPropertyChanged(nameof(HasCommandStats));
    }

    private async Task LoadSlowLogAsync(CancellationToken cancellationToken)
    {
        var redis = AppServices.Redis;
        if (redis is not { IsConnected: true }) return;

        try
        {
            var entries = await redis.GetSlowLogAsync(20, cancellationToken);
            SlowLog.Clear();
            foreach (var entry in entries)
            {
                SlowLog.Add(new SlowLogRowViewModel(
                    entry.Id,
                    entry.Timestamp.ToString("MM-dd HH:mm:ss"),
                    entry.Duration.TotalMilliseconds >= 1
                        ? $"{entry.Duration.TotalMilliseconds:N1} ms"
                        : $"{entry.Duration.TotalMicroseconds:N0} µs",
                    UiText.SingleLine(entry.CommandText, 200),
                    entry.ClientAddress ?? entry.ClientName ?? "—"));
            }

            SlowLogStatus = entries.Count == 0 ? Loc.T("SlowLog 为空") : Loc.F("{0} 条（最近 20 条）", entries.Count);
        }
        catch (Exception ex)
        {
            SlowLogStatus = Loc.T("读取 SlowLog 失败：") + UiText.Redact(ex.Message);
        }
        finally
        {
            OnPropertyChanged(nameof(HasSlowLog));
        }
    }

    private static void AppendSeries(ObservableCollection<double> series, double value)
    {
        series.Add(value);
        while (series.Count > MaxSamples) series.RemoveAt(0);
    }

    private void StartLoop()
    {
        if (!IsConnected) return;
        StopLoop();
        var cts = new CancellationTokenSource();
        _loopCts = cts;
        StatusText = Loc.F("自动采样中 · 每 {0}s", IntervalSeconds);
        _ = LoopAsync(cts.Token);
    }

    private void StopLoop()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(IntervalSeconds, 1, 60)), token);
                if (token.IsCancellationRequested) break;
                if (AppServices.Redis is not { IsConnected: true })
                {
                    OnUi(() =>
                    {
                        IsConnected = false;
                        StatusText = Loc.T("连接已断开");
                    });
                    break;
                }

                await RefreshAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // 停止采样
        }
        catch (Exception ex)
        {
            OnUi(() =>
            {
                ErrorText = Loc.T("采样循环异常：") + UiText.Redact(ex.Message);
                StatusText = Loc.T("采样已停止");
            });
        }
    }

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
            action();
        else
            _ui.Post(_ => action(), null);
    }
}
