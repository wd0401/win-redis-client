using System.Windows.Controls;

namespace WinRedisClient.App.Views;

/// <summary>Key 对比页（纯绑定，比较逻辑在 DiffViewModel）。</summary>
public partial class DiffPageView : UserControl
{
    public DiffPageView()
    {
        InitializeComponent();
    }
}
