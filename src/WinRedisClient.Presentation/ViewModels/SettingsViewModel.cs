using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Localization;
using WinRedisClient.Core.Models;

namespace WinRedisClient.Presentation.ViewModels;

/// <summary>界面语言选项。</summary>
public sealed record LanguageOption(AppLanguage Value, string Display);

/// <summary>关闭主窗口行为选项。</summary>
public sealed record CloseActionOption(CloseAction Value, string Display);

/// <summary>设置页：语言、主题、浏览偏好、监控、历史、生产写保护与本地数据管理。</summary>
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private ThemeOptionViewModel? _selectedThemeOption;
    [ObservableProperty] private LanguageOption? _selectedLanguageOption;
    [ObservableProperty] private CloseActionOption? _selectedCloseAction;
    [ObservableProperty] private string _selectedFontSize = Loc.T("标准（100%）");
    [ObservableProperty] private bool _useDirectoryView = true;
    [ObservableProperty] private string _keySeparator = ":";
    [ObservableProperty] private int _defaultDatabase;
    [ObservableProperty] private int _monitorIntervalSeconds = 5;
    [ObservableProperty] private bool _autoRefreshMonitoring = true;
    [ObservableProperty] private bool _commandHistoryEnabled = true;
    [ObservableProperty] private int _commandHistoryLimit = 200;
    [ObservableProperty] private bool _recentKeysEnabled = true;
    [ObservableProperty] private bool _confirmProductionWrites = true;
    [ObservableProperty] private bool _showKeySize = true;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _statusText = Loc.T("设置保存在本机，不会上传到任何外部服务");
    [ObservableProperty] private string _errorText = string.Empty;

    public IReadOnlyList<ThemeOptionViewModel> ThemeOptions { get; } = ThemeCatalog.All;
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(AppLanguage.Chinese, "中文"),
        new(AppLanguage.English, "English"),
    ];
    public IReadOnlyList<CloseActionOption> CloseActionOptions { get; } =
    [
        new(CloseAction.Ask, Loc.T("每次询问")),
        new(CloseAction.MinimizeToTray, Loc.T("最小化到托盘")),
        new(CloseAction.Exit, Loc.T("直接退出")),
    ];
    public IReadOnlyList<string> FontSizeOptions { get; } = [Loc.T("小（90%）"), Loc.T("标准（100%）"), Loc.T("大（115%）")];
    public IReadOnlyList<int> DatabaseOptions { get; } = Enumerable.Range(0, 16).ToList();

    public string DataDirectory => AppServices.DataDirectory;
    public string AppVersion
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                // 只展示语义版本，隐藏构建时追加的提交哈希，避免长文本挤占设置页。
                return informational.Split('+')[0];
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        }
    }

    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler<ThemePreference>? ThemeChangeRequested;
    public event EventHandler<AppLanguage>? LanguageChangeRequested;
    public event EventHandler<double>? FontScaleChangeRequested;
    public event EventHandler? HistoryCleared;
    public event EventHandler? OpenDataFolderRequested;
    public event EventHandler<ExportRequest>? ExportRequested;
    public event EventHandler<NotificationMessage>? NotificationRequested;
    public event EventHandler<ConfirmationRequest>? ConfirmationRequested;

    private bool _isLoadingSettings;

    public SettingsViewModel()
    {
        LoadFrom(AppServices.Settings);
    }

    public void LoadFrom(AppSettings settings)
    {
        _isLoadingSettings = true;
        try
        {
            SelectedThemeOption = ThemeCatalog.For(settings.Theme);
            SelectedLanguageOption = LanguageOptions.FirstOrDefault(o => o.Value == settings.Language)
                                     ?? LanguageOptions[0];
            SelectedCloseAction = CloseActionOptions.FirstOrDefault(o => o.Value == settings.CloseAction)
                                  ?? CloseActionOptions[0];
            SelectedFontSize = FontSizeTextFromScale(settings.FontScale);
            UseDirectoryView = settings.UseDirectoryView;
            KeySeparator = string.IsNullOrEmpty(settings.KeySeparator) ? ":" : settings.KeySeparator;
            DefaultDatabase = Math.Clamp(settings.DefaultDatabase, 0, 15);
            MonitorIntervalSeconds = Math.Clamp(settings.MonitorIntervalSeconds, 1, 60);
            AutoRefreshMonitoring = settings.AutoRefreshMonitoring;
            CommandHistoryEnabled = settings.CommandHistoryEnabled;
            CommandHistoryLimit = Math.Clamp(settings.CommandHistoryLimit, 20, 2000);
            RecentKeysEnabled = settings.RecentKeysEnabled;
            ConfirmProductionWrites = settings.ConfirmProductionWrites;
            ShowKeySize = settings.ShowKeySize;
            StatusText = Loc.T("设置保存在本机，不会上传到任何外部服务");
            ErrorText = string.Empty;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    /// <summary>主题是即时偏好：选择后立即应用并写入本机，无需额外点击保存。</summary>
    partial void OnSelectedThemeOptionChanged(ThemeOptionViewModel? value)
    {
        if (_isLoadingSettings || value is null) return;

        ThemeChangeRequested?.Invoke(this, value.Value);
        _ = PersistThemeAsync(value.Value);
    }

    /// <summary>字体大小是即时偏好：选择后立即应用到全局并写入本机。</summary>
    partial void OnSelectedFontSizeChanged(string value)
    {
        if (_isLoadingSettings) return;

        var scale = FontScaleFromText(value);
        FontScaleChangeRequested?.Invoke(this, scale);
        _ = PersistFontScaleAsync(scale);
    }

    /// <summary>界面语言：保存后需要重启应用生效。</summary>
    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (_isLoadingSettings || value is null) return;
        if (value.Value == AppServices.Settings.Language &&
            value.Value == Localizer.Instance.Language)
        {
            return;
        }

        LanguageChangeRequested?.Invoke(this, value.Value);
        _ = PersistLanguageAsync(value.Value);
    }

    private static string FontSizeTextFromScale(double scale) => scale switch
    {
        < 0.97 => Loc.T("小（90%）"),
        > 1.06 => Loc.T("大（115%）"),
        _ => Loc.T("标准（100%）"),
    };

    private static double FontScaleFromText(string? value) => value switch
    {
        var v when v == Loc.T("小（90%）") => 0.9,
        var v when v == Loc.T("大（115%）") => 1.15,
        _ => 1.0,
    };

    private async Task PersistFontScaleAsync(double scale)
    {
        var store = AppServices.SettingsStore;
        if (store is null) return;

        try
        {
            var updated = AppServices.Settings with { FontScale = scale };
            await store.SaveAsync(updated);
            AppServices.Settings = updated;
            StatusText = Loc.T("字体大小已应用并保存到本机");
            ErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("字体大小已应用，但保存失败：") + ex.Message;
            StatusText = Loc.T("字体大小已应用（未持久化）");
        }
    }

    private async Task PersistLanguageAsync(AppLanguage language)
    {
        var store = AppServices.SettingsStore;
        if (store is null) return;

        try
        {
            var updated = AppServices.Settings with { Language = language };
            await store.SaveAsync(updated);
            AppServices.Settings = updated;
            StatusText = Loc.T("界面语言已保存，重启 WinRedis 后生效");
            ErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("界面语言保存失败：") + ex.Message;
        }
    }

    private async Task PersistThemeAsync(ThemePreference preference)
    {
        var store = AppServices.SettingsStore;
        if (store is null) return;

        try
        {
            var updated = AppServices.Settings with { Theme = preference };
            await store.SaveAsync(updated);
            AppServices.Settings = updated;
            StatusText = Loc.T("主题已应用并保存到本机");
            ErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("主题已应用，但保存失败：") + ex.Message;
            StatusText = Loc.T("主题已应用（未持久化）");
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (IsSaving) return;

        var validation = Validate();
        if (validation is not null)
        {
            ErrorText = validation;
            return;
        }

        var store = AppServices.SettingsStore;
        if (store is null)
        {
            ErrorText = Loc.T("本地设置存储未初始化");
            return;
        }

        IsSaving = true;
        ErrorText = string.Empty;
        StatusText = Loc.T("正在保存…");

        var settings = ToSettings();
        try
        {
            await store.SaveAsync(settings);
            AppServices.Settings = settings;
            StatusText = Loc.F("已保存 · {0}", DateTime.Now.ToString("HH:mm:ss"));
            SettingsSaved?.Invoke(this, settings);
            ThemeChangeRequested?.Invoke(this, settings.Theme);
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("设置已保存")));
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("保存失败：") + ex.Message;
            StatusText = Loc.T("保存失败");
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ResetDefaults()
    {
        var confirmed = await ConfirmAsync(new ConfirmationRequest
        {
            Title = Loc.T("恢复默认设置？"),
            Message = Loc.T("将把界面、监控与历史设置恢复为默认值。连接配置和本地历史不会被删除。\n需要点击“保存设置”后才会持久化。"),
            ConfirmText = Loc.T("恢复默认"),
            CancelText = Loc.T("取消"),
            IsDanger = false,
        });
        if (!confirmed) return;

        LoadFrom(AppSettings.Default);
        StatusText = Loc.T("已恢复默认值，点击“保存设置”生效");
        NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("已恢复默认设置（尚未保存）")));
    }

    [RelayCommand]
    private async Task ClearAllHistory()
    {
        var store = AppServices.HistoryStore;
        if (store is null) return;

        var confirmed = await ConfirmAsync(new ConfirmationRequest
        {
            Title = Loc.T("清空本地历史？"),
            Message = Loc.T("将删除本机保存的最近访问 Key 与命令历史。该操作不可撤销，不会影响 Redis 中的数据。"),
            ConfirmText = Loc.T("清空历史"),
            CancelText = Loc.T("取消"),
            IsDanger = true,
        });
        if (!confirmed) return;

        try
        {
            await store.ClearRecentAsync();
            await store.ClearCommandsAsync();
            StatusText = Loc.T("本地历史已清空");
            HistoryCleared?.Invoke(this, EventArgs.Empty);
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("本地历史已清空")));
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("清空历史失败：") + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
        => OpenDataFolderRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>导出连接配置：按隐私要求始终排除 Redis 密码、SSH 密码与凭据引用。</summary>
    [RelayCommand]
    private async Task ExportConnections()
    {
        var store = AppServices.ConnectionStore;
        if (store is null)
        {
            ErrorText = Loc.T("本地连接存储未初始化");
            return;
        }

        try
        {
            var profiles = await store.GetAllAsync();
            var payload = new
            {
                exportedAt = DateTimeOffset.Now,
                passwordIncluded = false,
                connections = profiles.Select(p => new
                {
                    name = p.Name,
                    group = p.Group,
                    warningColor = p.WarningColor.ToString(),
                    environment = p.Environment.ToString(),
                    environmentLabel = p.EnvironmentLabel,
                    host = p.Host,
                    port = p.Port,
                    database = p.Database,
                    useTls = p.UseTls,
                    username = p.Username,
                    readOnly = p.ReadOnly,
                    autoLoadKeys = p.AutoLoadKeys,
                    connectTimeoutSeconds = (int)p.ConnectTimeout.TotalSeconds,
                    commandTimeoutSeconds = (int)p.CommandTimeout.TotalSeconds,
                    ssh = p.UseSshTunnel
                        ? new
                        {
                            authMode = p.SshAuthMode.ToString(),
                            host = p.SshHost,
                            port = p.SshPort,
                            username = p.SshUsername,
                            privateKeyPath = p.SshPrivateKeyPath,
                        }
                        : null,
                    notes = p.Notes,
                }),
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            ExportRequested?.Invoke(this, new ExportRequest(
                $"winredis-connections-{DateTime.Now:yyyyMMdd-HHmmss}.json", json));
            NotificationRequested?.Invoke(this, new NotificationMessage(Loc.T("已导出连接配置（不含密码）")));
        }
        catch (Exception ex)
        {
            ErrorText = Loc.T("导出连接配置失败：") + ex.Message;
        }
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(KeySeparator))
            return Loc.T("Key 分隔符不能为空");
        if (KeySeparator.Length > 5)
            return Loc.T("Key 分隔符最多 5 个字符");
        if (DefaultDatabase is < 0 or > 15)
            return Loc.T("默认数据库必须在 0–15 之间");
        if (MonitorIntervalSeconds is < 1 or > 60)
            return Loc.T("监控采样间隔必须在 1–60 秒之间");
        if (CommandHistoryLimit is < 20 or > 2000)
            return Loc.T("命令历史上限必须在 20–2000 条之间");
        return null;
    }

    private AppSettings ToSettings() => AppServices.Settings with
    {
        Theme = SelectedThemeOption?.Value ?? ThemePreference.Light,
        Language = SelectedLanguageOption?.Value ?? AppLanguage.Chinese,
        CloseAction = SelectedCloseAction?.Value ?? CloseAction.Ask,
        UseDirectoryView = UseDirectoryView,
        FontScale = FontScaleFromText(SelectedFontSize),
        KeySeparator = KeySeparator.Trim(),
        DefaultDatabase = Math.Clamp(DefaultDatabase, 0, 15),
        MonitorIntervalSeconds = Math.Clamp(MonitorIntervalSeconds, 1, 60),
        AutoRefreshMonitoring = AutoRefreshMonitoring,
        CommandHistoryEnabled = CommandHistoryEnabled,
        CommandHistoryLimit = Math.Clamp(CommandHistoryLimit, 20, 2000),
        RecentKeysEnabled = RecentKeysEnabled,
        ConfirmProductionWrites = ConfirmProductionWrites,
        ShowKeySize = ShowKeySize,
    };

    private Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        if (ConfirmationRequested is null) return Task.FromResult(false);
        ConfirmationRequested.Invoke(this, request);
        return request.WaitAsync();
    }
}
