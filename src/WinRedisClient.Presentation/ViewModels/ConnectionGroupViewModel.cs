using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

/// <summary>连接分组（目录形式）：支持折叠/展开，分组内承载连接卡片。</summary>
public partial class ConnectionGroupViewModel : ObservableObject
{
    [ObservableProperty] private bool _isExpanded = true;

    public ConnectionGroupViewModel(string? rawGroup)
    {
        RawGroup = string.IsNullOrWhiteSpace(rawGroup) ? null : rawGroup.Trim();
    }

    /// <summary>原始分组名；null 表示“未分组”。</summary>
    public string? RawGroup { get; }

    /// <summary>展示名称；“未分组”用于空分组。</summary>
    public string Name => RawGroup ?? Loc.T("未分组");

    /// <summary>展开/折叠箭头字形（Segoe MDL2）。</summary>
    public string Glyph => IsExpanded ? "\uE70D" : "\uE76B";

    public ObservableCollection<ConnectionItemViewModel> Items { get; } = [];

    public int Count => Items.Count;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Glyph));
}

/// <summary>连接目录列表项：分组标题行或分组内的连接卡片。</summary>
public sealed class ConnectionDirectoryItem
{
    public ConnectionDirectoryItem(ConnectionGroupViewModel group)
    {
        Group = group;
    }

    public ConnectionDirectoryItem(ConnectionGroupViewModel group, ConnectionItemViewModel connection)
    {
        Group = group;
        Connection = connection;
    }

    public ConnectionGroupViewModel Group { get; }
    public ConnectionItemViewModel? Connection { get; }
    public bool IsGroupHeader => Connection is null;

    /// <summary>自动化名称：分组标题与连接卡片区分开，便于 UIA 验证。</summary>
    public string AutomationName => IsGroupHeader ? Loc.F("分组 {0}", Group.Name) : Connection!.Name;
}
