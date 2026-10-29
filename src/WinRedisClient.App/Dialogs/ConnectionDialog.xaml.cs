using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using WinRedisClient.Core.Models;
using WinRedisClient.Presentation.ViewModels;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.App.Dialogs;

/// <summary>新建/编辑连接；密码/私钥口令仅在提交时读取，不回显明文。</summary>
public partial class ConnectionDialog : Window
{
    public ConnectionDialog(ConnectionEditorViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Title = viewModel.Title;
    }

    public ConnectionEditorViewModel ViewModel { get; }
    public ConnectionProfile? SavedProfile { get; private set; }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
        => ViewModel.Password = PasswordBox.Password;

    private void OnSshPasswordChanged(object sender, RoutedEventArgs e)
        => ViewModel.SshPassword = SshPasswordBox.Password;

    private void OnSshKeyPassphraseChanged(object sender, RoutedEventArgs e)
        => ViewModel.SshKeyPassphrase = SshKeyPassphraseBox.Password;

    private void OnClearStoredPasswordChecked(object sender, RoutedEventArgs e)
        => PasswordBox.Clear();

    private void OnClearStoredSshPasswordChecked(object sender, RoutedEventArgs e)
        => SshPasswordBox.Clear();

    private void OnClearStoredKeyPassphraseChecked(object sender, RoutedEventArgs e)
        => SshKeyPassphraseBox.Clear();

    private void OnBrowseSshKeyClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("选择 SSH 私钥文件"),
            CheckFileExists = true,
            CheckPathExists = true,
            Filter = Loc.T("SSH 私钥 (*.pem;*.key;id_rsa;id_ed25519;id_ecdsa)|*.pem;*.key;id_rsa;id_ed25519;id_ecdsa|所有文件 (*.*)|*.*"),
        };

        if (dialog.ShowDialog(this) == true)
            ViewModel.SshPrivateKeyPath = dialog.FileName;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var validation = ViewModel.Validate();
        if (validation is not null)
        {
            ViewModel.ErrorText = validation;
            return;
        }

        SavedProfile = ViewModel.ToProfile();
        DialogResult = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>分组快速选择：下拉列出已有分组 + 预置常用分组；仍可在输入框自定义。</summary>
    private void OnGroupPickClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || ViewModel.GroupOptions.Count == 0) return;

        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        foreach (var option in ViewModel.GroupOptions)
        {
            var item = new MenuItem { Header = option };
            item.Click += (_, _) => ViewModel.Group = option;
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
}
