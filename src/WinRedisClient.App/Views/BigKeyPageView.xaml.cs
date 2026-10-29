using System.Windows.Controls;
using System.Windows.Input;
using WinRedisClient.Presentation.ViewModels;

namespace WinRedisClient.App.Views;

/// <summary>BigKey 分析页：双击结果行在浏览器中打开该 Key。</summary>
public partial class BigKeyPageView : UserControl
{
    public BigKeyPageView()
    {
        InitializeComponent();
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is BigKeyViewModel { SelectedRow: { } row } viewModel)
            viewModel.OpenKeyCommand.Execute(row);
    }
}
