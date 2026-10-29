namespace WinRedisClient.Core.Models;

/// <summary>连接类型。</summary>
public enum ConnectionKind
{
    Standalone,
    Sentinel,
    Cluster,
    CloudProxy,
}

/// <summary>SSH 隧道认证方式。</summary>
public enum SshAuthMethod
{
    Password,
    PrivateKey,
}

/// <summary>连接环境标签。</summary>
public enum EnvironmentTag
{
    Development,
    Testing,
    Staging,
    Production,
    Custom,
}

/// <summary>连接卡片的警告标记色。</summary>
public enum ConnectionWarningColor
{
    None,
    Yellow,
    Orange,
    Red,
    Purple,
    Blue,
    Green,
}

/// <summary>连接生命周期状态。</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
}

/// <summary>命令风险等级（方案 10.2）。</summary>
public enum CommandRiskLevel
{
    /// <summary>L0 只读：GET、SCAN、TYPE、TTL 等。</summary>
    ReadOnly = 0,
    /// <summary>L1 单项写入：SET、HSET、单键 DEL 等。</summary>
    SingleWrite = 1,
    /// <summary>L2 批量/脚本/阻塞。</summary>
    BatchOrScript = 2,
    /// <summary>L3 高危管理：FLUSHALL、SHUTDOWN 等。</summary>
    HighRisk = 3,
}

/// <summary>命令执行结果状态。</summary>
public enum ExecutionOutcome
{
    Success,
    Failure,
    Cancelled,
    /// <summary>写命令发送后超时或断连，结果未知，不得自动重试。</summary>
    OutcomeUnknown,
}

/// <summary>连接配置（不含敏感字段；凭证通过 <see cref="CredentialRef"/> 引用）。</summary>
public sealed record ConnectionProfile
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    /// <summary>左侧连接列表的自定义分组名；空表示未分组。</summary>
    public string? Group { get; init; }
    /// <summary>连接卡片警告色标记，仅用于本地视觉提示。</summary>
    public ConnectionWarningColor WarningColor { get; init; }
    public required EnvironmentTag Environment { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public bool UseTls { get; init; }
    public string? Username { get; init; }
    /// <summary>Redis 认证密码（MVP 阶段内存传递；持久化阶段经 ICredentialStore/DPAPI 加密落盘）。</summary>
    public string? Password { get; init; }
    /// <summary>指向安全存储中的凭证，不包含明文密码。</summary>
    public string? CredentialRef { get; init; }
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>默认数据库；连接成功后自动切换。</summary>
    public int Database { get; init; }
    /// <summary>用户备注。</summary>
    public string? Notes { get; init; }
    /// <summary>自定义环境显示名（仅 Environment=Custom 时使用）。</summary>
    public string? EnvironmentLabel { get; init; }
    /// <summary>只读模式：阻止 L1/L2/L3 命令。</summary>
    public bool ReadOnly { get; init; }
    /// <summary>连接后自动按批次加载 Key（默认开启，避免大库一次性加载）。</summary>
    public bool AutoLoadKeys { get; init; } = true;
    public ConnectionKind Kind { get; init; } = ConnectionKind.Standalone;
    public Guid? SshConfigRef { get; init; }

    // ── SSH 隧道（MVP 内联配置；后续可拆分为独立 SSH 配置 + SshConfigRef 引用） ──
    public bool UseSshTunnel { get; init; }
    public SshAuthMethod SshAuthMode { get; init; } = SshAuthMethod.Password;
    public string? SshHost { get; init; }
    public int SshPort { get; init; } = 22;
    public string? SshUsername { get; init; }
    public string? SshPassword { get; init; }
    /// <summary>SSH 私钥文件路径（OpenSSH/PEM；不支持 PuTTY .ppk）。</summary>
    public string? SshPrivateKeyPath { get; init; }
    /// <summary>私钥口令（可选；DPAPI 加密落盘）。</summary>
    public string? SshKeyPassphrase { get; init; }
}

/// <summary>连接测试结果（不保存配置，仅验证）。</summary>
public sealed record ConnectionTestResult(
    bool Success,
    TimeSpan Elapsed,
    string? RedisVersion = null,
    string? Message = null,
    string? TechnicalDetail = null);

/// <summary>命令执行确认信息。执行层据此判断是否放行高危命令。</summary>
public sealed record CommandConfirmation(bool Confirmed, string? Scope = null);

