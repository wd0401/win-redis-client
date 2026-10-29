using System.Runtime.CompilerServices;
using StackExchange.Redis;
using WinRedisClient.Core.Abstractions;

namespace WinRedisClient.Redis;

/// <summary>基于 SCAN 的增量 Key 扫描。</summary>
public sealed class KeyScanService : IKeyScanService
{
    public async IAsyncEnumerable<KeyScanResult> ScanAsync(
        IRedisSession session,
        string? pattern,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (session is not RedisSession rs)
            yield break;

        var endpoint = rs.Multiplexer.GetEndPoints().FirstOrDefault();
        if (endpoint is null)
            yield break;

        var server = rs.Multiplexer.GetServer(endpoint);

        foreach (var key in server.Keys(database: rs.Database, pattern: pattern ?? "*", pageSize: batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new KeyScanResult((byte[])key!, key.ToString());
        }
    }
}
