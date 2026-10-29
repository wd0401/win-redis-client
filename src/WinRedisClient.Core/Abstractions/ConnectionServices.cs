using WinRedisClient.Core.Models;

namespace WinRedisClient.Core.Abstractions;

/// <summary>只验证连接，不保存配置、不影响当前会话。</summary>
public interface IConnectionTester
{
    Task<ConnectionTestResult> TestAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default);
}

/// <summary>连接配置的增删改查。</summary>
public interface IConnectionProfileService
{
    Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ConnectionProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>凭证安全存储（Windows Credential Manager / DPAPI）。</summary>
public interface ICredentialStore
{
    Task SetAsync(string reference, string secret, CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string reference, CancellationToken cancellationToken = default);
    Task DeleteAsync(string reference, CancellationToken cancellationToken = default);
}
