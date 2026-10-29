using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.App.Dialogs;

/// <summary>设置/移除 Key 的 TTL。</summary>
public partial class TtlDialog : Window
{
    private static readonly (string Label, long? Seconds)[] Presets =
    [
        (Loc.T("永不过期"), null),
        (Loc.T("60 秒"), 60),
        (Loc.T("5 分钟"), 300),
        (Loc.T("1 小时"), 3600),
        (Loc.T("1 天"), 86400),
        (Loc.T("7 天"), 604800),
        (Loc.T("自定义…"), -1),
    ];

    public TtlDialog(string keyName, TimeSpan? currentTtl, string currentTtlText)
    {
        InitializeComponent();
        CurrentTtlText.Text = Loc.F("Key：{0}    当前 TTL：{1}", keyName, currentTtlText);

        var currentSeconds = (long?)currentTtl?.TotalSeconds;
        var presetIndex = currentSeconds is null
            ? 0
            : Array.FindIndex(Presets, p => p.Seconds == currentSeconds);
        if (presetIndex < 0) presetIndex = Presets.Length - 1;

        PresetCombo.SelectedIndex = presetIndex;
        if (presetIndex == Presets.Length - 1)
            CustomInput.Text = currentSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        Loaded += (_, _) => PresetCombo.Focus();
    }

    /// <summary>null 表示永不过期。</summary>
    public TimeSpan? Result { get; private set; }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomPanel is null) return;
        CustomPanel.Visibility = PresetCombo.SelectedIndex == Presets.Length - 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
        if (CustomPanel.Visibility == Visibility.Visible)
        {
            CustomInput.Focus();
            CustomInput.SelectAll();
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (PresetCombo.SelectedIndex < 0)
        {
            ShowError(Loc.T("请选择过期时间"));
            return;
        }

        var preset = Presets[PresetCombo.SelectedIndex];
        if (preset.Seconds is null)
        {
            Result = null;
            DialogResult = true;
            return;
        }

        if (preset.Seconds > 0)
        {
            Result = TimeSpan.FromSeconds(preset.Seconds.Value);
            DialogResult = true;
            return;
        }

        if (!long.TryParse(CustomInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= 0)
        {
            ShowError(Loc.T("请输入大于 0 的整数秒数"));
            CustomInput.Focus();
            return;
        }

        if (seconds > 10L * 365 * 24 * 60 * 60)
        {
            ShowError(Loc.T("TTL 过大（最多 10 年）"));
            CustomInput.Focus();
            return;
        }

        Result = TimeSpan.FromSeconds(seconds);
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
}
