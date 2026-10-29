using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;
using WinRedisClient.Core.Services;
using WinRedisClient.Presentation.Support;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

public sealed record BigKeyRowViewModel(
    string Key,
    string Type,
    long? SizeBytes,
    long? ElementCount,
    bool IsLarge)
{
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

    public string SizeText => TtlFormatter.FormatBytes(SizeBytes);
    public string CountText => ElementCount?.ToString("N0") ?? "—";
    public string BadgeText => IsLarge ? Loc.T("大 Key") : string.Empty;
}

/// <summary>BigKey 右侧预览中的一条集合元素。</summary>
public sealed record BigKeyPreviewRow(string Text);

/// <summary>
/// BigKey 分析：SCAN 增量扫描 + MEMORY USAGE / 元素数量采样，按内存排序。
/// 从不使用 KEYS *；扫描数量有上限并可随时取消。
/// </summary>
public partial class BigKeyViewModel : ObservableObject
{
    private readonly List<BigKeyRowViewModel> _all = [];
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _previewCts;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private string _statusText = Loc.T("尚未分析");
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private long _scannedCount;
    [ObservableProperty] private int _candidateCount;
    [ObservableProperty] private string _scanLimitText = "20000";
    [ObservableProperty] private string _minSizeMbText = "0";
    [ObservableProperty] private string _typeFilter = Loc.T("全部");
    [ObservableProperty] private BigKeyRowViewModel? _selectedRow;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _hitScanLimit;
    [ObservableProperty] private string _lastRunText = "—";

    [ObservableProperty] private bool _isPreviewLoading;
    [ObservableProperty] private string _previewKey = string.Empty;
    [ObservableProperty] private string _previewType = "—";
    [ObservableProperty] private string _previewTtl = "—";
    [ObservableProperty] private string _previewSize = "—";
    [ObservableProperty] private string _previewCount = "—";
    [ObservableProperty] private string _previewContent = string.Empty;
    [ObservableProperty] private string _previewNote = string.Empty;
    [ObservableProperty] private string _previewError = string.Empty;
    [ObservableProperty] private bool _isPreviewString;
    [ObservableProperty] private bool _previewIsBinary;

    public ObservableCollection<BigKeyPreviewRow> PreviewItems { get; } = [];
    public bool HasPreview => SelectedRow is not null;
    public bool HasPreviewItems => PreviewItems.Count > 0;
    public bool ShowPreviewItems => !IsPreviewString && HasPreviewItems;

    public ObservableCollection<BigKeyRowViewModel> Rows { get; } = [];
    public IReadOnlyList<string> TypeFilters { get; } = [Loc.T("全部"), "string", "hash", "list", "set", "zset", "stream"];

    public event EventHandler<NotificationMessage>? NotificationRequested;
    public event EventHandler<ExportRequest>? ExportRequested;
    public event EventHandler<string>? OpenKeyRequested;

    public bool HasResults => Rows.Count > 0;
    public bool CanAnalyze => IsConnected && !IsAnalyzing;

    public void Activate()
    {
        IsConnected = AppServices.Redis?.IsConnected ?? false;
        if (!IsConnected)
        {
            StatusText = Loc.T("未连接 Redis");
            Rows.Clear();
            _all.Clear();
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(CanAnalyze));
        }
        else if (_all.Count == 0)
        {
            StatusText = Loc.T("点击“开始分析”扫描当前 DB");
        }
    }

    [RelayCommand]
    private async Task Analyze()
    {
        if (IsAnalyzing) return;

        var redis = AppServices.Redis;
        if (redis is not { IsConnected: true })
        {
            StatusText = Loc.T("未连接 Redis");
            return;
        }

        var limit = ParseScanLimit();
        var cts = new CancellationTokenSource();
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = cts;

        IsAnalyzing = true;
        IsConnected = true;
        ErrorText = string.Empty;
        _all.Clear();
        Rows.Clear();
        SelectedRow = null;
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(CanAnalyze));
        CandidateCount = 0;
        ScannedCount = 0;
        HitScanLimit = false;
        StatusText = Loc.T("正在扫描…");
        ProgressText = "SCAN cursor=0";

        try
        {
            long cursor = 0;
            var scanned = 0;
            var completed = false;

            while (scanned < limit)
            {
                var page = await redis.ScanPageAsync("*", 1000, cursor, cts.Token);
                if (page.Keys.Count == 0 && page.IsComplete) { completed = true; break; }

                var keys = page.Keys.Select(k => k.DisplayKey).ToList();
                var metadata = await redis.GetMetadataBatchAsync(keys, includeSize: true, cts.Token);

                foreach (var meta in metadata)
                {
                    if (meta.Type is "none" or "unknown") continue;
                    var isLarge = (meta.SizeBytes ?? 0) >= 1024 * 1024 || (meta.ElementCount ?? 0) >= 10_000;
                    _all.Add(new BigKeyRowViewModel(meta.Key, meta.Type, meta.SizeBytes, meta.ElementCount, isLarge));
                }

                scanned += keys.Count;
                ScannedCount = scanned;
                ProgressText = Loc.F("已扫描 {0:N0} / 上限 {1:N0} · 候选 {2:N0}", scanned, limit, _all.Count);
                StatusText = Loc.T("正在扫描…");

                cursor = page.NextCursor;
                if (page.IsComplete)
                {
                    completed = true;
                    break;
                }
            }

            if (!completed && scanned >= limit)
                HitScanLimit = true;

            RebuildResults();
            LastRunText = DateTime.Now.ToString("HH:mm:ss");
            StatusText = _all.Count == 0
                ? Loc.T("未发现任何 Key")
                : HitScanLimit
                    ? Loc.F("扫描达到上限 {0:N0}，以下为已采样结果", limit)
                    : completed
                        ? Loc.F("扫描完成，共 {0:N0} 个 Key", scanned)
                        : Loc.F("已扫描 {0:N0} 个 Key", scanned);
            ProgressText = Loc.F("候选 {0:N0} 个 · 显示 {1:N0} 个", _all.Count, Rows.Count);
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("分析已取消");
            ProgressText = Loc.F("已扫描 {0:N0} 个 Key", ScannedCount);
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("分析失败：") + UiText.Redact(ex.Message, redis.CurrentProfile?.Password);
            StatusText = Loc.T("分析失败");
            NotificationRequested?.Invoke(this, new NotificationMessage(ErrorText, true));
        }
        finally
        {
            IsAnalyzing = false;
            OnPropertyChanged(nameof(CanAnalyze));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _scanCts?.Cancel();
        StatusText = Loc.T("正在取消…");
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (AppServices.Redis is not { IsConnected: true })
        {
            StatusText = Loc.T("未连接 Redis");
            return;
        }

        await Analyze();
    }

    [RelayCommand]
    private void OpenKey(BigKeyRowViewModel? row)
    {
        if (row is not null)
            OpenKeyRequested?.Invoke(this, row.Key);
    }

    [RelayCommand]
    private void Export()
    {
        if (Rows.Count == 0) return;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        ExportRequested?.Invoke(this, new ExportRequest($"winredis-bigkeys-{stamp}.csv", BuildCsv()));
    }

    private string BuildCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("key,type,memory_bytes,element_count,is_large");
        foreach (var row in Rows)
        {
            sb.Append(EscapeCsv(row.Key)).Append(',')
                .Append(row.Type).Append(',')
                .Append(row.SizeBytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(row.ElementCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(row.IsLarge ? "true" : "false")
                .AppendLine();
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string value)
        => value.ContainsAny([',', '"', '\n', '\r'])
            ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : value;

    partial void OnSelectedRowChanged(BigKeyRowViewModel? value)
        => _ = LoadPreviewAsync(value);

    private async Task LoadPreviewAsync(BigKeyRowViewModel? row)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        var cts = new CancellationTokenSource();
        _previewCts = cts;

        PreviewItems.Clear();
        PreviewContent = string.Empty;
        PreviewNote = string.Empty;
        PreviewError = string.Empty;
        PreviewIsBinary = false;
        IsPreviewString = row?.Type == "string";
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasPreviewItems));
        OnPropertyChanged(nameof(ShowPreviewItems));

        if (row is null)
        {
            PreviewKey = string.Empty;
            PreviewType = "—";
            PreviewTtl = "—";
            PreviewSize = "—";
            PreviewCount = "—";
            IsPreviewLoading = false;
            return;
        }

        PreviewKey = row.Key;
        PreviewType = row.TypeDisplay;
        PreviewSize = row.SizeText;
        PreviewCount = row.CountText;

        var redis = AppServices.Redis;
        if (redis is not { IsConnected: true })
        {
            PreviewError = Loc.T("未连接 Redis");
            return;
        }

        IsPreviewLoading = true;
        try
        {
            var meta = await redis.GetMetadataAsync(row.Key, includeSize: true, cts.Token);
            if (cts.IsCancellationRequested) return;

            if (meta is null || meta.Type is "none" or "unknown")
            {
                PreviewError = Loc.T("Key 不存在或已过期");
                return;
            }

            PreviewTtl = TtlFormatter.Format(meta.TimeToLive);
            PreviewSize = TtlFormatter.FormatBytes(meta.SizeBytes);
            PreviewCount = meta.ElementCount?.ToString("N0") ?? row.CountText;

            if (row.Type == "string")
            {
                var preview = await redis.GetStringBytesAsync(row.Key, 64 * 1024, cts.Token);
                if (cts.IsCancellationRequested) return;

                if (preview is null)
                {
                    PreviewError = Loc.T("Key 不存在或已过期");
                    return;
                }

                if (UiText.TryDecodeText(preview.Data, out var text))
                {
                    PreviewContent = text;
                    if (preview.IsTruncated)
                        PreviewNote = Loc.F("仅预览前 {0} / 共 {1}", TtlFormatter.FormatBytes(preview.Data.LongLength), TtlFormatter.FormatBytes(preview.TotalLength));
                }
                else
                {
                    PreviewIsBinary = true;
                    PreviewContent = Loc.T("二进制内容，建议在数据浏览中打开");
                }
            }
            else
            {
                var page = await redis.GetCollectionPageAsync(row.Key, row.Type, null, 50, false, cts.Token);
                if (cts.IsCancellationRequested) return;

                if (!string.IsNullOrEmpty(page.Error))
                {
                    PreviewError = page.Error;
                    return;
                }

                foreach (var item in page.Items)
                    PreviewItems.Add(new BigKeyPreviewRow(FormatPreviewRow(row.Type, item)));

                PreviewNote = page.IsComplete
                    ? Loc.F("已显示 {0:N0} 项", PreviewItems.Count)
                    : Loc.F("前 {0:N0} / 共 {1:N0} 项", PreviewItems.Count, page.TotalCount);
            }
        }
        catch (OperationCanceledException)
        {
            // 选择已变化
        }
        catch (Exception ex)
        {
            PreviewError = Loc.T("预览失败：") + UiText.Redact(ex.Message, redis.CurrentProfile?.Password);
        }
        finally
        {
            if (ReferenceEquals(_previewCts, cts))
                IsPreviewLoading = false;
            OnPropertyChanged(nameof(HasPreviewItems));
            OnPropertyChanged(nameof(ShowPreviewItems));
        }
    }

    private static string FormatPreviewRow(string type, ElementData item) => type switch
    {
        "hash" => $"{item.Field}  =  {item.Value}",
        "list" => $"[{item.Field}]  {item.Value}",
        "set" => item.Field,
        "zset" => $"{item.Field}    score={item.Score}",
        "stream" => $"{item.Field}  {item.Value}",
        _ => $"{item.Field}  {item.Value}",
    };

    /// <summary>切换数据库后清空上一次分析结果。</summary>
    public void ResetResults()
    {
        _scanCts?.Cancel();
        _previewCts?.Cancel();
        SelectedRow = null;
        _all.Clear();
        Rows.Clear();
        ScannedCount = 0;
        CandidateCount = 0;
        HitScanLimit = false;
        ProgressText = string.Empty;
        StatusText = IsConnected ? Loc.T("点击“开始分析”扫描当前 DB") : Loc.T("未连接 Redis");
        OnPropertyChanged(nameof(HasResults));
    }

    private void RebuildResults()
    {
        var minBytes = ParseMinSizeMb() * 1024 * 1024;

        var filtered = _all.Where(r =>
                (TypeFilter == Loc.T("全部") || string.Equals(r.Type, TypeFilter, StringComparison.OrdinalIgnoreCase)) &&
                (r.SizeBytes ?? 0) >= minBytes)
            .OrderByDescending(r => r.SizeBytes ?? 0)
            .ThenByDescending(r => r.ElementCount ?? 0)
            .Take(200)
            .ToList();

        Rows.Clear();
        foreach (var row in filtered) Rows.Add(row);
        CandidateCount = _all.Count;
        OnPropertyChanged(nameof(HasResults));
    }

    private int ParseScanLimit()
        => int.TryParse(ScanLimitText, out var value) ? Math.Clamp(value, 100, 500_000) : 20_000;

    private double ParseMinSizeMb()
        => double.TryParse(MinSizeMbText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 0;

    partial void OnTypeFilterChanged(string value) => RebuildResults();
    partial void OnMinSizeMbTextChanged(string value) => RebuildResults();
    partial void OnIsPreviewStringChanged(bool value) => OnPropertyChanged(nameof(ShowPreviewItems));
    partial void OnIsAnalyzingChanged(bool value) => OnPropertyChanged(nameof(CanAnalyze));
    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(CanAnalyze));
}
