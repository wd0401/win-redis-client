using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinRedisClient.Presentation.ViewModels;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.App.Dialogs;

/// <summary>统一的高危/覆盖/删除确认对话框；支持要求输入 Key 名称的二次授权。</summary>
public partial class ConfirmationDialog : Window
{
    private readonly string? _requiredText;

    public ConfirmationDialog(ConfirmationRequest request)
    {
        InitializeComponent();

        TitleText.Text = request.Title;
        MessageText.Text = request.Message;
        ConfirmButton.Content = request.ConfirmText;
        CancelButton.Content = request.CancelText;

        if (request.IsDanger)
        {
            IconBubble.Background = (Brush)FindResource("DangerLightBrush");
            IconGlyph.Foreground = (Brush)FindResource("DangerBrush");
            IconGlyph.Text = "\uE7BA";
        }
        else
        {
            IconBubble.Background = (Brush)FindResource("InfoLightBrush");
            IconGlyph.Foreground = (Brush)FindResource("InfoBrush");
            IconGlyph.Text = "\uE946";
            ConfirmButton.Style = (Style)FindResource("BtnPrimary");
        }

        if (!string.IsNullOrEmpty(request.RequiredText))
        {
            _requiredText = request.RequiredText;
            RequiredPanel.Visibility = Visibility.Visible;
            RequiredHint.Text = Loc.F("该操作影响生产数据，请输入“{0}”以确认：", _requiredText);
            RequiredInput.Tag = _requiredText;
            ConfirmButton.IsEnabled = false;
        }

        Loaded += (_, _) =>
        {
            if (_requiredText is not null)
            {
                RequiredInput.Focus();
                Keyboard.Focus(RequiredInput);
            }
            else
            {
                ConfirmButton.Focus();
            }
        };
    }

    private bool IsRequiredTextSatisfied
        => _requiredText is null || string.Equals(RequiredInput.Text, _requiredText, StringComparison.Ordinal);

    private void OnRequiredTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_requiredText is null) return;
        ConfirmButton.IsEnabled = IsRequiredTextSatisfied;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!IsRequiredTextSatisfied)
        {
            RequiredInput.Focus();
            return;
        }

        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        try
        {
            DialogResult = false;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
}
