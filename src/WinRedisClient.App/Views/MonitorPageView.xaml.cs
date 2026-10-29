using System.Windows.Controls;

namespace WinRedisClient.App.Views;

/// <summary>实例监控页（纯绑定，采样逻辑在 MonitorViewModel）。</summary>
public partial class MonitorPageView : UserControl
{
    public MonitorPageView()
    {
        InitializeComponent();
    }
}
