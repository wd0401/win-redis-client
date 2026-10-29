using WinRedisClient.Core.Models;

namespace WinRedisClient.Core.Abstractions;

/// <summary>SSH 隧道生命周期管理。</summary>
public interface ISshTunnelService
{
    Task<ISshTunnel> OpenAsync(SshTunnelConfig config, CancellationToken cancellationToken = default);
}

/// <summary>活跃 SSH 隧道。</summary>
public interface ISshTunnel : IAsyncDisposable
{
    Guid Id { get; }
    string LocalEndpoint { get; }
    bool IsActive { get; }
}

/// <summary>SSH 隧道配置。</summary>
public sealed record SshTunnelConfig
{
    public required string JumpHost { get; init; }
    public required int JumpPort { get; init; }
    public required string TargetHost { get; init; }
    public required int TargetPort { get; init; }
    public string? Username { get; init; }
    /// <summary>SSH 认证方式：密码或私钥文件。</summary>
    public SshAuthMethod AuthMode { get; init; } = SshAuthMethod.Password;
    /// <summary>SSH 认证密码（密码模式；内存传递，不落盘）。</summary>
    public string? Password { get; init; }
    /// <summary>SSH 私钥文件路径（OpenSSH/PEM；不支持 PuTTY .ppk）。</summary>
    public string? PrivateKeyPath { get; init; }
    /// <summary>私钥口令（可选）。</summary>
    public string? KeyPassphrase { get; init; }
    /// <summary>指向凭证存储的引用，不包含明文。</summary>
    public string? CredentialRef { get; init; }
    /// <summary>仅监听 127.0.0.1，除非用户明确配置。</summary>
    public string ListenAddress { get; init; } = "127.0.0.1";
}
