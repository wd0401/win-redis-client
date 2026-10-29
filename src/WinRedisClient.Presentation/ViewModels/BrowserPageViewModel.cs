namespace WinRedisClient.Presentation.ViewModels;

/// <summary>数据浏览页：左侧 Key 列表 + 右侧详情编辑器。</summary>
public sealed class BrowserPageViewModel
{
    public BrowserPageViewModel(KeyBrowserViewModel browser, DetailPanelViewModel detail)
    {
        Browser = browser;
        Detail = detail;
    }

    public KeyBrowserViewModel Browser { get; }
    public DetailPanelViewModel Detail { get; }
}
