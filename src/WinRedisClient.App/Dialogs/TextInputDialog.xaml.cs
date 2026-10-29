using System.Windows;
using System.Windows.Input;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.App.Dialogs;

/// <summary>单行文本输入框（重命名 Key 等）。</summary>
public partial class TextInputDialog : Window
{
    public TextInputDialog(string title, string hint, string initialValue, string? okText = null)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        HintText.Text = hint;
        ValueInput.Text = initialValue;
        OkButton.Content = string.IsNullOrEmpty(okText) ? Loc.T("确定") : okText;
        ValueInput.SelectAll();

        Loaded += (_, _) =>
        {
            ValueInput.Focus();
            ValueInput.CaretIndex = ValueInput.Text.Length;
        };
    }

    public string Value => ValueInput.Text.Trim();

    private void OnValueChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => ErrorText.Visibility = Visibility.Collapsed;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueInput.Text))
        {
            ErrorText.Text = Loc.T("内容不能为空");
            ErrorText.Visibility = Visibility.Visible;
            ValueInput.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
}
