using StackExchange.Redis;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;

namespace WinRedisClient.Redis;

/// <summary>Redis 会话 — 包装 ConnectionMultiplexer，可选持有 SSH 隧道。</summary>
public sealed class RedisSession : IRedisSession
{
    private readonly IConnectionMultiplexer _mux;
    private readonly ISshTunnel? _tunnel;
    private int _database;

    public Guid Id { get; }
    public ConnectionProfile Profile { get; }
    public bool IsConnected => _mux.IsConnected;

    /// <summary>当前数据库索引（0-15）。</summary>
    public int Database
    {
        get => _database;
        set => _database = Math.Clamp(value, 0, 15);
    }

    internal IConnectionMultiplexer Multiplexer => _mux;
    internal IDatabase Db => _mux.GetDatabase(_database);

    public RedisSession(Guid id, ConnectionProfile profile, IConnectionMultiplexer mux, ISshTunnel? tunnel = null)
    {
        Id = id;
        Profile = profile;
        _mux = mux;
        _tunnel = tunnel;
        _database = Math.Clamp(profile.Database, 0, 15);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _mux.CloseAsync().ConfigureAwait(false);
        }
        catch
        {
            // 关闭失败不阻断释放
        }

        _mux.Dispose();

        if (_tunnel is not null)
            await _tunnel.DisposeAsync().ConfigureAwait(false);
    }
}
