using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinRedisClient.Presentation.ViewModels;

namespace WinRedisClient.App.Views;

/// <summary>数据浏览页。树形视图选中叶子节点时同步到 Browser.SelectedKey，并跟随 SelectedKey 回选树节点。</summary>
public partial class BrowserPageView : UserControl
{
    private KeyBrowserViewModel? _browser;

    public BrowserPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyTree.IsVisibleChanged += (_, _) => RestoreTreeSelection();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_browser is not null)
        {
            _browser.PropertyChanged -= OnBrowserPropertyChanged;
            _browser.TreeRoots.CollectionChanged -= OnTreeRootsChanged;
        }

        _browser = (e.NewValue as BrowserPageViewModel)?.Browser;
        if (_browser is not null)
        {
            _browser.PropertyChanged += OnBrowserPropertyChanged;
            _browser.TreeRoots.CollectionChanged += OnTreeRootsChanged;
        }
    }

    private void OnTreeRootsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(new Action(RestoreTreeSelection),
            System.Windows.Threading.DispatcherPriority.Loaded);

    private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyBrowserViewModel.SelectedKey))
            RestoreTreeSelection();
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is KeyTreeNodeViewModel { Key: not null } node &&
            DataContext is BrowserPageViewModel page)
        {
            page.Browser.SelectedKey = node.Key;
        }
    }

    /// <summary>展开数据库下拉时刷新各 DB 的 Key 数量；数量仅作展示，失败不打断切换。</summary>
    private async void OnDatabaseDropDownOpened(object? sender, EventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel viewModel)
            await viewModel.RefreshDatabaseKeyCountsAsync();
    }

    /// <summary>搜索框按 Enter 立即执行：本地模糊过滤不访问 Redis，服务端搜索才刷新。</summary>
    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not BrowserPageViewModel page) return;
        e.Handled = true;

        if (page.Browser.IsLocalSearchMode)
        {
            page.Browser.ApplyLocalSearchNow();
            return;
        }

        if (page.Browser.RefreshCommand.CanExecute(null))
            await page.Browser.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary>当代码把 SelectedKey 取消/还原时（例如未保存草稿拦截），同步恢复树上的高亮。</summary>
    private void RestoreTreeSelection()
    {
        if (_browser?.SelectedKey is null || !KeyTree.IsVisible) return;
        SelectNode(KeyTree, _browser.SelectedKey);
    }

    private static bool SelectNode(ItemsControl parent, KeyItemViewModel key)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container)
                continue;

            if (item is KeyTreeNodeViewModel node)
            {
                if (ReferenceEquals(node.Key, key))
                {
                    if (!container.IsSelected)
                    {
                        container.IsSelected = true;
                        container.BringIntoView();
                    }

                    return true;
                }

                if (node.Children.Count > 0)
                {
                    container.IsExpanded = true;
                    if (SelectNode(container, key)) return true;
                }
            }
        }

        return false;
    }
}
