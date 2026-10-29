using WinRedisClient.Core.Models;

namespace WinRedisClient.Core.Abstractions;

/// <summary>创建与释放 Redis 会话。</summary>
public interface IRedisSessionFactory
{
    Task<IRedisSession> CreateAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
}

/// <summary>活跃的 Redis 会话。</summary>
public interface IRedisSession : IAsyncDisposable
{
    Guid Id { get; }
    ConnectionProfile Profile { get; }
    bool IsConnected { get; }
}

/// <summary>执行受策略控制的命令。</summary>
public interface IRedisCommandExecutor
{
    Task<CommandResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default);
}

/// <summary>命令请求。</summary>
public sealed record CommandRequest
{
    public required string Command { get; init; }
    public required CommandRiskLevel RiskLevel { get; init; }
    public required Guid SessionId { get; init; }
    public TimeSpan? Timeout { get; init; }
    public bool AllowAutoRetry { get; init; }
    public Guid? AuditCorrelationId { get; init; }
}

/// <summary>命令执行结果。</summary>
public sealed record CommandResult
{
    public required ExecutionOutcome Outcome { get; init; }
    public string? Output { get; init; }
    /// <summary>可稳定判断的错误码，如 readonly_mode / confirmation_required / target_exists。</summary>
    public string? ErrorCode { get; init; }
    /// <summary>面向用户的错误说明（不含密码）。</summary>
    public string? ErrorMessage { get; init; }
    public TimeSpan Elapsed { get; init; }

    public bool IsSuccess => Outcome == ExecutionOutcome.Success;
}
