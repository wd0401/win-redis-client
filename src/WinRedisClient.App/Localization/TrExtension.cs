using System.Windows.Markup;
using WinRedisClient.Core.Localization;

namespace WinRedisClient.App.Localization;

/// <summary>
/// XAML 文案本地化：<c>Text="{loc:Tr '搜索'}"</c>。
/// 以中文原文为键，按启动时确定的界面语言返回文案（切换语言需重启）。
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string source) => Source = source;

    [ConstructorArgument("source")]
    public string Source { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => string.IsNullOrEmpty(Source) ? string.Empty : Localizer.Translate(Source);
}
