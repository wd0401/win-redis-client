using WinRedisClient.Core.Models;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

/// <summary>连接警告色的展示名称与颜色。</summary>
public sealed record WarningColorOption(ConnectionWarningColor Value, string Display, string Hex);

/// <summary>连接卡片警告色目录；颜色仅用于本地标记，不影响 Redis。</summary>
public static class WarningColorCatalog
{
    public static IReadOnlyList<WarningColorOption> All { get; } =
    [
        new(ConnectionWarningColor.None, Loc.T("无"), "#CBD5E1"),
        new(ConnectionWarningColor.Yellow, Loc.T("黄色"), "#D97706"),
        new(ConnectionWarningColor.Orange, Loc.T("橙色"), "#EA580C"),
        new(ConnectionWarningColor.Red, Loc.T("红色"), "#DC2626"),
        new(ConnectionWarningColor.Purple, Loc.T("紫色"), "#7C3AED"),
        new(ConnectionWarningColor.Blue, Loc.T("蓝色"), "#2563EB"),
        new(ConnectionWarningColor.Green, Loc.T("绿色"), "#059669"),
    ];

    public static WarningColorOption For(ConnectionWarningColor value)
        => All.FirstOrDefault(option => option.Value == value) ?? All[0];
}
