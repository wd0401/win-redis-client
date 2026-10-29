using WinRedisClient.Core.Models;
using WinRedisClient.Infrastructure;

namespace WinRedisClient.Core.Tests;

public sealed class ConnectionStoreTests
{
    [Fact]
    public async Task SaveAndLoad_PreservesGroupAndWarningColor_WithoutPlaintextSecrets()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winredis-connection-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonConnectionStore(path);
            var profile = new ConnectionProfile
            {
                Id = Guid.NewGuid(),
                Name = "grouped",
                Group = "测试组",
                WarningColor = ConnectionWarningColor.Purple,
                Environment = EnvironmentTag.Testing,
                Host = "127.0.0.1",
                Port = 6379,
                Password = "redis-secret",
                UseSshTunnel = true,
                SshPassword = "ssh-secret",
            };

            await store.SaveAsync(profile);
            var loaded = Assert.Single(await store.GetAllAsync());

            Assert.Equal("测试组", loaded.Group);
            Assert.Equal(ConnectionWarningColor.Purple, loaded.WarningColor);
            Assert.Equal("redis-secret", loaded.Password);
            Assert.Equal("ssh-secret", loaded.SshPassword);

            var raw = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("redis-secret", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("ssh-secret", raw, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
