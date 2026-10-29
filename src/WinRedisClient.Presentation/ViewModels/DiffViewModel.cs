using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;
using WinRedisClient.Core.Services;
using WinRedisClient.Presentation.Support;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

public sealed record DiffLineRowViewModel(string KindName, string Prefix, string Text);

/// <summary>Key 对比：两个独立会话分别读取、类型感知差异比较。</summary>
public partial class DiffViewModel : ObservableObject
{
    private IRedisService? _leftService;
    private IRedisService? _rightService;
    private int _prefillVersion;

    [ObservableProperty] private ConnectionItemViewModel? _leftConnection;
    [ObservableProperty] private ConnectionItemViewModel? _rightConnection;
    [ObservableProperty] private int _leftDatabase;
    [ObservableProperty] private int _rightDatabase;
    [ObservableProperty] private string _leftKey = string.Empty;
    [ObservableProperty] private string _rightKey = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = Loc.T("选择连接与 Key 后开始比较");
    [ObservableProperty] private string _summary = Loc.T("尚未比较");
    [ObservableProperty] private string _leftMeta = "—";
    [ObservableProperty] private string _rightMeta = "—";
    [ObservableProperty] private string _leftValuePreview = string.Empty;
    [ObservableProperty] private string _rightValuePreview = string.Empty;
    [ObservableProperty] private int _addedCount;
    [ObservableProperty] private int _removedCount;
    [ObservableProperty] private int _modifiedCount;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _typeMismatch;
    [ObservableProperty] private bool _isTruncated;
    [ObservableProperty] private string _errorText = string.Empty;

    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = [];
    public ObservableCollection<DiffLineRowViewModel> Lines { get; } = [];
    public IReadOnlyList<int> Databases { get; } = Enumerable.Range(0, 16).ToList();

    public event EventHandler<NotificationMessage>? NotificationRequested;
    public event EventHandler<string>? CopyRequested;
    public event EventHandler<ExportRequest>? ExportRequested;

    public bool CanCompare => Connections.Count > 0 && !IsLoading;

    public async Task LoadConnectionsAsync()
    {
        var store = AppServices.ConnectionStore;
        if (store is null) return;

        try
        {
            var profiles = await store.GetAllAsync();
            Connections.Clear();
            foreach (var profile in profiles)
                Connections.Add(ConnectionItemViewModel.FromProfile(profile));

            if (LeftConnection is null)
                LeftConnection = Connections.FirstOrDefault();
            if (RightConnection is null)
                RightConnection = Connections.Skip(1).FirstOrDefault() ?? Connections.FirstOrDefault();

            OnPropertyChanged(nameof(CanCompare));
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("读取连接配置失败：") + ex.Message;
        }
    }

    /// <summary>由 Shell 在打开页面时预填当前连接与 Key。</summary>
    public async Task PrefillAsync(ConnectionProfile? profile, string? key)
    {
        var version = Interlocked.Increment(ref _prefillVersion);
        await LoadConnectionsAsync();
        if (version != _prefillVersion) return;

        if (profile is not null)
        {
            var match = Connections.FirstOrDefault(c => c.Id == profile.Id)
                        ?? ConnectionItemViewModel.FromProfile(profile);
            LeftConnection = match;
            LeftDatabase = profile.Database;
        }

        if (!string.IsNullOrEmpty(key))
            LeftKey = key;
    }

    public void ClearResult()
    {
        Lines.Clear();
        HasResult = false;
        Summary = Loc.T("尚未比较");
        AddedCount = RemovedCount = ModifiedCount = 0;
        LeftMeta = RightMeta = "—";
        LeftValuePreview = RightValuePreview = string.Empty;
        TypeMismatch = false;
        IsTruncated = false;
        ErrorText = string.Empty;
        StatusText = Loc.T("选择连接与 Key 后开始比较");
    }

    [RelayCommand]
    private async Task Compare()
    {
        if (IsLoading) return;

        if (LeftConnection is null || RightConnection is null)
        {
            ErrorText = Loc.T("请选择左右两侧连接");
            return;
        }

        var leftKey = LeftKey.Trim();
        var rightKey = RightKey.Trim();
        if (leftKey.Length == 0 || rightKey.Length == 0)
        {
            ErrorText = Loc.T("请填写左右两侧的 Key 名称");
            return;
        }

        var factory = AppServices.RedisFactory;
        if (factory is null)
        {
            ErrorText = Loc.T("Redis 会话工厂未初始化");
            return;
        }

        IsLoading = true;
        ErrorText = string.Empty;
        StatusText = Loc.T("正在建立两个独立会话…");
        OnPropertyChanged(nameof(CanCompare));

        try
        {
            await DisposeSessionsAsync();

            var leftProfile = BuildProfile(LeftConnection, LeftDatabase);
            var rightProfile = BuildProfile(RightConnection, RightDatabase);

            StatusText = Loc.T("正在连接左侧…");
            _leftService = factory();
            await _leftService.ConnectAsync(leftProfile);

            StatusText = Loc.T("正在连接右侧…");
            _rightService = factory();
            await _rightService.ConnectAsync(rightProfile);

            StatusText = Loc.T("正在读取 Key 内容…");
            var leftContent = await _leftService.GetKeyContentAsync(leftKey, 1000, 1024 * 1024);
            var rightContent = await _rightService.GetKeyContentAsync(rightKey, 1000, 1024 * 1024);

            LeftMeta = FormatMeta(leftProfile, leftKey, leftContent);
            RightMeta = FormatMeta(rightProfile, rightKey, rightContent);
            LeftValuePreview = BuildPreview(leftContent);
            RightValuePreview = BuildPreview(rightContent);

            var result = KeyDiffEngine.Compare(leftContent, rightContent);
            Lines.Clear();
            foreach (var line in result.Lines)
            {
                var (kind, prefix) = line.Kind switch
                {
                    DiffChangeKind.Added => ("Added", "+"),
                    DiffChangeKind.Removed => ("Removed", "−"),
                    DiffChangeKind.Modified => ("Modified", "~"),
                    _ => ("Context", " "),
                };
                Lines.Add(new DiffLineRowViewModel(kind, prefix, line.Text));
            }

            Summary = result.Summary;
            TypeMismatch = result.TypeMismatch;
            AddedCount = result.Added;
            RemovedCount = result.Removed;
            ModifiedCount = result.Modified;
            HasResult = true;
            IsTruncated = leftContent.IsTruncated || rightContent.IsTruncated;
            StatusText = result.HasChanges
                ? Loc.F("比较完成 · +{0} −{1} ~{2}", result.Added, result.Removed, result.Modified)
                : Loc.T("比较完成 · 内容一致");
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("比较失败：") + UiText.Redact(ex.Message,
                LeftConnection?.Password, RightConnection?.Password);
            StatusText = Loc.T("比较失败");
            Lines.Clear();
            HasResult = false;
            NotificationRequested?.Invoke(this, new NotificationMessage(ErrorText, true));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanCompare));
        }
    }

    private static ConnectionProfile BuildProfile(ConnectionItemViewModel item, int database)
    {
        var profile = item.ToProfile();
        return profile with { Database = Math.Clamp(database, 0, 15), ReadOnly = true };
    }

    private static string FormatMeta(ConnectionProfile profile, string key, KeyContent content)
    {
        var ttl = TtlFormatter.Format(content.TimeToLive);
        var truncated = content.IsTruncated ? Loc.T(" · 已截断") : string.Empty;
        return $"{profile.Name} · DB{profile.Database} · {key} · {content.Type} · TTL {ttl}{truncated}";
    }

    private static string BuildPreview(KeyContent content)
    {
        if (!string.IsNullOrEmpty(content.Error)) return content.Error;
        if (content.Type == "string")
            return UiText.Ellipsis(content.Text ?? string.Empty, 4000);

        var lines = content.Elements.Take(80).Select(e => content.Type switch
        {
            "hash" => $"{e.Field} = {e.Value}",
            "list" => $"[{e.Field}] {e.Value}",
            "set" => e.Field,
            "zset" => $"{e.Field} ({e.Score})",
            "stream" => $"{e.Field} {e.Value}",
            _ => e.Value,
        });
        return string.Join(Environment.NewLine, lines);
    }

    [RelayCommand]
    private void Swap()
    {
        (LeftConnection, RightConnection) = (RightConnection, LeftConnection);
        (LeftKey, RightKey) = (RightKey, LeftKey);
        (LeftDatabase, RightDatabase) = (RightDatabase, LeftDatabase);
        (LeftMeta, RightMeta) = (RightMeta, LeftMeta);
        (LeftValuePreview, RightValuePreview) = (RightValuePreview, LeftValuePreview);
        ClearResult();
    }

    [RelayCommand]
    private void CopyResult()
    {
        if (!HasResult) return;
        CopyRequested?.Invoke(this, BuildExportText());
    }

    [RelayCommand]
    private void Export()
    {
        if (!HasResult) return;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        ExportRequested?.Invoke(this, new ExportRequest($"winredis-compare-{stamp}.txt", BuildExportText()));
    }

    private string BuildExportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Loc.T("WinRedisClient Key 对比"));
        sb.AppendLine(Loc.F("时间：{0}", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")));
        sb.AppendLine(Loc.T("左侧：") + LeftMeta);
        sb.AppendLine(Loc.T("右侧：") + RightMeta);
        sb.AppendLine(Summary);
        sb.AppendLine(Loc.F("新增 {0} · 删除 {1} · 修改 {2}", AddedCount, RemovedCount, ModifiedCount));
        sb.AppendLine(new string('─', 60));
        foreach (var line in Lines)
            sb.AppendLine($"{line.Prefix} {line.Text}");
        return sb.ToString();
    }

    [RelayCommand]
    private void Clear() => ClearResult();

    public async ValueTask DisposeAsync()
    {
        await DisposeSessionsAsync();
    }

    private async Task DisposeSessionsAsync()
    {
        if (_leftService is not null)
        {
            try { await _leftService.DisposeAsync(); } catch { }
            _leftService = null;
        }

        if (_rightService is not null)
        {
            try { await _rightService.DisposeAsync(); } catch { }
            _rightService = null;
        }
    }
}
