using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Models;
using WinRedisClient.Core.Services;
using WinRedisClient.Presentation.Support;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

/// <summary>CLI 输出行。</summary>
public sealed record CliLineViewModel(string Text, string Kind, string TimeText);

/// <summary>
/// 命令终端：真实执行、风险分级提示、L2/L3 与生产写操作二次确认、本地历史。
/// 认证类命令不会写入历史。
/// </summary>
public partial class CliViewModel : ObservableObject
{
    private const int MaxLines = 500;
    private Guid _connectionId;
    private int _database;

    [ObservableProperty] private string _input = string.Empty;
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionName = string.Empty;
    [ObservableProperty] private string _contextText = Loc.T("未连接");
    [ObservableProperty] private string _riskText = Loc.T("L0 · 只读");
    [ObservableProperty] private string _riskColor = "#10B981";
    [ObservableProperty] private string _statusText = Loc.T("就绪");
    [ObservableProperty] private string _lastDurationText = string.Empty;

    public ObservableCollection<CliLineViewModel> Lines { get; } = [];
    public ObservableCollection<CommandHistoryEntry> History { get; } = [];

    public event EventHandler<NotificationMessage>? NotificationRequested;
    public event EventHandler<ConfirmationRequest>? ConfirmationRequested;
    public event EventHandler<string>? CopyRequested;

    public bool HasHistory => History.Count > 0;
    public bool IsReadOnly => AppServices.Redis?.CurrentProfile?.ReadOnly ?? false;
    public string PromptText => IsConnected ? $"{ConnectionName} DB{_database} ❯" : Loc.T("未连接 ❯");

    public CliViewModel()
    {
        AppendLine(Loc.T("WinRedisClient 命令终端"), "info");
        AppendLine(Loc.T("连接 Redis 后可直接执行命令。支持引号与转义，例如：SET greeting \"hello world\""), "info");
        AppendLine(Loc.T("L2/L3 及生产环境写操作会在执行前要求确认；输入 clear / cls 可清空屏幕。"), "info");
    }

    public void Configure(bool connected, string connectionName, int database, Guid connectionId)
    {
        IsConnected = connected;
        ConnectionName = connectionName;
        _database = database;
        _connectionId = connectionId;
        ContextText = connected ? $"{connectionName} · DB{database}" : Loc.T("未连接");
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(PromptText));
        OnPropertyChanged(nameof(CanWrite));
        StatusText = connected ? Loc.T("就绪") : Loc.T("未连接：请先在左侧连接 Redis");
    }

    public bool CanWrite => !IsReadOnly;

    public async Task ActivateAsync()
    {
        IsConnected = AppServices.Redis?.IsConnected ?? false;
        await LoadHistoryAsync();
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(PromptText));
        OnPropertyChanged(nameof(CanWrite));
    }

    public async Task LoadHistoryAsync()
    {
        History.Clear();
        var store = AppServices.HistoryStore;
        if (store is null) return;

        try
        {
            var entries = await store.GetCommandsAsync();
            foreach (var entry in entries.Take(50))
                History.Add(entry);
        }
        catch
        {
            // 历史读取失败不影响命令执行
        }

        OnPropertyChanged(nameof(HasHistory));
    }

    [RelayCommand]
    private async Task Execute()
    {
        if (IsExecuting) return;

        var raw = Input.Trim();
        if (raw.Length == 0) return;

        if (raw is "clear" or "cls")
        {
            Lines.Clear();
            Input = string.Empty;
            return;
        }

        if (!IsConnected || AppServices.Redis is not { IsConnected: true } redis)
        {
            AppendLine(Loc.T("未连接 Redis，请先在左侧建立连接。"), "error");
            StatusText = Loc.T("未连接");
            return;
        }

        IReadOnlyList<string> tokens;
        try
        {
            tokens = CommandLineParser.Parse(raw);
        }
        catch (FormatException ex)
        {
            AppendLine(Loc.T("命令解析失败：") + ex.Message, "error");
            return;
        }

        if (tokens.Count == 0) return;

        var name = tokens[0].ToUpperInvariant();
        var args = tokens.Skip(1).ToArray();
        var risk = CommandRiskClassifier.Classify(name, args);
        var profile = redis.CurrentProfile;
        var production = profile?.Environment == EnvironmentTag.Production;

        if (profile?.ReadOnly == true && risk > CommandRiskLevel.ReadOnly)
        {
            AppendLine(Loc.F("只读连接：命令 {0} 属于写操作，已被阻止。", name), "error");
            AppendLine("❯ " + raw, "command");
            StatusText = Loc.T("已阻止：只读模式");
            return;
        }

        var isSensitive = CommandRiskClassifier.IsSensitive(name, args);

        if (risk >= CommandRiskLevel.BatchOrScript || (production && risk > CommandRiskLevel.ReadOnly))
        {
            var level = risk switch
            {
                CommandRiskLevel.HighRisk => Loc.T("L3 高危"),
                CommandRiskLevel.BatchOrScript => Loc.T("L2 批量/脚本"),
                _ => Loc.T("L1 写操作"),
            };
            var message = production
                ? Loc.F("当前连接为生产环境：{0} · DB{1}\\n", profile!.Name, redis.Database) +
                  Loc.F("命令被分类为 {0}：\\n{1}\\n\\n", level, raw) +
                  Loc.T("该命令可能修改大量数据或影响实例运行，请确认后执行。")
                : Loc.F("命令被分类为 {0}：\\n{1}\\n\\n确认执行？", level, raw);

            var confirmRequest = new ConfirmationRequest
            {
                Title = Loc.T("执行命令确认"),
                Message = message,
                ConfirmText = Loc.T("执行命令"),
                CancelText = Loc.T("取消"),
                IsDanger = true,
                RequiredText = risk == CommandRiskLevel.HighRisk || (production && risk > CommandRiskLevel.ReadOnly)
                    ? name
                    : null,
            };

            if (ConfirmationRequested is null ||
                !await AskConfirmationAsync(confirmRequest))
            {
                AppendLine(Loc.T("已取消执行：") + raw, "info");
                StatusText = Loc.T("已取消");
                return;
            }
        }

        Input = string.Empty;
        IsExecuting = true;
        StatusText = Loc.T("执行中…");
        AppendLine("❯ " + raw, "command");
        OnPropertyChanged(nameof(PromptText));

        try
        {
            var result = await redis.ExecuteArgsAsync(name, args, risk, confirmed: true);
            LastDurationText = result.Elapsed.TotalMilliseconds >= 1
                ? $"{result.Elapsed.TotalMilliseconds:N0} ms"
                : $"{result.Elapsed.TotalMicroseconds:N0} µs";

            switch (result.Outcome)
            {
                case ExecutionOutcome.Success:
                    AppendLine(result.Output ?? "(no output)", "output");
                    StatusText = Loc.F("执行成功 · {0}", LastDurationText);
                    if (!isSensitive)
                        await RecordHistoryAsync(raw, redis);
                    break;
                case ExecutionOutcome.OutcomeUnknown:
                    AppendLine(Loc.T("结果未知：") + (result.ErrorMessage ?? result.ErrorCode), "warning");
                    AppendLine(Loc.T("写命令可能已在服务端生效。请先刷新数据确认，再决定是否重试。"), "warning");
                    StatusText = Loc.T("结果未知 · 请刷新确认");
                    break;
                case ExecutionOutcome.Cancelled:
                    AppendLine(Loc.T("已取消"), "warning");
                    StatusText = Loc.T("已取消");
                    break;
                default:
                    AppendLine(Loc.F("错误{0}：{1}", (result.ErrorCode is null ? "" : $" [{result.ErrorCode}]"), result.ErrorMessage ?? "执行失败"),
                        "error");
                    StatusText = Loc.T("执行失败");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppendLine(Loc.T("执行异常：") + UiText.Redact(ex.Message, profile?.Password), "error");
            StatusText = Loc.T("执行异常");
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private async Task<bool> AskConfirmationAsync(ConfirmationRequest request)
    {
        ConfirmationRequested!.Invoke(this, request);
        return await request.WaitAsync();
    }

    private async Task RecordHistoryAsync(string command, WinRedisClient.Core.Abstractions.IRedisService redis)
    {
        var store = AppServices.HistoryStore;
        if (store is null) return;

        var settings = AppServices.Settings;
        if (!settings.CommandHistoryEnabled) return;

        try
        {
            await store.AddCommandAsync(new CommandHistoryEntry(
                    command,
                    DateTimeOffset.Now,
                    redis.CurrentProfile?.Id ?? _connectionId,
                    redis.Database),
                enabled: settings.CommandHistoryEnabled,
                limit: settings.CommandHistoryLimit);
            await LoadHistoryAsync();
        }
        catch
        {
            // 历史持久化失败不影响命令结果
        }
    }

    [RelayCommand]
    private void ClearLines()
    {
        Lines.Clear();
        StatusText = Loc.T("已清空输出");
    }

    [RelayCommand]
    private void CopyOutput()
    {
        if (Lines.Count == 0) return;
        CopyRequested?.Invoke(this, string.Join(Environment.NewLine, Lines.Select(l => l.Text)));
    }

    [RelayCommand]
    private void UseHistory(CommandHistoryEntry? entry)
    {
        if (entry is null) return;
        Input = entry.Command;
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        var store = AppServices.HistoryStore;
        if (store is null) return;
        try
        {
            await store.ClearCommandsAsync();
            History.Clear();
            OnPropertyChanged(nameof(HasHistory));
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("命令历史已清空")));
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("清空历史失败：") + ex.Message, true));
        }
    }

    private void AppendLine(string text, string kind)
    {
        Lines.Add(new CliLineViewModel(text, kind, DateTime.Now.ToString("HH:mm:ss")));
        while (Lines.Count > MaxLines) Lines.RemoveAt(0);
    }

    partial void OnInputChanged(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            RiskText = Loc.T("L0 · 只读");
            RiskColor = "#10B981";
            return;
        }

        if (trimmed is "clear" or "cls")
        {
            RiskText = Loc.T("本地命令");
            RiskColor = "#64748B";
            return;
        }

        try
        {
            var tokens = CommandLineParser.Parse(trimmed);
            if (tokens.Count == 0) return;

            var risk = CommandRiskClassifier.Classify(tokens[0], tokens.Skip(1).ToArray());
            (RiskText, RiskColor) = risk switch
            {
                CommandRiskLevel.ReadOnly => (Loc.T("L0 · 只读"), "#10B981"),
                CommandRiskLevel.SingleWrite => (Loc.T("L1 · 单项写入"), "#F59E0B"),
                CommandRiskLevel.BatchOrScript => (Loc.T("L2 · 批量/脚本"), "#F97316"),
                _ => (Loc.T("L3 · 高危"), "#EF4444"),
            };
        }
        catch (FormatException)
        {
            RiskText = Loc.T("解析错误");
            RiskColor = "#EF4444";
        }
    }
}
