using WinRedisClient.Core.Models;

namespace WinRedisClient.Core.Services;

/// <summary>
/// 命令风险分类。执行层会取“调用方声明风险”和“实际命令分类”的较大值，
/// 防止 UI 以 ReadOnly 声明执行写命令绕过保护。
/// </summary>
public static class CommandRiskClassifier
{
    private static readonly HashSet<string> HighRisk = new(StringComparer.OrdinalIgnoreCase)
    {
        "FLUSHALL", "FLUSHDB", "SHUTDOWN", "SWAPDB", "MIGRATE",
        "REPLICAOF", "SLAVEOF", "FAILOVER", "DEBUG", "RESTORE-ASKING",
    };

    private static readonly HashSet<string> BatchOrScript = new(StringComparer.OrdinalIgnoreCase)
    {
        "EVAL", "EVALSHA", "EVALSHA_RO", "EVAL_RO", "FCALL", "FCALL_RO",
        "KEYS", "MONITOR", "SUBSCRIBE", "PSUBSCRIBE", "SSUBSCRIBE",
        "MULTI", "EXEC", "DISCARD", "WATCH", "UNWATCH", "WAIT", "WAITAOF",
        "BLPOP", "BRPOP", "BRPOPLPUSH", "BLMOVE", "BZPOPMIN", "BZPOPMAX",
        "BLMPOP", "BZMPOP", "MSET", "MSETNX", "RESTORE", "SORT", "SORT_RO",
        "GEORADIUS", "GEORADIUSBYMEMBER", "XREAD", "XREADGROUP",
    };

    private static readonly HashSet<string> SingleWrite = new(StringComparer.OrdinalIgnoreCase)
    {
        "SET", "SETNX", "SETEX", "PSETEX", "GETSET", "GETDEL", "GETEX",
        "APPEND", "SETRANGE", "INCR", "DECR", "INCRBY", "DECRBY", "INCRBYFLOAT",
        "HSET", "HMSET", "HSETNX", "HDEL", "HINCRBY", "HINCRBYFLOAT",
        "LPUSH", "RPUSH", "LPUSHX", "RPUSHX", "LSET", "LREM", "LTRIM", "LINSERT",
        "SADD", "SREM", "SMOVE", "SPOP",
        "ZADD", "ZREM", "ZINCRBY", "ZPOPMIN", "ZPOPMAX", "ZREMRANGEBYRANK",
        "ZREMRANGEBYSCORE", "ZREMRANGEBYLEX",
        "EXPIRE", "PEXPIRE", "EXPIREAT", "PEXPIREAT", "PERSIST",
        "RENAME", "RENAMENX", "COPY", "DEL", "UNLINK",
        "SINTERSTORE", "SUNIONSTORE", "SDIFFSTORE", "ZUNIONSTORE", "ZINTERSTORE",
        "PFADD", "PFMERGE", "GEOADD", "SETBIT", "BITFIELD",
        "XADD", "XDEL", "XTRIM", "XSETID", "XGROUP", "XACK", "XCLAIM", "XAUTOCLAIM",
        "PUBLISH", "SPUBLISH", "PUBSUB",
    };

    private static readonly HashSet<string> ReadOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "PING", "ECHO", "INFO", "DBSIZE", "TIME", "LASTSAVE", "ROLE",
        "GET", "MGET", "GETRANGE", "STRLEN", "EXISTS", "TYPE", "TTL", "PTTL",
        "SCAN", "SSCAN", "HSCAN", "ZSCAN", "RANDOMKEY",
        "MEMORY", "SLOWLOG", "OBJECT", "LOLWUT",
        "HGET", "HMGET", "HGETALL", "HKEYS", "HVALS", "HLEN", "HEXISTS", "HRANDFIELD",
        "LLEN", "LRANGE", "LINDEX", "LPOS",
        "SCARD", "SMEMBERS", "SISMEMBER", "SMISMEMBER", "SRANDMEMBER",
        "ZCARD", "ZCOUNT", "ZSCORE", "ZMSCORE", "ZRANGE", "ZRANGEBYSCORE",
        "ZRANGEBYLEX", "ZREVRANGE", "ZRANGEBYRANK", "ZRANK", "ZREVRANK", "ZRANDMEMBER",
        "XRANGE", "XREVRANGE", "XLEN", "XINFO", "XPENDING",
        "BITCOUNT", "BITPOS", "GETBIT", "PFCOUNT", "GEOHASH", "GEOPOS", "GEODIST", "GEOSEARCH",
        "CLIENT", "COMMAND", "LATENCY", "ACL", "CONFIG",
    };

    public static CommandRiskLevel Classify(string command, IReadOnlyList<string>? args = null)
    {
        var name = Normalize(command);
        if (name.Length == 0) return CommandRiskLevel.BatchOrScript;

        var sub = args is { Count: > 0 } ? Normalize(args[0]) : string.Empty;

        // 子命令敏感的管理命令
        if (name == "CONFIG" && sub is "SET" or "REWRITE" or "RESETSTAT") return CommandRiskLevel.HighRisk;
        if (name == "ACL" && sub is "SETUSER" or "DELUSER" or "LOAD" or "SAVE") return CommandRiskLevel.HighRisk;
        if (name == "SCRIPT" && sub is "FLUSH" or "KILL") return CommandRiskLevel.BatchOrScript;
        if (name == "MODULE" && sub is "LOAD" or "UNLOAD") return CommandRiskLevel.HighRisk;
        if (name == "CLIENT" && sub is "KILL") return CommandRiskLevel.BatchOrScript;
        if (name == "CLUSTER" && sub is "RESET" or "FAILOVER" or "SETSLOT") return CommandRiskLevel.HighRisk;
        if (name == "FUNCTION" && sub is "FLUSH" or "RESTORE" or "DELETE") return CommandRiskLevel.BatchOrScript;

        if (HighRisk.Contains(name)) return CommandRiskLevel.HighRisk;

        // 单键 DEL / UNLINK 为 L1，多键删除升级为 L2
        if ((name is "DEL" or "UNLINK") && args is { Count: > 1 })
            return CommandRiskLevel.BatchOrScript;

        if (BatchOrScript.Contains(name)) return CommandRiskLevel.BatchOrScript;
        if (SingleWrite.Contains(name)) return CommandRiskLevel.SingleWrite;
        if (ReadOnly.Contains(name)) return CommandRiskLevel.ReadOnly;

        // 未知命令（含模块命令）按脚本级处理，要求显式确认
        return CommandRiskLevel.BatchOrScript;
    }

    /// <summary>执行层使用的最终风险等级，绝不低于命令自身分类。</summary>
    public static CommandRiskLevel Effective(
        string command,
        IReadOnlyList<string>? args,
        CommandRiskLevel declared)
        => (CommandRiskLevel)Math.Max((int)declared, (int)Classify(command, args));

    /// <summary>是否属于可能包含凭证参数、不应写入历史的命令。</summary>
    public static bool IsSensitive(string command, IReadOnlyList<string>? args = null)
    {
        var name = Normalize(command);
        if (name is "AUTH" or "HELLO") return true;
        var sub = args is { Count: > 0 } ? Normalize(args[0]) : string.Empty;
        return name == "ACL" && sub == "SETUSER"
            || name == "CONFIG" && sub == "SET"
            || name == "MIGRATE";
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
