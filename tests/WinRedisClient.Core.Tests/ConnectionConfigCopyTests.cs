using System.Text.Json;
using WinRedisClient.Core.Models;
using WinRedisClient.Presentation.ViewModels;

namespace WinRedisClient.Core.Tests;

public class ConnectionConfigCopyTests
{
    [Fact]
    public void CopyConfig_ExcludesPasswordsAndCredentials()
    {
        var vm = new ConnectionListViewModel();
        string? copied = null;
        vm.CopyConfigRequested += (_, text) => copied = text;

        var item = new ConnectionItemViewModel
        {
            Id = Guid.NewGuid(),
            Name = "stage",
            Group = "预发环境",
            WarningColor = ConnectionWarningColor.Orange,
            Host = "10.0.0.5",
            Port = 6380,
            Environment = EnvironmentTag.Testing,
            Database = 3,
            Username = "app",
            Password = "s3cret",
            AutoLoadKeys = false,
            UseSshTunnel = true,
            SshAuthMode = SshAuthMethod.PrivateKey,
            SshHost = "jump.example.com",
            SshUsername = "ops",
            SshPassword = "ssh-secret",
            SshPrivateKeyPath = @"C:\keys\id_ed25519",
            SshKeyPassphrase = "key-pass",
        };

        vm.CopyConfigCommand.Execute(item);

        Assert.NotNull(copied);
        using var doc = JsonDocument.Parse(copied);
        var root = doc.RootElement;

        Assert.Equal("stage", root.GetProperty("name").GetString());
        Assert.Equal("预发环境", root.GetProperty("group").GetString());
        Assert.Equal("Orange", root.GetProperty("warningColor").GetString());
        Assert.Equal("10.0.0.5", root.GetProperty("host").GetString());
        Assert.False(root.GetProperty("passwordIncluded").GetBoolean());
        Assert.False(root.GetProperty("autoLoadKeys").GetBoolean());

        Assert.False(root.TryGetProperty("password", out _));
        var ssh = root.GetProperty("ssh");
        Assert.False(ssh.TryGetProperty("password", out _));
        Assert.Equal("PrivateKey", ssh.GetProperty("authMode").GetString());
        Assert.Equal(@"C:\keys\id_ed25519", ssh.GetProperty("privateKeyPath").GetString());
        Assert.False(ssh.TryGetProperty("keyPassphrase", out _));
        Assert.False(ssh.TryGetProperty("sshPassword", out _));

        Assert.DoesNotContain("s3cret", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh-secret", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("key-pass", copied, StringComparison.Ordinal);
    }
}
