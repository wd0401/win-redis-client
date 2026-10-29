namespace WinRedisClient.Core.Abstractions;

/// <summary>安全审计。</summary>
public interface IOperationAuditService
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>审计记录（不含密码、Token、私钥或完整敏感 Value）。</summary>
public sealed record AuditEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required Guid SessionId { get; init; }
    public required string ConnectionName { get; init; }
    public required string Environment { get; init; }
    public required string CommandCategory { get; init; }
    public string? TargetKey { get; init; }
    public required string ResultStatus { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary>后台任务、并发限制、状态与取消。</summary>
public interface IBackgroundTaskService
{
    Task<IBackgroundTask> RunAsync(
        string name,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default);
}

/// <summary>后台任务句柄。</summary>
public interface IBackgroundTask : IAsyncDisposable
{
    Guid Id { get; }
    string Name { get; }
    BackgroundTaskStatus Status { get; }
    Task WaitAsync(CancellationToken cancellationToken = default);
}

public enum BackgroundTaskStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed,
}
