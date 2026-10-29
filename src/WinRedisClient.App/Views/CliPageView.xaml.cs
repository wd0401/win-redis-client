using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WinRedisClient.Presentation.ViewModels;

namespace WinRedisClient.App.Views;

/// <summary>命令终端：输出自动滚底、Enter 执行、双击历史填入。</summary>
public partial class CliPageView : UserControl
{
    private INotifyCollectionChanged? _observedLines;

    public CliPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachLines();
        Loaded += (_, _) =>
        {
            AttachLines();
            CommandInput.Focus();
        };
        Unloaded += (_, _) => DetachLines();
    }

    private void AttachLines()
    {
        DetachLines();
        if (DataContext is not CliViewModel viewModel) return;
        _observedLines = viewModel.Lines;
        _observedLines.CollectionChanged += OnLinesChanged;
        ScrollToEnd();
    }

    private void DetachLines()
    {
        if (_observedLines is not null)
            _observedLines.CollectionChanged -= OnLinesChanged;
        _observedLines = null;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ScrollToEnd));

    private void ScrollToEnd()
    {
        if (OutputList.Items.Count == 0) return;
        OutputList.ScrollIntoView(OutputList.Items[^1]);
    }

    private void OnCommandInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        if (DataContext is CliViewModel viewModel && viewModel.ExecuteCommand.CanExecute(null))
        {
            viewModel.ExecuteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnHistoryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: { } entry } && DataContext is CliViewModel viewModel)
            viewModel.UseHistoryCommand.Execute(entry);
    }
}
