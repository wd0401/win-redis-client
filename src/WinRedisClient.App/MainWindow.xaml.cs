using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinRedisClient.App.Dialogs;
using WinRedisClient.App.Themes;
using WinRedisClient.Core.Models;
using WinRedisClient.Presentation;
using WinRedisClient.Presentation.ViewModels;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.App;

/// <summary>Shell 窗口：装配 ViewModel 事件、对话框、剪贴板、导出与快捷键。</summary>
public partial class MainWindow : Window
{
    private const string ConnectionDragFormat = "WinRedis.Connection";
    private readonly MainWindowViewModel _viewModel;
    private bool _closeConfirmed;
    private bool _closeInProgress;
    private bool _exitRequested;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private Point _connectionDragStart;
    private ConnectionItemViewModel? _connectionDragItem;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        WireViewModelEvents();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 系统关机/注销时不弹关闭询问，直接走释放流程。
        Application.Current.SessionEnding += (_, _) => _exitRequested = true;
        Closed += (_, _) => DisposeTrayIcon();
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("初始化失败：") + ex.Message, "WinRedis",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void WireViewModelEvents()
    {
        _viewModel.ConfirmationRequested += (_, request) =>
        {
            var dialog = new ConfirmationDialog(request) { Owner = this };
            request.Complete(dialog.ShowDialog() == true);
        };

        _viewModel.CopyRequested += (_, text) => CopyToClipboard(text);
        _viewModel.ExportRequested += async (_, request) => await ExportAsync(request);

        _viewModel.AddConnectionDialogRequested += async (_, _) => await ShowConnectionDialogAsync(null);
        _viewModel.EditConnectionDialogRequested += async (_, item) => await ShowConnectionDialogAsync(item);

        _viewModel.AddKeyDialogRequested += async (_, _) => await ShowCreateKeyDialogAsync();
        _viewModel.RenameKeyDialogRequested += async (_, item) => await ShowRenameKeyDialogAsync(item);
        _viewModel.SetTtlDialogRequested += async (_, item) => await ShowTtlDialogAsync(item);

        _viewModel.ThemeChangeRequested += (_, preference) =>
            ThemeManager.Apply(ThemeManager.FromPreference(preference));
        _viewModel.FontScaleChangeRequested += (_, scale) => FontScaleManager.Apply(scale);
        _viewModel.LanguageChangeRequested += async (_, _) => await ConfirmLanguageRestartAsync();

        _viewModel.OpenDataFolderRequested += (_, _) => OpenDataFolder();
    }

    /// <summary>语言切换需要重启才能让整个界面一致更新；这里询问并支持一键重启。</summary>
    private async Task ConfirmLanguageRestartAsync()
    {
        await Task.Yield();

        var result = MessageBox.Show(
            this,
            Loc.T("新的界面语言已保存。\n重启 WinRedis 后生效，是否现在重启？"),
            Loc.T("界面语言"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    UseShellExecute = true,
                });
            }
        }
        catch
        {
            // 启动新实例失败时保持当前进程，用户可手动重启。
        }

        _closeConfirmed = true;
        Application.Current.Shutdown();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsPaletteOpen) || !_viewModel.IsPaletteOpen)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            PaletteSearchBox.Focus();
            Keyboard.Focus(PaletteSearchBox);
        }));
    }

    // ── 连接分组拖拽 ───────────────────────────────────────

    private void OnConnectionListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _connectionDragItem = null;
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;

        _connectionDragItem = FindConnectionItem(e.OriginalSource as DependencyObject);
        _connectionDragStart = e.GetPosition(null);
    }

    private void OnConnectionListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _connectionDragItem is null) return;

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _connectionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _connectionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            _connectionDragItem = null;
            return;
        }

        var item = _connectionDragItem;
        _connectionDragItem = null;
        _viewModel.Connections.Select(item);
        DragDrop.DoDragDrop(ConnectionList, new DataObject(ConnectionDragFormat, item), DragDropEffects.Move);
    }

    private void OnConnectionListDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(ConnectionDragFormat))
            return;

        var dragged = e.Data.GetData(ConnectionDragFormat) as ConnectionItemViewModel;
        var target = FindConnectionItem(e.OriginalSource as DependencyObject);
        e.Effects = target is not null && !ReferenceEquals(target, dragged)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnConnectionListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(ConnectionDragFormat) is not ConnectionItemViewModel dragged)
            return;

        var target = FindConnectionItem(e.OriginalSource as DependencyObject);
        if (target is null || ReferenceEquals(target, dragged))
            return;

        e.Handled = true;
        await _viewModel.Connections.MoveToGroupAsync(dragged, target.Group);
    }

    private void OnGroupHeaderDragOver(object sender, DragEventArgs e)
    {
        if (sender is not Control header || !e.Data.GetDataPresent(ConnectionDragFormat))
            return;

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        header.Background = TryFindResource("BgSidebarActiveBrush") as Brush;
    }

    private void OnGroupHeaderDragLeave(object sender, DragEventArgs e) => ResetGroupHeader(sender);

    private async void OnGroupHeaderDrop(object sender, DragEventArgs e)
    {
        ResetGroupHeader(sender);
        if (sender is not FrameworkElement { DataContext: ConnectionDirectoryItem { IsGroupHeader: true } directory } ||
            e.Data.GetData(ConnectionDragFormat) is not ConnectionItemViewModel dragged)
        {
            return;
        }

        e.Handled = true;
        await _viewModel.Connections.MoveToGroupAsync(dragged, directory.Group.RawGroup);
    }

    private void ResetGroupHeader(object? sender)
    {
        if (sender is not Control header) return;
        header.Background = Brushes.Transparent;
    }

    private ConnectionItemViewModel? FindConnectionItem(DependencyObject? source)
        => FindAncestor<ListBoxItem>(source)?.DataContext switch
        {
            ConnectionDirectoryItem { Connection: not null } directory => directory.Connection,
            ConnectionItemViewModel item => item,
            _ => null,
        };

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    /// <summary>
    /// 右键卡片菜单（在打开前构建）：设置分组 / 已有分组与预置常用分组 / 新建分组 / 移出分组。
    /// 注意：ContextMenu 顶层项的 Role 是 SubmenuItem，不能挂子菜单，因此分组选项与主菜单项同级展示。
    /// </summary>
    private void OnCardContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: ContextMenu menu }) return;

        // 打开阶段 ContextMenu 的 DataContext 绑定尚未解析，从 PlacementTarget（卡片 Grid）取
        var item = (sender as FrameworkElement)?.DataContext as ConnectionItemViewModel;
        menu.Items.Clear();

        var setGroup = new MenuItem { Header = Loc.T("设置分组…") };
        setGroup.Click += async (_, _) =>
        {
            if (item is null) return;
            var dialog = new TextInputDialog(
                Loc.T("设置分组"),
                Loc.T("输入分组名，相同名称的连接会归入同一组；留空请使用“移出分组”。"),
                item.Group,
                Loc.T("保存")) { Owner = this };
            if (dialog.ShowDialog() == true)
                await _viewModel.Connections.MoveToGroupAsync(item, dialog.Value);
        };
        menu.Items.Add(setGroup);
        menu.Items.Add(new Separator());

        foreach (var option in _viewModel.Connections.GetGroupOptions())
        {
            var child = new MenuItem
            {
                Header = option,
                IsChecked = item is not null &&
                            string.Equals(item.Group, option, StringComparison.Ordinal),
            };
            child.Click += async (_, _) =>
            {
                if (item is not null)
                    await _viewModel.Connections.MoveToGroupAsync(item, option);
            };
            menu.Items.Add(child);
        }

        var createNew = new MenuItem { Header = Loc.T("新建分组…") };
        createNew.Click += async (_, _) =>
        {
            if (item is null) return;
            var dialog = new TextInputDialog(Loc.T("移动到新分组"), Loc.T("输入新分组名，连接将移动到该分组。"), string.Empty, Loc.T("移动"))
            {
                Owner = this,
            };
            if (dialog.ShowDialog() == true)
                await _viewModel.Connections.MoveToGroupAsync(item, dialog.Value);
        };
        menu.Items.Add(createNew);
        menu.Items.Add(new Separator());

        var clearGroup = new MenuItem { Header = Loc.T("移出分组") };
        clearGroup.Click += async (_, _) =>
        {
            if (item is not null)
                await _viewModel.Connections.MoveToGroupAsync(item, string.Empty);
        };
        menu.Items.Add(clearGroup);
    }

    // ── 对话框 ─────────────────────────────────────────────

    private async Task ShowConnectionDialogAsync(ConnectionItemViewModel? item)
    {
        var editor = item is null ? new ConnectionEditorViewModel() : new ConnectionEditorViewModel(item);
        editor.GroupOptions = _viewModel.Connections.GetGroupOptions();
        if (item is null)
            editor.Database = Math.Clamp(AppServices.Settings.DefaultDatabase, 0, 15);

        var dialog = new ConnectionDialog(editor) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SavedProfile is null) return;

        await _viewModel.SaveConnectionAsync(dialog.SavedProfile);
    }

    private async Task ShowCreateKeyDialogAsync()
    {
        var context = _viewModel.IsConnected
            ? $"{_viewModel.ActiveConnection?.Name} · DB{_viewModel.DatabaseIndex}"
            : Loc.T("未连接");
        var dialog = new CreateKeyDialog(context) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await _viewModel.CreateKeyAsync(dialog.KeyName, dialog.KeyType, dialog.Values,
            dialog.Ttl, dialog.AllowOverwrite);
    }

    private async Task ShowRenameKeyDialogAsync(KeyItemViewModel item)
    {
        var dialog = new TextInputDialog(Loc.T("重命名 Key"), Loc.F("输入 {0} 的新名称；目标已存在时操作会被拒绝。", item.Name),
            item.Name, Loc.T("重命名")) { Owner = this };
        if (dialog.ShowDialog() == true)
            await _viewModel.RenameKeyAsync(item, dialog.Value);
    }

    private async Task ShowTtlDialogAsync(KeyItemViewModel item)
    {
        var dialog = new TtlDialog(item.Name, item.Ttl, item.TtlText) { Owner = this };
        if (dialog.ShowDialog() == true)
            await _viewModel.SetTtlAsync(item, dialog.Result);
    }

    // ── 剪贴板 / 导出 / 文件夹 ──────────────────────────────

    private void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            _viewModel.Notify(Loc.T("已复制到剪贴板"));
        }
        catch (Exception ex)
        {
            _viewModel.Notify(Loc.T("复制失败：") + ex.Message, true);
        }
    }

    private async Task ExportAsync(ExportRequest request)
    {
        var extension = Path.GetExtension(request.SuggestedFileName);
        var filter = extension.ToLowerInvariant() switch
        {
            ".csv" => Loc.T("CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"),
            ".json" => Loc.T("JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*"),
            ".txt" => Loc.T("文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*"),
            _ => Loc.T("所有文件 (*.*)|*.*"),
        };

        var dialog = new SaveFileDialog
        {
            FileName = request.SuggestedFileName,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await File.WriteAllTextAsync(dialog.FileName, request.Content, new UTF8Encoding(true));
            _viewModel.Notify(Loc.T("已导出：") + dialog.FileName);
        }
        catch (Exception ex)
        {
            _viewModel.Notify(Loc.T("导出失败：") + ex.Message, true);
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            if (string.IsNullOrEmpty(AppServices.DataDirectory))
            {
                _viewModel.Notify(Loc.T("本地数据目录尚未初始化"), true);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = AppServices.DataDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _viewModel.Notify(Loc.T("打开数据目录失败：") + ex.Message, true);
        }
    }

    // ── 窗口按钮 ───────────────────────────────────────────

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = System.Windows.WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == System.Windows.WindowState.Maximized
            ? System.Windows.WindowState.Normal
            : System.Windows.WindowState.Maximized;

    // ── 无边框窗口最大化：限制在当前显示器的工作区，避免遮挡任务栏 ──

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WindowHook);
    }

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
            ApplyWorkAreaBounds(hwnd, lParam);
        return IntPtr.Zero;
    }

    /// <summary>
    /// WindowStyle=None + WindowChrome 时，WPF 默认按整个显示器最大化，
    /// 会覆盖任务栏。这里用当前显示器的 rcWork 作为最大化边界。
    /// </summary>
    private static void ApplyWorkAreaBounds(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var work = info.Work;
        var screen = info.Monitor;

        // 最大化时窗口的客户区应正好铺满工作区；WindowChrome 的隐藏缩放边
        // 由 WPF 自己处理，这里只约束上限，避免把任务栏盖住。
        mmi.MaxPosition.X = work.Left - screen.Left;
        mmi.MaxPosition.Y = work.Top - screen.Top;
        mmi.MaxSize.X = work.Right - work.Left;
        mmi.MaxSize.Y = work.Bottom - work.Top;

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed) return;

        // 关闭流程需要异步确认/释放连接：先取消本次关闭，确认后再真正关闭。
        e.Cancel = true;

        // 用户可能在确认框显示期间再次点击关闭（或快速连点两次），
        // 这里挡住重入，避免第二个流程在窗口已开始关闭时再次调用 Close()。
        if (_closeInProgress) return;

        // 关闭策略：询问 / 最小化到托盘 / 直接退出（从托盘“退出”时跳过询问）。
        if (!_exitRequested)
        {
            var policy = AppServices.Settings.CloseAction;
            if (policy == CloseAction.Ask)
            {
                var choice = new CloseConfirmDialog { Owner = this };
                if (choice.ShowDialog() != true) return;

                if (choice.Remember)
                    _ = PersistCloseActionAsync(choice.Action);

                if (choice.Action == CloseAction.MinimizeToTray)
                {
                    HideToTray();
                    return;
                }
            }
            else if (policy == CloseAction.MinimizeToTray)
            {
                HideToTray();
                return;
            }
        }

        _closeInProgress = true;
        try
        {
            if (!await _viewModel.ConfirmCloseAsync()) return;

            _closeConfirmed = true;
            await _viewModel.DisposeAsync();
        }
        catch (Exception ex)
        {
            _closeConfirmed = false;
            _viewModel.Notify(Loc.T("退出时发生错误：") + ex.Message, true);
            return;
        }
        finally
        {
            _closeInProgress = false;
        }

        // 不能在 Closing 事件内部直接调用 Close()（会抛“窗口关闭期间”异常），
        // 统一通过 Dispatcher 延迟到事件分发结束后再关闭。
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closeConfirmed && IsVisible) Close();
        }));
    }

    private static async Task PersistCloseActionAsync(CloseAction action)
    {
        var store = AppServices.SettingsStore;
        if (store is null) return;

        try
        {
            var updated = AppServices.Settings with { CloseAction = action };
            await store.SaveAsync(updated);
            AppServices.Settings = updated;
        }
        catch
        {
            // 记住选择失败不影响关闭流程
        }
    }

    // ── 系统托盘 ───────────────────────────────────────────

    /// <summary>隐藏主窗口并保留托盘图标，应用继续在后台运行。</summary>
    private void HideToTray()
    {
        EnsureTrayIcon();

        ShowInTaskbar = false;
        Hide();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(
                2000,
                "WinRedis",
                Loc.T("WinRedis 仍在后台运行，可在此图标恢复窗口。"),
                System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null) return;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        var showItem = new System.Windows.Forms.ToolStripMenuItem(Loc.T("显示主窗口"));
        showItem.Click += (_, _) => RestoreFromTray();
        var exitItem = new System.Windows.Forms.ToolStripMenuItem(Loc.T("退出"));
        exitItem.Click += (_, _) => ExitFromTray();
        menu.Items.Add(showItem);
        menu.Items.Add(exitItem);

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "WinRedis",
            ContextMenuStrip = menu,
            Visible = false,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is not null) return icon;
            }
        }
        catch
        {
            // 回退到系统默认图标
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void RestoreFromTray()
    {
        if (_trayIcon is not null) _trayIcon.Visible = false;

        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null) return;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    // ── 交互 ───────────────────────────────────────────────

    private async void OnConnectionDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: ConnectionItemViewModel item }) return;
        if (item.IsConnected)
            await _viewModel.DisconnectAsync(item);
        else
            await _viewModel.ConnectAsync(item);
    }

    private void OnPaletteDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedPaletteItem is not null)
            _viewModel.ExecutePaletteItemCommand.Execute(null);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (ctrl && e.Key == Key.K)
        {
            _viewModel.OpenPaletteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.N)
        {
            _viewModel.Connections.AddConnectionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5 || (ctrl && e.Key == Key.R))
        {
            _viewModel.RefreshCurrentPageCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.S && _viewModel.CurrentPage is BrowserPageViewModel)
        {
            if (_viewModel.Detail.CanSave)
            {
                _viewModel.Detail.SaveStringCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (_viewModel.IsPaletteOpen)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    _viewModel.ClosePaletteCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.Down:
                    _viewModel.MovePaletteSelection(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    _viewModel.MovePaletteSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Enter:
                    _viewModel.ExecutePaletteItemCommand.Execute(null);
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Escape)
        {
            if (_viewModel.Detail.IsElementEditorOpen)
            {
                _viewModel.Detail.CancelElementEditorCommand.Execute(null);
                e.Handled = true;
            }
            else if (_viewModel.CurrentPage is BrowserPageViewModel && _viewModel.Browser.IsScanning)
            {
                _viewModel.Browser.CancelScanCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
