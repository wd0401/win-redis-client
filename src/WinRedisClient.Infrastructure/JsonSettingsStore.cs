using System.Text.Json;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;

namespace WinRedisClient.Infrastructure;

/// <summary>用户设置持久化：%AppData%\WinRedis\settings.json。</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(string? path = null)
    {
        _path = path ?? StoragePaths.SettingsFile;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return AppSettings.Default;
            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return Normalize(settings ?? AppSettings.Default);
        }
        catch (JsonException)
        {
            return AppSettings.Default;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = cancellationToken;
            var json = JsonSerializer.Serialize(Normalize(settings), JsonOptions);
            StoragePaths.WriteAtomic(_path, json);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static AppSettings Normalize(AppSettings settings) => settings with
    {
        KeySeparator = string.IsNullOrEmpty(settings.KeySeparator) ? ":" : settings.KeySeparator,
        MonitorIntervalSeconds = Math.Clamp(settings.MonitorIntervalSeconds, 2, 300),
        CommandHistoryLimit = Math.Clamp(settings.CommandHistoryLimit, 10, 1000),
        DefaultDatabase = Math.Clamp(settings.DefaultDatabase, 0, 15),
        KeyPaneWidth = Math.Clamp(settings.KeyPaneWidth, 260, 900),
        FontScale = Math.Clamp(settings.FontScale, 0.8, 1.5),
    };
}
