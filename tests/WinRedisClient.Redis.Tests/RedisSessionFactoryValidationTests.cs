using WinRedisClient.Core.Models;
using WinRedisClient.Redis;
using WinRedisClient.Transport;

namespace WinRedisClient.Redis.Tests;

public class RedisSessionFactoryValidationTests
{
    private static RedisSessionFactory CreateFactory() => new(new SshTunnelService());

    private static ConnectionProfile Profile(Action<ProfileBuilder>? configure = null)
    {
        var builder = new ProfileBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    [Fact]
    public async Task CreateAsync_RejectsUnsupportedConnectionKind()
    {
        var factory = CreateFactory();
        var profile = Profile(b => b.Kind = ConnectionKind.Cluster);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => factory.CreateAsync(profile));
        Assert.Contains("Standalone", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsEmptyHost()
    {
        var factory = CreateFactory();
        var profile = Profile(b => b.Host = "  ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => factory.CreateAsync(profile));
        Assert.Contains("Host", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public async Task CreateAsync_RejectsInvalidPort(int port)
    {
        var factory = CreateFactory();
        var profile = Profile(b => b.Port = port);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => factory.CreateAsync(profile));
        Assert.Contains("Port", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsSshTunnelWithoutJumpHost()
    {
        var factory = CreateFactory();
        var profile = Profile(b => b.UseSshTunnel = true);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => factory.CreateAsync(profile));
        Assert.Contains("SSH", ex.Message);
    }

    [Fact]
    public async Task ConnectionTester_ReportsFailureForClosedPort()
    {
        var factory = CreateFactory();
        var tester = new RedisConnectionTester(factory);
        var profile = Profile(b =>
        {
            b.Host = "127.0.0.1";
            b.Port = 1; // 保留端口，连接必然失败
            b.ConnectTimeout = TimeSpan.FromSeconds(2);
            b.CommandTimeout = TimeSpan.FromSeconds(2);
        });

        var result = await tester.TestAsync(profile);

        Assert.False(result.Success);
        Assert.Null(result.RedisVersion);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.False(string.IsNullOrWhiteSpace(result.TechnicalDetail));
    }

    private sealed class ProfileBuilder
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; set; } = "test";
        public EnvironmentTag Environment { get; set; } = EnvironmentTag.Development;
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 6379;
        public int Database { get; set; }
        public bool UseSshTunnel { get; set; }
        public ConnectionKind Kind { get; set; } = ConnectionKind.Standalone;
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public ConnectionProfile Build() => new()
        {
            Id = Id,
            Name = Name,
            Environment = Environment,
            Host = Host,
            Port = Port,
            Database = Database,
            UseSshTunnel = UseSshTunnel,
            Kind = Kind,
            ConnectTimeout = ConnectTimeout,
            CommandTimeout = CommandTimeout,
        };
    }
}
