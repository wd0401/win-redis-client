using Renci.SshNet;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Transport;

/// <summary>基于 SSH.NET 的本地端口转发隧道服务。支持密码或私钥文件认证。</summary>
public sealed class SshTunnelService : ISshTunnelService
{
    public Task<ISshTunnel> OpenAsync(SshTunnelConfig config, CancellationToken cancellationToken = default)
        => Task.Run(() => OpenCore(config), cancellationToken);

    private static ISshTunnel OpenCore(SshTunnelConfig config)
    {
        var auth = BuildAuthentication(config);

        var connectionInfo = new ConnectionInfo(config.JumpHost, config.JumpPort, config.Username ?? string.Empty, auth.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        var client = new SshClient(connectionInfo);
        try
        {
            client.Connect();
            if (!client.IsConnected)
                throw new InvalidOperationException(Loc.F("SSH 连接失败: {0}:{1}", config.JumpHost, config.JumpPort));

            // 监听随机本地端口，转发到 target
            var port = new ForwardedPortLocal(config.ListenAddress, 0, config.TargetHost, (uint)config.TargetPort);
            client.AddForwardedPort(port);
            port.Start();

            if (!port.IsStarted)
                throw new InvalidOperationException(Loc.T("SSH 本地端口转发启动失败"));

            return new SshTunnel(client, port);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static List<AuthenticationMethod> BuildAuthentication(SshTunnelConfig config)
    {
        var username = config.Username ?? string.Empty;

        if (config.AuthMode == SshAuthMethod.PrivateKey)
        {
            var path = config.PrivateKeyPath?.Trim();
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException(Loc.T("SSH 私钥文件不能为空，请在连接设置中选择私钥文件"));

            if (!File.Exists(path))
                throw new InvalidOperationException(Loc.F("SSH 私钥文件不存在：{0}", path));

            try
            {
                var keyFile = new PrivateKeyFile(path, config.KeyPassphrase ?? string.Empty);
                return [new PrivateKeyAuthenticationMethod(username, keyFile)];
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                                       or Renci.SshNet.Common.SshException or System.Security.Cryptography.CryptographicException)
            {
                throw new InvalidOperationException(
                    Loc.F("SSH 私钥无法读取：{0}。请确认文件格式为 OpenSSH/PEM，口令是否正确。", Path.GetFileName(path)), ex);
            }
        }

        return [new PasswordAuthenticationMethod(username, config.Password ?? string.Empty)];
    }

    private sealed class SshTunnel : ISshTunnel
    {
        private readonly SshClient _client;
        private readonly ForwardedPortLocal _port;

        public SshTunnel(SshClient client, ForwardedPortLocal port)
        {
            _client = client;
            _port = port;
        }

        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>本地转发端点（host:port），Redis 客户端应连接此地址。</summary>
        public string LocalEndpoint => $"{_port.BoundHost}:{_port.BoundPort}";

        public bool IsActive => _client.IsConnected && _port.IsStarted;

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_port.IsStarted) _port.Stop();
                if (_client.IsConnected) _client.Disconnect();
            }
            catch
            {
                // 关闭失败不抛出
            }
            _client.Dispose();
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
