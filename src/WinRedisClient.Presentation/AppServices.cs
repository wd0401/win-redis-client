using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;

namespace WinRedisClient.Presentation;

/// <summary>
/// 服务定位器 — 组合根（App 层）在启动时装配实现。
/// 阶段 0 原型用静态定位简化注入，后续可重构为构造注入。
/// </summary>
public static class AppServices
{
    /// <summary>当前活动的 Redis 会话门面。</summary>
    public static IRedisService? Redis { get; set; }

    /// <summary>创建独立的 Redis 会话（用于连接测试、Key 对比）。由组合根赋值。</summary>
    public static Func<IRedisService>? RedisFactory { get; set; }

    public static IConnectionTester? ConnectionTester { get; set; }
    public static IConnectionStore? ConnectionStore { get; set; }
    public static ISettingsStore? SettingsStore { get; set; }
    public static IHistoryStore? HistoryStore { get; set; }

    /// <summary>当前生效的设置快照。</summary>
    public static AppSettings Settings { get; set; } = AppSettings.Default;

    /// <summary>本地数据目录（连接配置、设置、历史；由组合根赋值）。</summary>
    public static string DataDirectory { get; set; } = string.Empty;
}
