using System.Windows;
using System.Windows.Input;
using WinRedisClient.Core.Models;

namespace WinRedisClient.App.Dialogs;

/// <summary>关闭主窗口时的选择：最小化到托盘 / 直接退出 / 取消，可选择记住。</summary>
public partial class CloseConfirmDialog : Window
{
    public CloseConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>用户选择的行为；<see cref="CloseAction.Ask"/> 表示取消关闭。</summary>
    public CloseAction Action { get; private set; } = CloseAction.Ask;

    /// <summary>是否记住本次选择。</summary>
    public bool Remember => RememberCheck.IsChecked == true;

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        Action = CloseAction.MinimizeToTray;
        DialogResult = true;
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Action = CloseAction.Exit;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Action = CloseAction.Ask;
        DialogResult = false;
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
}
