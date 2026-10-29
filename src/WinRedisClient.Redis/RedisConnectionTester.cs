using System.Diagnostics;
using StackExchange.Redis;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Redis;

/// <summary>连接测试 — 创建临时会话验证认证/网络，随后立即释放，不影响当前连接。</summary>
public sealed class RedisConnectionTester : IConnectionTester
{
    private readonly IRedisSessionFactory _factory;

    public RedisConnectionTester(IRedisSessionFactory factory)
    {
        _factory = factory;
    }

    public async Task<ConnectionTestResult> TestAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        IRedisSession? session = null;

        try
        {
            session = await _factory.CreateAsync(profile, cancellationToken).ConfigureAwait(false);

            if (session is not RedisSession redisSession)
                throw new InvalidOperationException(Loc.T("无法识别 Redis 会话实现"));

            await redisSession.Db.PingAsync().ConfigureAwait(false);
            stopwatch.Stop();

            string? version = null;
            try
            {
                var info = await redisSession.Db.ExecuteAsync("INFO", "server").ConfigureAwait(false);
                var snapshot = Core.Services.InfoParser.Parse(info.ToString() ?? string.Empty);
                version = snapshot.Get("redis_version");
            }
            catch
            {
                // INFO 被 ACL 限制时仍视为连接成功
            }

            return new ConnectionTestResult(
                true,
                stopwatch.Elapsed,
                version,
                version is null ? Loc.T("连接成功（无法读取版本信息）") : Loc.F("连接成功 · Redis {0}", version));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ConnectionTestResult(
                false,
                stopwatch.Elapsed,
                null,
                RedisErrors.Humanize(ex),
                ex.Message);
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal static class RedisErrors
{
    public static string Humanize(Exception ex) => ex switch
    {
        RedisConnectionException => Loc.T("无法连接 Redis：请检查 Host/Port、网络、防火墙或 SSH 隧道配置"),
        RedisTimeoutException => Loc.T("连接超时：Redis 未在超时时间内响应"),
        RedisServerException server when server.Message.Contains("NOAUTH", StringComparison.OrdinalIgnoreCase)
            => Loc.T("认证失败：该实例需要密码"),
        RedisServerException server when server.Message.Contains("WRONGPASS", StringComparison.OrdinalIgnoreCase)
            => Loc.T("认证失败：用户名或密码错误"),
        RedisServerException server when server.Message.Contains("NOPERM", StringComparison.OrdinalIgnoreCase)
            => Loc.T("权限不足：当前用户无权执行该操作"),
        RedisServerException server when server.Message.Contains("READONLY", StringComparison.OrdinalIgnoreCase)
            => Loc.T("目标实例为只读副本，无法写入"),
        System.Net.Sockets.SocketException => Loc.T("网络连接失败：目标主机不可达或端口未开放"),
        var other when other.GetType().Name == "SshAuthenticationException" => Loc.T("SSH 认证失败：请检查 SSH 用户名/密码"),
        var other when other.GetType().Name == "SshConnectionException" => Loc.T("SSH 连接失败：请检查跳板机配置与网络"),
        NotSupportedException => ex.Message,
        ArgumentException => ex.Message,
        _ => ex.Message,
    };

    public static (string Code, string Message) Normalize(Exception ex)
        => ex switch
        {
            RedisConnectionException => ("connection_error", Humanize(ex)),
            RedisTimeoutException => ("timeout", Humanize(ex)),
            RedisServerException server => NormalizeServer(server.Message),
            RedisException => ("redis_error", ex.Message),
            OperationCanceledException => ("cancelled", Loc.T("操作已取消")),
            _ => ("unknown_error", ex.Message),
        };

    private static (string Code, string Message) NormalizeServer(string message)
    {
        if (message.Contains("NOAUTH", StringComparison.OrdinalIgnoreCase)) return ("authentication_required", Loc.T("该实例需要密码认证"));
        if (message.Contains("WRONGPASS", StringComparison.OrdinalIgnoreCase)) return ("authentication_failed", Loc.T("用户名或密码错误"));
        if (message.Contains("NOPERM", StringComparison.OrdinalIgnoreCase)) return ("no_permission", Loc.T("当前用户无权执行该命令"));
        if (message.Contains("WRONGTYPE", StringComparison.OrdinalIgnoreCase)) return ("wrong_type", Loc.T("Key 类型不匹配，请刷新后重试"));
        if (message.Contains("READONLY", StringComparison.OrdinalIgnoreCase)) return ("server_readonly", Loc.T("目标实例为只读副本"));
        if (message.Contains("OOM", StringComparison.OrdinalIgnoreCase)) return ("out_of_memory", Loc.T("Redis 内存不足，命令被拒绝"));
        if (message.Contains("LOADING", StringComparison.OrdinalIgnoreCase)) return ("loading", Loc.T("Redis 正在加载数据，请稍后重试"));
        if (message.Contains("unknown command", StringComparison.OrdinalIgnoreCase)) return ("unknown_command", Loc.T("该 Redis 版本不支持此命令"));
        return ("redis_error", message);
    }
}
