using System.Text.Json;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;

namespace WinRedisClient.Infrastructure;

/// <summary>最近访问与命令历史持久化：%AppData%\WinRedis\history.json。不保存 Value 或认证参数。</summary>
public sealed class JsonHistoryStore : IHistoryStore
{
    private const int RecentLimit = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonHistoryStore(string? path = null)
    {
        _path = path ?? StoragePaths.HistoryFile;
    }

    public async Task<IReadOnlyList<RecentKeyEntry>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return file.Recent.OrderByDescending(r => r.LastAccessed).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddRecentAsync(RecentKeyEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
            file.Recent.RemoveAll(r => r.ConnectionId == entry.ConnectionId
                && r.Database == entry.Database
                && string.Equals(r.Key, entry.Key, StringComparison.Ordinal));
            file.Recent.Insert(0, entry);
            if (file.Recent.Count > RecentLimit)
                file.Recent.RemoveRange(RecentLimit, file.Recent.Count - RecentLimit);
            await SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveRecentAsync(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (file.Recent.RemoveAll(r => string.Equals(r.Key, key, StringComparison.Ordinal)) > 0)
                await SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearRecentAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
            file.Recent.Clear();
            await SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CommandHistoryEntry>> GetCommandsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return file.Commands.OrderByDescending(c => c.ExecutedAt).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddCommandAsync(
        CommandHistoryEntry entry,
        bool enabled,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (!enabled) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
            file.Commands.RemoveAll(c => string.Equals(c.Command, entry.Command, StringComparison.Ordinal));
            file.Commands.Insert(0, entry);

            var max = Math.Clamp(limit, 10, 1000);
            if (file.Commands.Count > max)
                file.Commands.RemoveRange(max, file.Commands.Count - max);

            await SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (file.Commands.RemoveAll(c => string.Equals(c.Command, command, StringComparison.Ordinal)) > 0)
                await SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearCommandsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
            file.Commands.Clear();
            await SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HistoryFile> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new HistoryFile();
        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<HistoryFile>(json, JsonOptions) ?? new HistoryFile();
        }
        catch (JsonException)
        {
            return new HistoryFile();
        }
    }

    private Task SaveAsync(HistoryFile file, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        StoragePaths.WriteAtomic(_path, JsonSerializer.Serialize(file, JsonOptions));
        return Task.CompletedTask;
    }

    private sealed class HistoryFile
    {
        public List<RecentKeyEntry> Recent { get; set; } = [];
        public List<CommandHistoryEntry> Commands { get; set; } = [];
    }
}
