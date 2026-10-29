using StackExchange.Redis;
using WinRedisClient.Core.Abstractions;

namespace WinRedisClient.Redis;

/// <summary>批量读取 Key 类型与 TTL 元数据。</summary>
public sealed class KeyMetadataService : IKeyMetadataService
{
    public async Task<IReadOnlyList<KeyMetadata>> GetMetadataAsync(
        IRedisSession session,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default)
    {
        if (session is not RedisSession rs || keys.Count == 0)
            return [];

        var db = rs.Db;
        var results = new List<KeyMetadata>(keys.Count);

        foreach (var key in keys)
        {
            try
            {
                var type = await db.KeyTypeAsync(key).ConfigureAwait(false);
                if (type == RedisType.None)
                {
                    results.Add(new KeyMetadata(key, "none", null, DateTimeOffset.UtcNow, "key not found"));
                    continue;
                }

                var ttl = await db.KeyTimeToLiveAsync(key).ConfigureAwait(false);
                results.Add(new KeyMetadata(key, MapType(type), ttl, DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                results.Add(new KeyMetadata(key, "unknown", null, DateTimeOffset.UtcNow, ex.Message));
            }
        }

        return results;
    }

    private static string MapType(RedisType type) => type switch
    {
        RedisType.String => "string",
        RedisType.Hash => "hash",
        RedisType.List => "list",
        RedisType.Set => "set",
        RedisType.SortedSet => "zset",
        RedisType.Stream => "stream",
        RedisType.None => "none",
        _ => "unknown",
    };
}
