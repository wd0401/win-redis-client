using System.Text;
using StackExchange.Redis;
using WinRedisClient.Core.Abstractions;

namespace WinRedisClient.Redis;

/// <summary>按类型读取并序列化 Value 预览。</summary>
public sealed class ValuePreviewService : IValuePreviewService
{
    public async Task<ValuePreview> PreviewAsync(
        IRedisSession session,
        string key,
        PreviewOptions options,
        CancellationToken cancellationToken = default)
    {
        if (session is not RedisSession rs)
            return new ValuePreview(0, [], options.Offset, false, false);

        var db = rs.Db;
        var type = await db.KeyTypeAsync(key).ConfigureAwait(false);

        var data = type switch
        {
            RedisType.String => ((byte[]?)await db.StringGetAsync(key).ConfigureAwait(false)) ?? [],
            RedisType.Hash => SerializeHash(await db.HashGetAllAsync(key).ConfigureAwait(false)),
            RedisType.List => SerializeList(await db.ListRangeAsync(key, 0, -1).ConfigureAwait(false)),
            RedisType.Set => SerializeSet(await db.SetMembersAsync(key).ConfigureAwait(false)),
            RedisType.SortedSet => SerializeZSet(await db.SortedSetRangeByRankWithScoresAsync(key).ConfigureAwait(false)),
            RedisType.Stream => SerializeStream(await db.StreamRangeAsync(key).ConfigureAwait(false)),
            _ => Array.Empty<byte>(),
        };

        var total = data.Length;
        var truncated = total > options.MaxBytes;
        var slice = truncated ? data.AsSpan(0, (int)options.MaxBytes).ToArray() : data;

        return new ValuePreview(total, slice, options.Offset, truncated, false);
    }

    private static byte[] SerializeHash(HashEntry[] entries)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < entries.Length; i++)
            sb.AppendLine($"{i + 1}) {entries[i].Name} = {entries[i].Value}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] SerializeList(RedisValue[] values)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
            sb.AppendLine($"{i + 1}) {values[i]}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] SerializeSet(RedisValue[] members)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < members.Length; i++)
            sb.AppendLine($"{i + 1}) {members[i]}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] SerializeZSet(SortedSetEntry[] entries)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < entries.Length; i++)
            sb.AppendLine($"{i + 1}) {entries[i].Element} (score: {entries[i].Score})");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] SerializeStream(StreamEntry[] entries)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            sb.Append(entry.Id).Append(' ');
            foreach (var f in entry.Values)
                sb.Append(f.Name).Append('=').Append(f.Value).Append(' ');
            sb.AppendLine();
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
