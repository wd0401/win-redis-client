using StackExchange.Redis;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Redis;

/// <summary>Redis 会话工厂 — 可选 SSH 隧道 + 密码认证，创建 ConnectionMultiplexer。</summary>
public sealed class RedisSessionFactory : IRedisSessionFactory
{
    private readonly ISshTunnelService _sshTunnelService;

    public RedisSessionFactory(ISshTunnelService sshTunnelService)
    {
        _sshTunnelService = sshTunnelService;
    }

    public async Task<IRedisSession> CreateAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        if (profile.Kind != ConnectionKind.Standalone)
            throw new NotSupportedException(Loc.T("当前版本仅支持 Standalone 连接（TLS / SSH 隧道可用）"));

        if (string.IsNullOrWhiteSpace(profile.Host))
            throw new ArgumentException(Loc.T("Host 不能为空"), nameof(profile));

        if (profile.Port is <= 0 or > 65535)
            throw new ArgumentException(Loc.T("Port 必须在 1-65535 之间"), nameof(profile));

        var host = profile.Host.Trim();
        var port = profile.Port;
        ISshTunnel? tunnel = null;

        try
        {
            // SSH 隧道：本地随机端口 → 跳板机 → target Redis
            if (profile.UseSshTunnel)
            {
                if (string.IsNullOrWhiteSpace(profile.SshHost))
                    throw new ArgumentException(Loc.T("已启用 SSH 隧道，但 SSH Host 为空"), nameof(profile));

                tunnel = await _sshTunnelService.OpenAsync(new SshTunnelConfig
                {
                    JumpHost = profile.SshHost.Trim(),
                    JumpPort = profile.SshPort,
                    TargetHost = profile.Host.Trim(),
                    TargetPort = profile.Port,
                    Username = profile.SshUsername,
                    AuthMode = profile.SshAuthMode,
                    Password = profile.SshPassword,
                    PrivateKeyPath = profile.SshPrivateKeyPath,
                    KeyPassphrase = profile.SshKeyPassphrase,
                }, cancellationToken).ConfigureAwait(false);

                var parts = tunnel.LocalEndpoint.Split(':');
                host = parts[0];
                port = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            }

            var options = new ConfigurationOptions
            {
                AbortOnConnectFail = true,
                ConnectTimeout = (int)Math.Clamp(profile.ConnectTimeout.TotalMilliseconds, 1000, 60000),
                SyncTimeout = (int)Math.Clamp(profile.CommandTimeout.TotalMilliseconds, 1000, 600000),
                AsyncTimeout = (int)Math.Clamp(profile.CommandTimeout.TotalMilliseconds, 1000, 600000),
                ConnectRetry = 1,
                KeepAlive = 60,
                Ssl = profile.UseTls,
                ClientName = "WinRedis/1.0",
                // INFO / SLOWLOG / MEMORY 等诊断命令在 SE.Redis 中归为 admin 命令，必须开启；
                // 写风险由应用层只读模式与确认策略拦截，不依赖此开关
                AllowAdmin = true,
                DefaultDatabase = Math.Clamp(profile.Database, 0, 15),
            };

            if (!string.IsNullOrEmpty(profile.Password))
                options.Password = profile.Password;
            if (!string.IsNullOrWhiteSpace(profile.Username))
                options.User = profile.Username;

            options.EndPoints.Add(host, port);

            var mux = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
            return new RedisSession(Guid.NewGuid(), profile, mux, tunnel);
        }
        catch
        {
            // 连接失败时释放已建立的隧道，避免泄漏
            if (tunnel is not null)
                await tunnel.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
