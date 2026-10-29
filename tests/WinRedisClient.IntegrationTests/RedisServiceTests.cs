using System.Text;
using WinRedisClient.Core.Models;
using WinRedisClient.Core.Services;
using WinRedisClient.Redis;
using WinRedisClient.Transport;

namespace WinRedisClient.IntegrationTests;

/// <summary>
/// 针对本机 Redis（127.0.0.1:6379）的集成测试：扫描分页、读值、写入保护、TTL 保持与集合分页。
/// 每个用例使用独立前缀，结束后清理，不会修改既有业务 Key。
/// </summary>
public class RedisServiceTests
{
    private const string Host = "127.0.0.1";
    private const int Port = 6379;

    private static RedisService CreateService()
        => new(new RedisSessionFactory(new SshTunnelService()));

    private static ConnectionProfile LocalProfile(
        bool readOnly = false,
        EnvironmentTag environment = EnvironmentTag.Development) => new()
    {
        Id = Guid.NewGuid(),
        Name = "integration-local",
        Environment = environment,
        Host = Host,
        Port = Port,
        ReadOnly = readOnly,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        CommandTimeout = TimeSpan.FromSeconds(15),
    };

    private static string NewPrefix() => "winredis:it:" + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task Connect_ReportsServerVersion_AndDatabaseSize()
    {
        await using var service = CreateService();
        await service.ConnectAsync(LocalProfile());

        Assert.True(service.IsConnected);
        Assert.NotNull(service.CurrentProfile);

        var version = await service.GetServerVersionAsync();
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Contains(".", version!);

        var size = await service.GetDatabaseSizeAsync();
        Assert.True(size >= 0);

        var ping = await service.PingAsync();
        Assert.True(ping >= TimeSpan.Zero);

        await service.DisconnectAsync();
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task ScanPage_PaginatesWithoutDuplicates_AndFiltersByPattern()
    {
        await using var service = CreateService();
        await service.ConnectAsync(LocalProfile());

        var prefix = NewPrefix();
        try
        {
            for (var i = 0; i < 25; i++)
            {
                var created = await service.CreateKeyAsync($"{prefix}:{i:D2}", "string", [$"v{i}"]);
                Assert.Equal(ExecutionOutcome.Success, created.Outcome);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cursor = 0L;
            var pages = 0;
            while (pages < 200)
            {
                var page = await service.ScanPageAsync($"{prefix}:*", 5, cursor);
                foreach (var key in page.Keys)
                    Assert.True(seen.Add(key.DisplayKey), "SCAN 返回了重复 Key：" + key.DisplayKey);

                cursor = page.NextCursor;
                pages++;
                if (page.IsComplete) break;
            }

            Assert.Equal(25, seen.Count);
            Assert.All(seen, key => Assert.StartsWith(prefix + ":", key));
        }
        finally
        {
            await CleanupPrefixAsync(service, prefix);
        }

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task Metadata_StringRead_AndPreserveTtl()
    {
        await using var service = CreateService();
        await service.ConnectAsync(LocalProfile());

        var key = NewPrefix() + ":string";
        try
        {
            var created = await service.CreateKeyAsync(key, "string", ["v1"], TimeSpan.FromSeconds(120));
            Assert.Equal(ExecutionOutcome.Success, created.Outcome);

            var meta = await service.GetMetadataAsync(key, includeSize: true);
            Assert.NotNull(meta);
            Assert.Equal("string", meta!.Type);
            Assert.True(meta.TimeToLive is { TotalSeconds: > 60 });
            Assert.True(meta.SizeBytes is > 0);

            var read = await service.GetStringBytesAsync(key, 1024);
            Assert.NotNull(read);
            Assert.Equal("v1", Encoding.UTF8.GetString(read!.Data));

            // 不带 TTL 更新时保留剩余过期时间
            var updated = await service.SetStringAsync(key, Encoding.UTF8.GetBytes("v2"), null, preserveTtl: true);
            Assert.Equal(ExecutionOutcome.Success, updated.Outcome);

            var after = await service.GetMetadataAsync(key, includeSize: false);
            Assert.NotNull(after);
            Assert.True(after!.TimeToLive is { TotalSeconds: > 0 });
            Assert.True(after.TimeToLive <= TimeSpan.FromSeconds(120));

            var reread = await service.GetStringBytesAsync(key, 1024);
            Assert.Equal("v2", Encoding.UTF8.GetString(reread!.Data));
        }
        finally
        {
            await service.ExecuteArgsAsync("DEL", [key], CommandRiskLevel.SingleWrite, confirmed: true);
        }

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task CollectionPage_HashPagination_CoversAllFields()
    {
        await using var service = CreateService();
        await service.ConnectAsync(LocalProfile());

        var key = NewPrefix() + ":hash";
        try
        {
            var created = await service.CreateKeyAsync(key, "hash", ["f0", "v0"]);
            Assert.Equal(ExecutionOutcome.Success, created.Outcome);

            var set1 = await service.ExecuteArgsAsync("HSET", [key, "f1", "v1"], CommandRiskLevel.SingleWrite);
            var set2 = await service.ExecuteArgsAsync("HSET", [key, "f2", "v2"], CommandRiskLevel.SingleWrite);
            Assert.Equal(ExecutionOutcome.Success, set1.Outcome);
            Assert.Equal(ExecutionOutcome.Success, set2.Outcome);

            var seenFields = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            var pages = 0;
            do
            {
                var page = await service.GetCollectionPageAsync(key, "hash", cursor, 2);
                Assert.Null(page.Error);
                Assert.Equal(3, page.TotalCount);
                foreach (var item in page.Items)
                    Assert.True(seenFields.Add(item.Field));

                cursor = page.NextCursor;
                pages++;
            } while (cursor is not null && pages < 20);

            Assert.Equal(3, seenFields.Count);
            Assert.Contains("f0", seenFields);
            Assert.Contains("f1", seenFields);
            Assert.Contains("f2", seenFields);
        }
        finally
        {
            await service.ExecuteArgsAsync("DEL", [key], CommandRiskLevel.SingleWrite, confirmed: true);
        }

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task CreateKey_DoesNotOverwriteExistingKey()
    {
        await using var service = CreateService();
        await service.ConnectAsync(LocalProfile());

        var existing = NewPrefix() + ":existing";
        var duplicate = NewPrefix() + ":duplicate";
        try
        {
            await service.CreateKeyAsync(existing, "string", ["origin"]);
            var second = await service.CreateKeyAsync(existing, "string", ["overwrite"]);
            Assert.Equal(ExecutionOutcome.Failure, second.Outcome);
            Assert.Equal("target_exists", second.ErrorCode);

            var read = await service.GetStringBytesAsync(existing, 64);
            Assert.Equal("origin", Encoding.UTF8.GetString(read!.Data));

            // RENAMENX：目标已存在时拒绝
            await service.CreateKeyAsync(duplicate, "string", ["other"]);
            var rename = await service.RenameKeyAsync(existing, duplicate, confirmed: true);
            Assert.Equal(ExecutionOutcome.Failure, rename.Outcome);
            Assert.Equal("target_exists", rename.ErrorCode);

            var renamed = await service.RenameKeyAsync(existing, existing + ":renamed", confirmed: true);
            Assert.Equal(ExecutionOutcome.Success, renamed.Outcome);
        }
        finally
        {
            await service.ExecuteArgsAsync("DEL", [existing, existing + ":renamed", duplicate],
                CommandRiskLevel.SingleWrite, confirmed: true);
        }

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task SetTtl_AndPersist_AreReflectedInMetadata()
    {
        await using var service = CreateService();
        await service.ConnectAsync(LocalProfile());

        var key = NewPrefix() + ":ttl";
        try
        {
            await service.CreateKeyAsync(key, "string", ["v"]);

            var expire = await service.SetTtlAsync(key, TimeSpan.FromSeconds(90));
            Assert.Equal(ExecutionOutcome.Success, expire.Outcome);

            var withTtl = await service.GetMetadataAsync(key, includeSize: false);
            Assert.True(withTtl!.TimeToLive is { TotalSeconds: > 60 });

            var persist = await service.SetTtlAsync(key, null);
            Assert.Equal(ExecutionOutcome.Success, persist.Outcome);

            var persisted = await service.GetMetadataAsync(key, includeSize: false);
            Assert.Null(persisted!.TimeToLive);
        }
        finally
        {
            await service.ExecuteArgsAsync("DEL", [key], CommandRiskLevel.SingleWrite, confirmed: true);
        }

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task ReadOnlyProfile_BlocksWrites_AllowsReads()
    {
        await using var service = CreateService();
        await service.ConnectAsync(LocalProfile(readOnly: true));

        var info = await service.GetInfoAsync();
        Assert.Contains("redis_version", info.Fields.Keys);

        var write = await service.ExecuteArgsAsync("SET", ["ro:test", "1"], CommandRiskLevel.SingleWrite);
        Assert.Equal(ExecutionOutcome.Failure, write.Outcome);
        Assert.Equal("readonly_mode", write.ErrorCode);

        var delete = await service.DeleteKeyAsync("ro:test", confirmed: true);
        Assert.Equal(ExecutionOutcome.Failure, delete.Outcome);
        Assert.Equal("readonly_mode", delete.ErrorCode);

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task ProductionProfile_RequiresConfirmationForDestructiveWrites()
    {
        await using var service = CreateService();
        await service.ConnectAsync(LocalProfile(environment: EnvironmentTag.Production));

        var key = NewPrefix() + ":prod";
        try
        {
            var create = await service.CreateKeyAsync(key, "string", ["v"]);
            Assert.Equal(ExecutionOutcome.Success, create.Outcome);

            var blocked = await service.DeleteKeyAsync(key, confirmed: false);
            Assert.Equal(ExecutionOutcome.Failure, blocked.Outcome);
            Assert.Equal("production_confirmation_required", blocked.ErrorCode);

            var confirmed = await service.DeleteKeyAsync(key, confirmed: true);
            Assert.Equal(ExecutionOutcome.Success, confirmed.Outcome);
        }
        finally
        {
            await service.ExecuteArgsAsync("DEL", [key], CommandRiskLevel.SingleWrite, confirmed: true);
        }

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task BatchAndHighRiskCommands_RequireConfirmedFlag()
    {
        await using var service = CreateService();
        await service.ConnectAsync(LocalProfile());

        var blocked = await service.ExecuteArgsAsync("FLUSHDB", [], CommandRiskLevel.BatchOrScript);
        Assert.Equal(ExecutionOutcome.Failure, blocked.Outcome);
        Assert.Equal("confirmation_required", blocked.ErrorCode);

        var eval = await service.ExecuteArgsAsync("EVAL", ["return 1", "0"], CommandRiskLevel.HighRisk);
        Assert.Equal(ExecutionOutcome.Failure, eval.Outcome);
        Assert.Equal("confirmation_required", eval.ErrorCode);

        var allowed = await service.ExecuteArgsAsync("EVAL", ["return 1", "0"], CommandRiskLevel.HighRisk, confirmed: true);
        Assert.Equal(ExecutionOutcome.Success, allowed.Outcome);
        Assert.Equal("1", allowed.Output);

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task Info_And_SlowLog_AreReadable()
    {
        await using var service = CreateService();
        await service.ConnectAsync(LocalProfile());

        var info = await service.GetInfoAsync();
        Assert.False(string.IsNullOrWhiteSpace(info.RawText));
        Assert.Contains("redis_version", info.Fields.Keys);
        Assert.NotEmpty(info.Sections);

        var slowLog = await service.GetSlowLogAsync(10);
        Assert.NotNull(slowLog);

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task Disconnect_WhenNotConnected_IsNoOp()
    {
        await using var service = CreateService();
        await service.DisconnectAsync();
        Assert.False(service.IsConnected);

        var result = await service.DeleteKeyAsync("whatever", confirmed: true);
        Assert.Equal(ExecutionOutcome.Failure, result.Outcome);
        Assert.Equal("not_connected", result.ErrorCode);
    }

    private static async Task CleanupPrefixAsync(RedisService service, string prefix)
    {
        var cursor = 0L;
        var pages = 0;
        do
        {
            var page = await service.ScanPageAsync($"{prefix}:*", 200, cursor);
            foreach (var key in page.Keys)
                await service.DeleteKeyAsync(key.DisplayKey, confirmed: true);
            cursor = page.NextCursor;
            pages++;
            if (page.IsComplete) break;
        } while (pages < 50);
    }
}
