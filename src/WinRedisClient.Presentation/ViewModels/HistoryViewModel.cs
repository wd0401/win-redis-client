using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Models;
using WinRedisClient.Presentation.Support;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

public sealed class RecentKeyRowViewModel
{
    public RecentKeyRowViewModel(RecentKeyEntry entry) => Entry = entry;

    public RecentKeyEntry Entry { get; }
    public string Key => Entry.Key;
    public string ConnectionText => $"{Entry.ConnectionName} · DB{Entry.Database}";
    public string TimeText => Entry.LastAccessed.LocalDateTime.ToString("MM-dd HH:mm");
}

public sealed class CommandHistoryRowViewModel
{
    public CommandHistoryRowViewModel(CommandHistoryEntry entry) => Entry = entry;

    public CommandHistoryEntry Entry { get; }
    public string Command => Entry.Command;
    public string TimeText => Entry.ExecutedAt.LocalDateTime.ToString("MM-dd HH:mm:ss");
    public string CommandPreview => UiText.SingleLine(Entry.Command, 220);
}

/// <summary>最近访问与本地命令历史（均可关闭记录、单项删除与清空）。</summary>
public partial class HistoryViewModel : ObservableObject
{
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = Loc.T("尚未加载");
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private string _recentEnabledText = string.Empty;
    [ObservableProperty] private string _commandsEnabledText = string.Empty;

    public ObservableCollection<RecentKeyRowViewModel> Recent { get; } = [];
    public ObservableCollection<CommandHistoryRowViewModel> Commands { get; } = [];

    public event EventHandler<RecentKeyEntry>? OpenKeyRequested;
    public event EventHandler<string>? UseCommandRequested;
    public event EventHandler<string>? CopyRequested;
    public event EventHandler<NotificationMessage>? NotificationRequested;

    public bool HasRecent => Recent.Count > 0;
    public bool HasCommands => Commands.Count > 0;
    public bool IsEmptyState => !IsLoading && Recent.Count == 0 && Commands.Count == 0;

    public async Task ActivateAsync()
    {
        RefreshSettingsText();
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        var store = AppServices.HistoryStore;
        if (store is null)
        {
            StatusText = Loc.T("本地历史存储未初始化");
            return;
        }

        IsLoading = true;
        ErrorText = string.Empty;

        try
        {
            var recent = await store.GetRecentAsync();
            var commands = await store.GetCommandsAsync();

            Recent.Clear();
            foreach (var entry in recent) Recent.Add(new RecentKeyRowViewModel(entry));

            Commands.Clear();
            foreach (var entry in commands) Commands.Add(new CommandHistoryRowViewModel(entry));

            StatusText = Loc.F("{0:N0} 条最近访问 · {1:N0} 条命令历史", Recent.Count, Commands.Count);
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("读取本地历史失败：") + ex.Message;
            StatusText = Loc.T("加载失败");
        }
        finally
        {
            IsLoading = false;
            RefreshSettingsText();
            NotifyCollections();
        }
    }

    private void RefreshSettingsText()
    {
        var settings = AppServices.Settings;
        RecentEnabledText = settings.RecentKeysEnabled ? Loc.T("最近访问记录：开启") : Loc.T("最近访问记录：已关闭");
        CommandsEnabledText = settings.CommandHistoryEnabled ? Loc.T("命令历史记录：开启") : Loc.T("命令历史记录：已关闭");
    }

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();

    [RelayCommand]
    private void OpenKey(RecentKeyRowViewModel? row)
    {
        if (row is not null)
            OpenKeyRequested?.Invoke(this, row.Entry);
    }

    [RelayCommand]
    private void CopyKey(RecentKeyRowViewModel? row)
    {
        if (row is not null)
            CopyRequested?.Invoke(this, row.Key);
    }

    [RelayCommand]
    private void UseCommand(CommandHistoryRowViewModel? row)
    {
        if (row is not null)
            UseCommandRequested?.Invoke(this, row.Command);
    }

    [RelayCommand]
    private void CopyCommandText(CommandHistoryRowViewModel? row)
    {
        if (row is not null)
            CopyRequested?.Invoke(this, row.Command);
    }

    [RelayCommand]
    private async Task RemoveRecent(RecentKeyRowViewModel? row)
    {
        if (row is null || AppServices.HistoryStore is null) return;
        try
        {
            await AppServices.HistoryStore.RemoveRecentAsync(row.Key);
            Recent.Remove(row);
            NotifyCollections();
            StatusText = Loc.F("已移除最近访问：{0}", row.Key);
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("移除失败：") + ex.Message, true));
        }
    }

    [RelayCommand]
    private async Task RemoveCommand(CommandHistoryRowViewModel? row)
    {
        if (row is null || AppServices.HistoryStore is null) return;
        try
        {
            await AppServices.HistoryStore.RemoveCommandAsync(row.Command);
            Commands.Remove(row);
            NotifyCollections();
            StatusText = Loc.T("已移除一条命令历史");
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("移除失败：") + ex.Message, true));
        }
    }

    [RelayCommand]
    private async Task ClearRecent()
    {
        if (AppServices.HistoryStore is null) return;
        try
        {
            await AppServices.HistoryStore.ClearRecentAsync();
            Recent.Clear();
            NotifyCollections();
            StatusText = Loc.T("最近访问已清空");
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("最近访问已清空")));
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("清空失败：") + ex.Message, true));
        }
    }

    [RelayCommand]
    private async Task ClearCommands()
    {
        if (AppServices.HistoryStore is null) return;
        try
        {
            await AppServices.HistoryStore.ClearCommandsAsync();
            Commands.Clear();
            NotifyCollections();
            StatusText = Loc.T("命令历史已清空");
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("命令历史已清空")));
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("清空失败：") + ex.Message, true));
        }
    }

    private void NotifyCollections()
    {
        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(IsEmptyState));
    }
}
