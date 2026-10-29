using System.Diagnostics;
using System.Globalization;
using System.Text;
using StackExchange.Redis;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;
using WinRedisClient.Core.Services;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Redis;

/// <summary>Redis 高层门面 — 管理当前会话并实现浏览、读取、受控写入与监控。</summary>
public sealed class RedisService : IRedisService
{
    private readonly IRedisSessionFactory _factory;
    private RedisSession? _session;

    public RedisService(IRedisSessionFactory factory)
    {
        _factory = factory;
    }

    public bool IsConnected => _session?.IsConnected ?? false;
    public ConnectionProfile? CurrentProfile => _session?.Profile;
    public int Database => _session?.Database ?? 0;

    private RedisSession Session => _session
        ?? throw new InvalidOperationException(Loc.T("尚未连接 Redis"));

    public async Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);

        var session = await _factory.CreateAsync(profile, cancellationToken).ConfigureAwait(false);
        if (session is not RedisSession redisSession)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(Loc.T("无法识别 Redis 会话实现"));
        }

        try
        {
            await redisSession.Db.PingAsync().ConfigureAwait(false);
            redisSession.Database = profile.Database;
            _session = redisSession;
        }
        catch
        {
            await redisSession.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        var session = _session;
        _session = null;
        if (session is not null)
            await session.DisposeAsync().ConfigureAwait(false);
    }

    public bool SelectDatabase(int db)
    {
        if (_session is null || db is < 0 or > 15) return false;
        _session.Database = db;
        return true;
    }

    public async Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return await Session.Db.PingAsync().ConfigureAwait(false);
    }

    public async Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var result = await Session.Db.ExecuteAsync("INFO", "server").ConfigureAwait(false);
        return InfoParser.Parse(result.ToString() ?? string.Empty).Get("redis_version");
    }

    public async Task<long> GetDatabaseSizeAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var result = await Session.Db.ExecuteAsync("DBSIZE").ConfigureAwait(false);
        return ParseLong(result) ?? 0;
    }

    public async Task<IReadOnlyDictionary<int, long>> GetDatabaseKeyCountsAsync(
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var result = await Session.Db.ExecuteAsync("INFO", "keyspace").ConfigureAwait(false);
        return InfoParser.ParseDatabaseKeyCounts(result.ToString() ?? string.Empty);
    }

    // ── 浏览 ────────────────────────────────────────────────

    public async Task<ScanPage> ScanPageAsync(
        string? pattern,
        int batchSize,
        long cursor,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null) return new ScanPage([], 0, true);

        var args = new List<object> { cursor, "COUNT", Math.Clamp(batchSize, 1, 10000) };
        if (!string.IsNullOrEmpty(pattern) && pattern != "*")
        {
            args.Add("MATCH");
            args.Add(pattern);
        }

        var result = await session.Db.ExecuteAsync("SCAN", args.ToArray()).ConfigureAwait(false);
        var nextCursor = ParseLong(result[0]) ?? 0;
        var keys = new List<KeyScanResult>();
        var keysResult = result[1];

        if (!keysResult.IsNull)
        {
            for (var i = 0; i < keysResult.Length; i++)
            {
                var value = (RedisValue)keysResult[i];
                var bytes = (byte[]?)value ?? [];
                keys.Add(new KeyScanResult(bytes, value.ToString()));
            }
        }

        return new ScanPage(keys, nextCursor, nextCursor == 0);
    }

    public async Task<IReadOnlyList<KeyMetadata>> GetMetadataBatchAsync(
        IReadOnlyList<string> keys,
        bool includeSize = true,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null || keys.Count == 0) return [];

        var db = session.Db;
        var batch = db.CreateBatch();
        var typeTasks = new Task<RedisType>[keys.Count];
        var ttlTasks = new Task<TimeSpan?>[keys.Count];
        var memoryTasks = includeSize ? new Task<RedisResult>[keys.Count] : null;

        for (var i = 0; i < keys.Count; i++)
        {
            typeTasks[i] = batch.KeyTypeAsync(keys[i]);
            ttlTasks[i] = batch.KeyTimeToLiveAsync(keys[i]);
            if (memoryTasks is not null)
                memoryTasks[i] = batch.ExecuteAsync("MEMORY", "USAGE", keys[i]);
        }
        batch.Execute();

        var result = new List<KeyMetadata>(keys.Count);
        var types = new RedisType[keys.Count];

        for (var i = 0; i < keys.Count; i++)
        {
            RedisType type;
            try { type = await typeTasks[i].ConfigureAwait(false); }
            catch (Exception ex)
            {
                result.Add(new KeyMetadata(keys[i], "unknown", null, DateTimeOffset.Now, ex.Message));
                continue;
            }
            types[i] = type;

            if (type == RedisType.None)
            {
                result.Add(new KeyMetadata(keys[i], "none", null, DateTimeOffset.Now, Loc.T("Key 不存在或已过期")));
                continue;
            }

            TimeSpan? ttl = null;
            try { ttl = await ttlTasks[i].ConfigureAwait(false); }
            catch { /* TTL 获取失败不阻断列表 */ }

            long? size = null;
            if (memoryTasks is not null)
            {
                try
                {
                    var memory = await memoryTasks[i].ConfigureAwait(false);
                    size = ParseLong(memory);
                }
                catch { /* 旧版本或 ACL 限制时大小不可用 */ }
            }

            result.Add(new KeyMetadata(keys[i], MapType(type), ttl, DateTimeOffset.Now,
                null, size, null));
        }

        if (!includeSize) return result;

        // 元素数量：第二批次（只对存在的 Key）
        var countBatch = db.CreateBatch();
        var countTasks = new Task<RedisResult>?[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            var command = types[i] switch
            {
                RedisType.String => "STRLEN",
                RedisType.Hash => "HLEN",
                RedisType.List => "LLEN",
                RedisType.Set => "SCARD",
                RedisType.SortedSet => "ZCARD",
                RedisType.Stream => "XLEN",
                _ => null,
            };
            if (command is not null)
                countTasks[i] = countBatch.ExecuteAsync(command, keys[i]);
        }
        countBatch.Execute();

        for (var i = 0; i < keys.Count; i++)
        {
            if (countTasks[i] is null) continue;
            try
            {
                var count = ParseLong(await countTasks[i]!.ConfigureAwait(false));
                result[i] = result[i] with { ElementCount = count };
            }
            catch { /* 元素数量不可用 */ }
        }

        return result;
    }

    public async Task<KeyMetadata?> GetMetadataAsync(
        string key,
        bool includeSize = true,
        CancellationToken cancellationToken = default)
    {
        var list = await GetMetadataBatchAsync([key], includeSize, cancellationToken).ConfigureAwait(false);
        return list.FirstOrDefault();
    }

    public async Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return await Session.Db.KeyExistsAsync(key).ConfigureAwait(false);
    }

    public async Task<string?> GetKeyTypeAsync(string key, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var type = await Session.Db.KeyTypeAsync(key).ConfigureAwait(false);
        return MapType(type);
    }

    // ── 值读取 ──────────────────────────────────────────────

    public async Task<ValuePreview?> PreviewAsync(
        string key,
        PreviewOptions options,
        CancellationToken cancellationToken = default)
    {
        var session = _session;
        if (session is null) return null;

        var type = await session.Db.KeyTypeAsync(key).ConfigureAwait(false);
        if (type == RedisType.None) return null;

        if (type == RedisType.String)
            return await GetStringBytesAsync(key, options.MaxBytes, cancellationToken).ConfigureAwait(false);

        var page = await GetCollectionPageAsync(key, MapType(type), null, 200, false, cancellationToken)
            .ConfigureAwait(false);
        if (page.Error is not null)
            return new ValuePreview(0, Encoding.UTF8.GetBytes(page.Error), 0, false, false);

        var sb = new StringBuilder();
        foreach (var item in page.Items)
            sb.AppendLine(FormatElement(MapType(type), item));

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var max = (int)Math.Clamp(options.MaxBytes, 1, int.MaxValue);
        var truncated = bytes.Length > max;
        var slice = truncated ? bytes[..max] : bytes;
        return new ValuePreview(bytes.Length, slice, 0, truncated, false);
    }

    public async Task<ValuePreview?> GetStringBytesAsync(
        string key,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null) return null;

        var db = session.Db;
        var type = await db.KeyTypeAsync(key).ConfigureAwait(false);
        if (type == RedisType.None) return null;
        if (type != RedisType.String)
            throw new InvalidOperationException(Loc.T("Key 不是 String 类型"));

        var total = await db.StringLengthAsync(key).ConfigureAwait(false);
        var max = (long)Math.Clamp(maxBytes, 1, 64L * 1024 * 1024);
        var value = await db.StringGetRangeAsync(key, 0, max - 1).ConfigureAwait(false);
        var data = (byte[]?)value ?? [];
        return new ValuePreview(total, data, 0, total > data.Length, false);
    }

    public async Task<CollectionPage> GetCollectionPageAsync(
        string key,
        string type,
        string? cursor,
        int count,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null) return new CollectionPage([], null, true, 0, Loc.T("尚未连接 Redis"));

        var db = session.Db;
        count = Math.Clamp(count, 1, 1000);

        try
        {
            var actual = await db.KeyTypeAsync(key).ConfigureAwait(false);
            if (actual == RedisType.None)
                return new CollectionPage([], null, true, 0, Loc.T("Key 不存在或已过期"));
            if (!string.Equals(MapType(actual), type, StringComparison.OrdinalIgnoreCase))
                return new CollectionPage([], null, true, 0,
                    Loc.F("Key 类型已变为 {0}，请刷新", MapType(actual)));

            return type.ToLowerInvariant() switch
            {
                "hash" => await GetHashPageAsync(db, key, cursor, count).ConfigureAwait(false),
                "set" => await GetSetPageAsync(db, key, cursor, count).ConfigureAwait(false),
                "list" => await GetListPageAsync(db, key, cursor, count, descending).ConfigureAwait(false),
                "zset" => await GetZSetPageAsync(db, key, cursor, count, descending).ConfigureAwait(false),
                "stream" => await GetStreamPageAsync(db, key, cursor, count).ConfigureAwait(false),
                _ => new CollectionPage([], null, true, 0, Loc.F("不支持的类型：{0}", type)),
            };
        }
        catch (Exception ex)
        {
            var (_, message) = RedisErrors.Normalize(ex);
            return new CollectionPage([], null, true, 0, message);
        }
    }

    private static string DecodeValue(RedisValue value)
        => TextValueDecoder.DecodeForDisplay((byte[]?)value ?? []);

    private static string DecodeValue(RedisResult value)
        => DecodeValue((RedisValue)value);

    private static async Task<CollectionPage> GetHashPageAsync(
        IDatabase db, string key, string? cursor, int count)
    {
        var result = await db.ExecuteAsync("HSCAN", key, string.IsNullOrEmpty(cursor) ? "0" : cursor, "COUNT", count)
            .ConfigureAwait(false);
        var next = result[0].ToString() ?? "0";
        var items = new List<ElementData>();
        var values = result[1];

        if (!values.IsNull)
        {
            for (var i = 0; i + 1 < values.Length; i += 2)
                items.Add(new ElementData(
                    DecodeValue(values[i]),
                    DecodeValue(values[i + 1]),
                    string.Empty));
        }

        var total = await db.HashLengthAsync(key).ConfigureAwait(false);
        return new CollectionPage(items, next == "0" ? null : next, next == "0", total);
    }

    private static async Task<CollectionPage> GetSetPageAsync(
        IDatabase db, string key, string? cursor, int count)
    {
        var result = await db.ExecuteAsync("SSCAN", key, string.IsNullOrEmpty(cursor) ? "0" : cursor, "COUNT", count)
            .ConfigureAwait(false);
        var next = result[0].ToString() ?? "0";
        var items = new List<ElementData>();
        var values = result[1];

        if (!values.IsNull)
        {
            for (var i = 0; i < values.Length; i++)
            {
                items.Add(new ElementData(
                    DecodeValue(values[i]), string.Empty, string.Empty));
            }
        }

        var total = await db.SetLengthAsync(key).ConfigureAwait(false);
        return new CollectionPage(items, next == "0" ? null : next, next == "0", total);
    }

    private static async Task<CollectionPage> GetListPageAsync(
        IDatabase db, string key, string? cursor, int count, bool descending)
    {
        var total = await db.ListLengthAsync(key).ConfigureAwait(false);
        var offset = Math.Max(0, ParseLong(cursor) ?? 0);
        if (offset >= total)
            return new CollectionPage([], null, true, total);

        var items = new List<ElementData>();
        if (descending)
        {
            var start = total - 1 - offset;
            var end = Math.Max(0, start - count + 1);
            var values = await db.ListRangeAsync(key, start, end).ConfigureAwait(false);
            for (var i = 0; i < values.Length; i++)
                items.Add(new ElementData((start - i).ToString(CultureInfo.InvariantCulture),
                    DecodeValue(values[i]), string.Empty));
        }
        else
        {
            var end = Math.Min(total - 1, offset + count - 1);
            var values = await db.ListRangeAsync(key, offset, end).ConfigureAwait(false);
            for (var i = 0; i < values.Length; i++)
                items.Add(new ElementData((offset + i).ToString(CultureInfo.InvariantCulture),
                    DecodeValue(values[i]), string.Empty));
        }

        var nextOffset = offset + items.Count;
        var complete = nextOffset >= total || items.Count == 0;
        return new CollectionPage(items, complete ? null : nextOffset.ToString(CultureInfo.InvariantCulture),
            complete, total);
    }

    private static async Task<CollectionPage> GetZSetPageAsync(
        IDatabase db, string key, string? cursor, int count, bool descending)
    {
        var total = await db.SortedSetLengthAsync(key).ConfigureAwait(false);
        var offset = Math.Max(0, ParseLong(cursor) ?? 0);
        if (offset >= total)
            return new CollectionPage([], null, true, total);

        var end = Math.Min(total - 1, offset + count - 1);
        var entries = await db.SortedSetRangeByRankWithScoresAsync(
            key, offset, end, descending ? Order.Descending : Order.Ascending).ConfigureAwait(false);

        var items = entries
            .Select(e => new ElementData(
                DecodeValue(e.Element),
                string.Empty,
                e.Score.ToString("0.############", CultureInfo.InvariantCulture)))
            .ToList();

        var nextOffset = offset + items.Count;
        var complete = nextOffset >= total || items.Count == 0;
        return new CollectionPage(items, complete ? null : nextOffset.ToString(CultureInfo.InvariantCulture),
            complete, total);
    }

    private static async Task<CollectionPage> GetStreamPageAsync(
        IDatabase db, string key, string? cursor, int count)
    {
        var start = string.IsNullOrEmpty(cursor) ? "-" : cursor;
        var result = await db.ExecuteAsync("XRANGE", key, start, "+", "COUNT", count + 1).ConfigureAwait(false);
        var parsed = new List<ElementData>();

        if (!result.IsNull)
        {
            for (var i = 0; i < result.Length; i++)
            {
                var entry = result[i];
                if (entry.IsNull || entry.Length < 2) continue;

                var id = entry[0].ToString();
                if (start != "-" && id == start) continue;

                var fields = entry[1];
                var parts = new List<string>();
                if (!fields.IsNull)
                {
                    for (var f = 0; f + 1 < fields.Length; f += 2)
                        parts.Add($"{DecodeValue(fields[f])}={DecodeValue(fields[f + 1])}");
                }
                parsed.Add(new ElementData(id, string.Join("; ", parts), string.Empty));
            }
        }

        var hasMore = parsed.Count > count;
        var items = parsed.Take(count).ToList();
        var total = await db.StreamLengthAsync(key).ConfigureAwait(false);
        var next = hasMore && items.Count > 0 ? items[^1].Field : null;
        return new CollectionPage(items, next, !hasMore, total);
    }

    public async Task<KeyContent> GetKeyContentAsync(
        string key,
        int maxElements,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        var meta = await GetMetadataAsync(key, false, cancellationToken).ConfigureAwait(false);
        if (meta is null || meta.Type == "none")
            return new KeyContent(key, "none", null, null, [], false, Loc.T("Key 不存在或已过期"));
        if (meta.Error is not null)
            return new KeyContent(key, meta.Type, meta.TimeToLive, null, [], false, meta.Error);

        if (meta.Type == "string")
        {
            var preview = await GetStringBytesAsync(key, maxBytes, cancellationToken).ConfigureAwait(false);
            var text = preview is null ? string.Empty : TextValueDecoder.DecodeForDisplay(preview.Data);
            return new KeyContent(key, "string", meta.TimeToLive, text, [],
                preview?.IsTruncated ?? false);
        }

        var page = await GetCollectionPageAsync(key, meta.Type, null,
            Math.Clamp(maxElements, 1, 5000), false, cancellationToken).ConfigureAwait(false);
        return new KeyContent(key, meta.Type, meta.TimeToLive, null, page.Items,
            !page.IsComplete || page.TotalCount > page.Items.Count, page.Error);
    }

    public async Task<BigKeyInfo?> GetBigKeyInfoAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var meta = await GetMetadataAsync(key, true, cancellationToken).ConfigureAwait(false);
        if (meta is null || meta.Type == "none") return null;
        return new BigKeyInfo(key, meta.Type, meta.SizeBytes, meta.ElementCount);
    }

    // ── 命令与写入 ──────────────────────────────────────────

    public Task<CommandResult> ExecuteAsync(
        string command,
        CommandRiskLevel risk = CommandRiskLevel.ReadOnly,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
        => ExecuteArgsAsync(command, [], risk, confirmed, cancellationToken);

    public async Task<CommandResult> ExecuteArgsAsync(
        string command,
        string[] args,
        CommandRiskLevel risk = CommandRiskLevel.ReadOnly,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        var session = _session;
        if (session is null)
            return Fail("not_connected", Loc.T("尚未连接 Redis"));

        command = command.Trim();
        if (command.Length == 0)
            return Fail("invalid_command", Loc.T("命令不能为空"));

        var effective = CommandRiskClassifier.Effective(command, args, risk);
        var readonlyError = CheckReadOnly(session, effective);
        if (readonlyError is not null) return readonlyError;

        if (effective >= CommandRiskLevel.BatchOrScript && !confirmed)
            return Fail("confirmation_required", Loc.T("该命令属于批量/脚本/阻塞命令，需要确认后执行"));

        if (session.Profile.Environment == EnvironmentTag.Production
            && effective > CommandRiskLevel.ReadOnly
            && !confirmed && IsDestructive(command))
            return Fail("production_confirmation_required", Loc.T("生产环境执行破坏性命令需要额外确认"));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var objects = args.Cast<object>().ToArray();
            var result = objects.Length == 0
                ? await session.Db.ExecuteAsync(command).ConfigureAwait(false)
                : await session.Db.ExecuteAsync(command, objects).ConfigureAwait(false);
            stopwatch.Stop();
            return new CommandResult
            {
                Outcome = ExecutionOutcome.Success,
                Output = FormatResult(result),
                Elapsed = stopwatch.Elapsed,
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new CommandResult
            {
                Outcome = ExecutionOutcome.Cancelled,
                ErrorCode = "cancelled",
                ErrorMessage = Loc.T("操作已取消"),
                Elapsed = stopwatch.Elapsed,
            };
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            stopwatch.Stop();
            var (code, message) = RedisErrors.Normalize(ex);
            if (effective > CommandRiskLevel.ReadOnly)
                message += Loc.T("；写入命令可能已发送，结果未知，请刷新确认后再决定是否重试");
            return new CommandResult
            {
                Outcome = effective > CommandRiskLevel.ReadOnly
                    ? ExecutionOutcome.OutcomeUnknown
                    : ExecutionOutcome.Failure,
                ErrorCode = code,
                ErrorMessage = message,
                Elapsed = stopwatch.Elapsed,
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var (code, message) = RedisErrors.Normalize(ex);
            return new CommandResult
            {
                Outcome = ExecutionOutcome.Failure,
                ErrorCode = code,
                ErrorMessage = message,
                Elapsed = stopwatch.Elapsed,
            };
        }
    }

    public async Task<CommandResult> SetStringAsync(
        string key,
        byte[] value,
        TimeSpan? ttl = null,
        bool preserveTtl = true,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null) return Fail("not_connected", Loc.T("尚未连接 Redis"));

        var readonlyError = CheckReadOnly(session, CommandRiskLevel.SingleWrite);
        if (readonlyError is not null) return readonlyError;

        var productionError = CheckProductionConfirmation(session, confirmed);
        if (productionError is not null) return productionError;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (ttl is { } expiry)
            {
                var ms = Math.Max(1, (long)expiry.TotalMilliseconds);
                await session.Db.ExecuteAsync("SET", key, (RedisValue)value, "PX", ms).ConfigureAwait(false);
            }
            else if (preserveTtl)
            {
                var remaining = await session.Db.KeyTimeToLiveAsync(key).ConfigureAwait(false);
                await session.Db.StringSetAsync(key, value).ConfigureAwait(false);
                if (remaining is { } keep && keep > TimeSpan.Zero)
                {
                    var ms = Math.Max(1, (long)keep.TotalMilliseconds);
                    await session.Db.ExecuteAsync("PEXPIRE", key, ms).ConfigureAwait(false);
                }
            }
            else
            {
                await session.Db.StringSetAsync(key, value).ConfigureAwait(false);
            }

            stopwatch.Stop();
            return Success("OK", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return WriteFailure(ex, stopwatch.Elapsed);
        }
    }

    public async Task<CommandResult> CreateKeyAsync(
        string key,
        string type,
        IReadOnlyList<string> initialValues,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null) return Fail("not_connected", Loc.T("尚未连接 Redis"));

        var readonlyError = CheckReadOnly(session, CommandRiskLevel.SingleWrite);
        if (readonlyError is not null) return readonlyError;

        type = type.ToLowerInvariant();
        var validation = ValidateInitialValues(type, initialValues);
        if (validation is not null) return Fail("invalid_arguments", validation);

        const string script = """
            if redis.call('EXISTS', KEYS[1]) == 1 then return 0 end
            local t = ARGV[1]
            local ttl = tonumber(ARGV[2])
            if t == 'string' then
                redis.call('SET', KEYS[1], ARGV[3])
            elseif t == 'hash' then
                redis.call('HSET', KEYS[1], ARGV[3], ARGV[4])
            elseif t == 'list' then
                for i = 3, #ARGV do redis.call('RPUSH', KEYS[1], ARGV[i]) end
            elseif t == 'set' then
                redis.call('SADD', KEYS[1], ARGV[3])
            elseif t == 'zset' then
                redis.call('ZADD', KEYS[1], ARGV[4], ARGV[3])
            elseif t == 'stream' then
                redis.call('XADD', KEYS[1], '*', ARGV[3], ARGV[4])
            else
                return -2
            end
            if ttl and ttl > 0 then redis.call('PEXPIRE', KEYS[1], ttl) end
            return 1
            """;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var values = new List<RedisValue>
            {
                type,
                (long?)ttl?.TotalMilliseconds ?? 0,
            };
            values.AddRange(initialValues.Select(v => (RedisValue)v));

            var result = await session.Db.ScriptEvaluateAsync(
                script, new RedisKey[] { key }, values.ToArray()).ConfigureAwait(false);
            stopwatch.Stop();

            var code = ParseLong(result) ?? 0;
            return code switch
            {
                1 => Success(key, stopwatch.Elapsed),
                0 => Fail("target_exists", Loc.T("Key 已存在；如需覆盖请明确选择覆盖"), stopwatch.Elapsed),
                -2 => Fail("unsupported_type", Loc.F("不支持创建类型 {0}", type), stopwatch.Elapsed),
                _ => Fail("create_failed", Loc.T("创建 Key 失败"), stopwatch.Elapsed),
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var (errorCode, message) = RedisErrors.Normalize(ex);
            return Fail(errorCode, message, stopwatch.Elapsed);
        }
    }

    public async Task<CommandResult> DeleteKeyAsync(
        string key,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null) return Fail("not_connected", Loc.T("尚未连接 Redis"));

        var readonlyError = CheckReadOnly(session, CommandRiskLevel.SingleWrite);
        if (readonlyError is not null) return readonlyError;

        var productionError = CheckProductionConfirmation(session, confirmed);
        if (productionError is not null) return productionError;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var deleted = await session.Db.KeyDeleteAsync(key).ConfigureAwait(false);
            stopwatch.Stop();
            return deleted
                ? Success("1", stopwatch.Elapsed)
                : Fail("key_not_found", Loc.T("Key 不存在或已被删除"), stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return WriteFailure(ex, stopwatch.Elapsed);
        }
    }

    public async Task<CommandResult> RenameKeyAsync(
        string oldKey,
        string newKey,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null) return Fail("not_connected", Loc.T("尚未连接 Redis"));

        if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
            return Fail("invalid_arguments", Loc.T("新 Key 名称与原名相同"));
        if (string.IsNullOrWhiteSpace(newKey))
            return Fail("invalid_arguments", Loc.T("新 Key 名称不能为空"));

        var readonlyError = CheckReadOnly(session, CommandRiskLevel.SingleWrite);
        if (readonlyError is not null) return readonlyError;

        var productionError = CheckProductionConfirmation(session, confirmed);
        if (productionError is not null) return productionError;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await session.Db.ExecuteAsync("RENAMENX", oldKey, newKey).ConfigureAwait(false);
            stopwatch.Stop();
            var code = ParseLong(result) ?? 0;
            return code switch
            {
                1 => Success(newKey, stopwatch.Elapsed),
                0 => Fail("target_exists", Loc.T("目标 Key 已存在，未执行重命名"), stopwatch.Elapsed),
                _ => Fail("key_not_found", Loc.T("源 Key 不存在"), stopwatch.Elapsed),
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return WriteFailure(ex, stopwatch.Elapsed);
        }
    }

    public async Task<CommandResult> SetTtlAsync(
        string key,
        TimeSpan? ttl,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null) return Fail("not_connected", Loc.T("尚未连接 Redis"));

        var readonlyError = CheckReadOnly(session, CommandRiskLevel.SingleWrite);
        if (readonlyError is not null) return readonlyError;

        if (ttl is { } value && value <= TimeSpan.Zero)
            return Fail("invalid_arguments", Loc.T("TTL 必须大于 0；清除过期时间请选择永不过期"));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var exists = await session.Db.KeyExistsAsync(key).ConfigureAwait(false);
            if (!exists)
            {
                stopwatch.Stop();
                return Fail("key_not_found", Loc.T("Key 不存在或已被删除"), stopwatch.Elapsed);
            }

            if (ttl is { } expiry)
            {
                var ms = Math.Max(1, (long)expiry.TotalMilliseconds);
                await session.Db.ExecuteAsync("PEXPIRE", key, ms).ConfigureAwait(false);
            }
            else
            {
                await session.Db.ExecuteAsync("PERSIST", key).ConfigureAwait(false);
            }

            stopwatch.Stop();
            return Success("OK", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return WriteFailure(ex, stopwatch.Elapsed);
        }
    }

    public async Task<CommandResult> ListRemoveAtAsync(
        string key,
        long index,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null) return Fail("not_connected", Loc.T("尚未连接 Redis"));

        var readonlyError = CheckReadOnly(session, CommandRiskLevel.SingleWrite);
        if (readonlyError is not null) return readonlyError;

        var productionError = CheckProductionConfirmation(session, confirmed);
        if (productionError is not null) return productionError;

        const string script = """
            local v = redis.call('LINDEX', KEYS[1], ARGV[1])
            if not v then return 0 end
            local token = ARGV[2]
            redis.call('LSET', KEYS[1], ARGV[1], token)
            redis.call('LREM', KEYS[1], 1, token)
            return 1
            """;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var token = "__winredis_deleted_" + Guid.NewGuid().ToString("N") + "__";
            var result = await session.Db.ScriptEvaluateAsync(
                script, new RedisKey[] { key }, new RedisValue[] { index, token }).ConfigureAwait(false);
            stopwatch.Stop();
            var code = ParseLong(result) ?? 0;
            return code == 1
                ? Success("1", stopwatch.Elapsed)
                : Fail("index_out_of_range", Loc.T("该索引已不存在，列表可能已被其他客户端修改"), stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return WriteFailure(ex, stopwatch.Elapsed);
        }
    }

    public async Task<CommandResult> StreamAddAsync(
        string key,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = _session;
        if (session is null) return Fail("not_connected", Loc.T("尚未连接 Redis"));

        if (fields.Count == 0)
            return Fail("invalid_arguments", Loc.T("至少需要一个字段"));
        if (fields.Any(f => string.IsNullOrEmpty(f.Key)))
            return Fail("invalid_arguments", Loc.T("字段名不能为空"));

        var readonlyError = CheckReadOnly(session, CommandRiskLevel.SingleWrite);
        if (readonlyError is not null) return readonlyError;

        var args = new List<object> { key, "*" };
        foreach (var field in fields)
        {
            args.Add(field.Key);
            args.Add(field.Value);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await session.Db.ExecuteAsync("XADD", args.ToArray()).ConfigureAwait(false);
            stopwatch.Stop();
            return Success(result.ToString() ?? "OK", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return WriteFailure(ex, stopwatch.Elapsed);
        }
    }

    // ── 监控 ────────────────────────────────────────────────

    public async Task<InfoSnapshot> GetInfoAsync(
        string? section = null,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var result = string.IsNullOrWhiteSpace(section)
            ? await Session.Db.ExecuteAsync("INFO").ConfigureAwait(false)
            : await Session.Db.ExecuteAsync("INFO", section).ConfigureAwait(false);

        return InfoParser.Parse(result.ToString() ?? string.Empty, DateTimeOffset.Now);
    }

    public async Task<IReadOnlyList<SlowLogEntry>> GetSlowLogAsync(
        int count = 20,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var result = await Session.Db.ExecuteAsync("SLOWLOG", "GET", Math.Clamp(count, 1, 128))
            .ConfigureAwait(false);
        var entries = new List<SlowLogEntry>();

        if (result.IsNull || result.Resp2Type == ResultType.None) return entries;

        for (var i = 0; i < result.Length; i++)
        {
            var item = result[i];
            if (item.IsNull || item.Length < 4) continue;

            var id = ParseLong(item[0]) ?? 0;
            var timestamp = ParseLong(item[1]) ?? 0;
            var durationMicros = ParseLong(item[2]) ?? 0;

            var command = new List<string>();
            var commandArray = item[3];
            if (!commandArray.IsNull)
            {
                for (var c = 0; c < commandArray.Length; c++)
                    command.Add(commandArray[c].ToString());
            }

            var address = item.Length > 4 && !item[4].IsNull ? item[4].ToString() : null;
            var name = item.Length > 5 && !item[5].IsNull ? item[5].ToString() : null;

            entries.Add(new SlowLogEntry(
                id,
                DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime(),
                TimeSpan.FromMilliseconds(durationMicros / 1000.0),
                command,
                address,
                name));
        }

        return entries;
    }

    // ── 内部辅助 ────────────────────────────────────────────

    private static CommandResult? CheckReadOnly(RedisSession session, CommandRiskLevel risk)
        => session.Profile.ReadOnly && risk > CommandRiskLevel.ReadOnly
            ? Fail("readonly_mode", Loc.T("当前连接为只读模式，写操作已被阻止"))
            : null;

    private static CommandResult? CheckProductionConfirmation(RedisSession session, bool confirmed)
        => session.Profile.Environment == EnvironmentTag.Production && !confirmed
            ? Fail("production_confirmation_required", Loc.T("生产环境执行写操作需要额外确认"))
            : null;

    private static bool IsDestructive(string command)
        => command.ToUpperInvariant() is
            "DEL" or "UNLINK" or "FLUSHALL" or "FLUSHDB" or "SHUTDOWN" or
            "RENAME" or "RENAMENX" or "SWAPDB" or "MIGRATE" or "RESTORE" or
            "LTRIM" or "CONFIG" or "ACL" or "MODULE" or "REPLICAOF" or "SLAVEOF" or
            "FAILOVER" or "MOVE" or "XTRIM" or "EVAL" or "EVALSHA" or
            // 单元素删除/覆盖：生产环境同样属于破坏性写入
            "HDEL" or "SREM" or "ZREM" or "LREM" or "LSET" or "XDEL" or
            "SET" or "SETEX" or "PSETEX" or "GETSET" or "SETRANGE";

    private static string? ValidateInitialValues(string type, IReadOnlyList<string> values)
        => type switch
        {
            "string" when values.Count != 1 => Loc.T("String 需要 1 个初始值"),
            "hash" when values.Count != 2 => Loc.T("Hash 需要 1 个初始字段与值"),
            "list" when values.Count == 0 => Loc.T("List 至少需要 1 个元素"),
            "set" when values.Count == 0 => Loc.T("Set 至少需要 1 个成员"),
            "zset" when values.Count != 2 => Loc.T("ZSet 需要 1 个成员与 Score"),
            "zset" when !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                => Loc.T("ZSet Score 必须是数字"),
            "stream" when values.Count != 2 => Loc.T("Stream 需要 1 个初始字段与值"),
            "string" or "hash" or "list" or "set" or "zset" or "stream" => null,
            _ => Loc.F("不支持的类型：{0}", type),
        };

    private static CommandResult Success(string? output, TimeSpan elapsed) => new()
    {
        Outcome = ExecutionOutcome.Success,
        Output = output,
        Elapsed = elapsed,
    };

    private static CommandResult Fail(string code, string message, TimeSpan elapsed = default) => new()
    {
        Outcome = ExecutionOutcome.Failure,
        ErrorCode = code,
        ErrorMessage = message,
        Elapsed = elapsed,
    };

    private static CommandResult WriteFailure(Exception ex, TimeSpan elapsed)
    {
        var (code, message) = RedisErrors.Normalize(ex);
        if (ex is RedisConnectionException or RedisTimeoutException)
        {
            return new CommandResult
            {
                Outcome = ExecutionOutcome.OutcomeUnknown,
                ErrorCode = code,
                ErrorMessage = message + Loc.T("；写入结果未知，请刷新后确认，勿直接重试"),
                Elapsed = elapsed,
            };
        }

        return Fail(code, message, elapsed);
    }

    private static long? ParseLong(RedisResult result)
    {
        if (result.IsNull) return null;
        var text = result.ToString();
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static long? ParseLong(string? text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

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

    private static string FormatElement(string type, ElementData item) => type switch
    {
        "hash" => $"{item.Field} = {item.Value}",
        "list" => $"[{item.Field}] {item.Value}",
        "set" => item.Field,
        "zset" => $"{item.Field} (score: {item.Score})",
        "stream" => $"{item.Field} {item.Value}",
        _ => item.Value,
    };

    private static string FormatResult(RedisResult result)
    {
        if (result.IsNull) return "(nil)";
        return FormatResultCore(result, 0);
    }

    private static string FormatResultCore(RedisResult result, int depth)
    {
        if (result.IsNull) return "(nil)";

        if (result.Resp2Type is ResultType.Array or ResultType.Set)
        {
            var length = result.Length;
            if (length == 0) return "(empty array)";

            var limit = Math.Min(length, 500);
            var sb = new StringBuilder();
            var indent = new string(' ', depth);
            for (var i = 0; i < limit; i++)
            {
                if (i > 0) sb.AppendLine();
                sb.Append(indent).Append(i + 1).Append(") ")
                    .Append(FormatResultCore(result[i], depth + 3));
            }
            if (length > limit)
                sb.AppendLine().Append(indent).Append($"... ({length - limit} more)");
            return sb.ToString();
        }

        if (result.Resp2Type == ResultType.Error)
            return "(error) " + result;

        return result.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}
