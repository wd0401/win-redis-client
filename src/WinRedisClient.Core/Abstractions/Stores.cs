using WinRedisClient.Core.Models;

namespace WinRedisClient.Core.Abstractions;

/// <summary>连接配置持久化（不含明文密码）。</summary>
public interface IConnectionStore
{
    Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>用户设置持久化。</summary>
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>最近访问与命令历史持久化（本地、可清空、可关闭）。</summary>
public interface IHistoryStore
{
    Task<IReadOnlyList<RecentKeyEntry>> GetRecentAsync(CancellationToken cancellationToken = default);
    Task AddRecentAsync(RecentKeyEntry entry, CancellationToken cancellationToken = default);
    Task RemoveRecentAsync(string key, CancellationToken cancellationToken = default);
    Task ClearRecentAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandHistoryEntry>> GetCommandsAsync(CancellationToken cancellationToken = default);
    Task AddCommandAsync(CommandHistoryEntry entry, bool enabled, int limit, CancellationToken cancellationToken = default);
    Task RemoveCommandAsync(string command, CancellationToken cancellationToken = default);
    Task ClearCommandsAsync(CancellationToken cancellationToken = default);
}
