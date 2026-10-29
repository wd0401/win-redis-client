using WinRedisClient.Core.Localization;

namespace WinRedisClient.Core.Models;

/// <summary>主题偏好。</summary>
public enum ThemePreference
{
    Light,
    Dark,
    System,
    Salt,
    Sugar,
    Forest,
    Flower,
    Wind,
}

/// <summary>关闭主窗口时的行为。</summary>
public enum CloseAction
{
    /// <summary>每次询问：最小化到托盘或直接退出。</summary>
    Ask,

    /// <summary>最小化到系统托盘，应用继续在后台运行。</summary>
    MinimizeToTray,

    /// <summary>直接退出应用。</summary>
    Exit,
}

/// <summary>本地用户设置。</summary>
public sealed record AppSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.Light;
    /// <summary>界面语言；切换后需要重启生效。</summary>
    public AppLanguage Language { get; init; } = AppLanguage.Chinese;
    /// <summary>关闭主窗口时的行为；默认每次询问。</summary>
    public CloseAction CloseAction { get; init; } = CloseAction.Ask;
    public bool UseDirectoryView { get; init; } = true;
    public string KeySeparator { get; init; } = ":";
    public int DefaultDatabase { get; init; }
    public int MonitorIntervalSeconds { get; init; } = 5;
    public bool AutoRefreshMonitoring { get; init; } = true;
    public bool CommandHistoryEnabled { get; init; } = true;
    public int CommandHistoryLimit { get; init; } = 200;
    public bool RecentKeysEnabled { get; init; } = true;
    public bool ConfirmProductionWrites { get; init; } = true;
    public double KeyPaneWidth { get; init; } = 460;
    public bool KeyPaneCollapsed { get; init; }
    public bool SidebarCollapsed { get; init; }
    public bool ShowKeySize { get; init; } = true;
    public double FontScale { get; init; } = 1.0;
    /// <summary>目录树展开状态：键为 "{connectionId}:{db}"，值为已展开的文件夹完整路径。</summary>
    public Dictionary<string, string[]> ExpandedFolders { get; init; } = new(StringComparer.Ordinal);
    /// <summary>连接分组折叠状态：在此列表中的分组显示为折叠，其余默认展开。</summary>
    public string[] CollapsedConnectionGroups { get; init; } = [];

    public static AppSettings Default { get; } = new();
}
