using System.Text.Json;
using System.Text.Json.Serialization;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Infrastructure;

/// <summary>连接配置持久化：%AppData%\WinRedis\connections.json；密码经 DPAPI 加密，不落明文。</summary>
public sealed class JsonConnectionStore : IConnectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonConnectionStore(string? path = null)
    {
        _path = path ?? StoragePaths.ConnectionsFile;
    }

    public async Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadFileAsync(cancellationToken).ConfigureAwait(false);
            return file.Connections
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(ToProfile)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadFileAsync(cancellationToken).ConfigureAwait(false);
            file.Connections.RemoveAll(c => c.Id == profile.Id);
            file.Connections.Add(ToDto(profile));
            await WriteFileAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadFileAsync(cancellationToken).ConfigureAwait(false);
            if (file.Connections.RemoveAll(c => c.Id == id) > 0)
                await WriteFileAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ConnectionFile> LoadFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new ConnectionFile();

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ConnectionFile>(json, JsonOptions) ?? new ConnectionFile();
        }
        catch (JsonException ex)
        {
            var backup = _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try { File.Copy(_path, backup, overwrite: true); } catch { /* 备份失败不覆盖原始错误 */ }
            throw new InvalidDataException(Loc.F("连接配置文件无法解析，已备份到 {0}", backup), ex);
        }
    }

    private Task WriteFileAsync(ConnectionFile file, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var json = JsonSerializer.Serialize(file, JsonOptions);
        StoragePaths.WriteAtomic(_path, json);
        return Task.CompletedTask;
    }

    private static ConnectionProfile ToProfile(ConnectionDto dto) => new()
    {
        Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
        Name = dto.Name,
        Group = dto.Group,
        WarningColor = dto.WarningColor,
        Environment = dto.Environment,
        EnvironmentLabel = dto.EnvironmentLabel,
        Host = dto.Host,
        Port = dto.Port,
        UseTls = dto.UseTls,
        Username = dto.Username,
        Password = Dpapi.TryUnprotect(dto.PasswordProtected),
        CredentialRef = dto.CredentialRef,
        ConnectTimeout = TimeSpan.FromSeconds(Math.Clamp(dto.ConnectTimeoutSeconds, 1, 120)),
        CommandTimeout = TimeSpan.FromSeconds(Math.Clamp(dto.CommandTimeoutSeconds, 1, 600)),
        Database = Math.Clamp(dto.Database, 0, 15),
        Notes = dto.Notes,
        ReadOnly = dto.ReadOnly,
        AutoLoadKeys = dto.AutoLoadKeys,
        Kind = dto.Kind,
        SshConfigRef = dto.SshConfigRef,
        UseSshTunnel = dto.UseSshTunnel,
        SshAuthMode = dto.SshAuthMode,
        SshHost = dto.SshHost,
        SshPort = dto.SshPort is > 0 and <= 65535 ? dto.SshPort : 22,
        SshUsername = dto.SshUsername,
        SshPassword = Dpapi.TryUnprotect(dto.SshPasswordProtected),
        SshPrivateKeyPath = dto.SshPrivateKeyPath,
        SshKeyPassphrase = Dpapi.TryUnprotect(dto.SshKeyPassphraseProtected),
    };

    private static ConnectionDto ToDto(ConnectionProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Group = profile.Group,
        WarningColor = profile.WarningColor,
        Environment = profile.Environment,
        EnvironmentLabel = profile.EnvironmentLabel,
        Host = profile.Host,
        Port = profile.Port,
        UseTls = profile.UseTls,
        Username = profile.Username,
        PasswordProtected = string.IsNullOrEmpty(profile.Password) ? null : Dpapi.Protect(profile.Password),
        CredentialRef = profile.CredentialRef,
        ConnectTimeoutSeconds = (int)profile.ConnectTimeout.TotalSeconds,
        CommandTimeoutSeconds = (int)profile.CommandTimeout.TotalSeconds,
        Database = profile.Database,
        Notes = profile.Notes,
        ReadOnly = profile.ReadOnly,
        AutoLoadKeys = profile.AutoLoadKeys,
        Kind = profile.Kind,
        SshConfigRef = profile.SshConfigRef,
        UseSshTunnel = profile.UseSshTunnel,
        SshAuthMode = profile.SshAuthMode,
        SshHost = profile.SshHost,
        SshPort = profile.SshPort,
        SshUsername = profile.SshUsername,
        SshPasswordProtected = string.IsNullOrEmpty(profile.SshPassword) ? null : Dpapi.Protect(profile.SshPassword),
        SshPrivateKeyPath = profile.SshPrivateKeyPath,
        SshKeyPassphraseProtected = string.IsNullOrEmpty(profile.SshKeyPassphrase) ? null : Dpapi.Protect(profile.SshKeyPassphrase),
    };

    private sealed class ConnectionFile
    {
        public int Version { get; set; } = 1;
        public List<ConnectionDto> Connections { get; set; } = [];
    }

    private sealed class ConnectionDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Group { get; set; }
        public ConnectionWarningColor WarningColor { get; set; }
        public EnvironmentTag Environment { get; set; }
        public string? EnvironmentLabel { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 6379;
        public bool UseTls { get; set; }
        public string? Username { get; set; }
        public string? PasswordProtected { get; set; }
        public string? CredentialRef { get; set; }
        public int ConnectTimeoutSeconds { get; set; } = 5;
        public int CommandTimeoutSeconds { get; set; } = 30;
        public int Database { get; set; }
        public string? Notes { get; set; }
        public bool ReadOnly { get; set; }
        public bool AutoLoadKeys { get; set; } = true;
        public ConnectionKind Kind { get; set; } = ConnectionKind.Standalone;
        public Guid? SshConfigRef { get; set; }
        public bool UseSshTunnel { get; set; }
        public SshAuthMethod SshAuthMode { get; set; } = SshAuthMethod.Password;
        public string? SshHost { get; set; }
        public int SshPort { get; set; } = 22;
        public string? SshUsername { get; set; }
        public string? SshPasswordProtected { get; set; }
        public string? SshPrivateKeyPath { get; set; }
        public string? SshKeyPassphraseProtected { get; set; }
    }
}
