using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.App.Dialogs;

/// <summary>创建 string / hash / list / set / zset / stream 的初始 Key。</summary>
public partial class CreateKeyDialog : Window
{
    private static readonly string[] Types = ["string", "hash", "list", "set", "zset", "stream"];

    public CreateKeyDialog(string context)
    {
        InitializeComponent();
        ContextText.Text = context;
        TypeCombo.ItemsSource = Types;
        TypeCombo.SelectedIndex = 0;
        UpdateTypeUi();

        Loaded += (_, _) => NameInput.Focus();
    }

    public string KeyName => NameInput.Text.Trim();
    public string KeyType => TypeCombo.SelectedItem as string ?? "string";
    public IReadOnlyList<string> Values { get; private set; } = [];
    public TimeSpan? Ttl { get; private set; }
    public bool AllowOverwrite => OverwriteCheck.IsChecked == true;

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FieldPanel is null) return;
        UpdateTypeUi();
    }

    private void UpdateTypeUi()
    {
        var type = KeyType;
        ErrorText.Visibility = Visibility.Collapsed;

        switch (type)
        {
            case "hash":
                FieldPanel.Visibility = Visibility.Visible;
                FieldLabel.Text = Loc.T("字段");
                ValueLabel.Text = Loc.T("值");
                ValueHint.Text = Loc.T("写入 1 个初始字段");
                ValueInput.Height = 110;
                break;
            case "zset":
                FieldPanel.Visibility = Visibility.Visible;
                FieldLabel.Text = Loc.T("成员");
                ValueLabel.Text = "Score";
                ValueHint.Text = Loc.T("Score 必须是数字，例如 1、2.5");
                ValueInput.Height = 34;
                break;
            case "stream":
                FieldPanel.Visibility = Visibility.Visible;
                FieldLabel.Text = Loc.T("字段");
                ValueLabel.Text = Loc.T("值");
                ValueHint.Text = Loc.T("写入一条消息（XADD *）");
                ValueInput.Height = 110;
                break;
            case "list":
                FieldPanel.Visibility = Visibility.Collapsed;
                ValueLabel.Text = Loc.T("元素");
                ValueHint.Text = Loc.T("每行一个元素，按顺序 RPUSH，最多 500 行");
                ValueInput.Height = 150;
                break;
            case "set":
                FieldPanel.Visibility = Visibility.Collapsed;
                ValueLabel.Text = Loc.T("成员");
                ValueHint.Text = Loc.T("每行一个成员，重复自动去重，最多 500 行");
                ValueInput.Height = 150;
                break;
            default:
                FieldPanel.Visibility = Visibility.Collapsed;
                ValueLabel.Text = Loc.T("值");
                ValueHint.Text = Loc.T("String 初始值，支持多行文本");
                ValueInput.Height = 150;
                break;
        }
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameInput.Text))
        {
            Fail(Loc.T("Key 名称不能为空"), NameInput);
            return;
        }

        if (!TryParseTtl(out var ttl))
            return;

        var type = KeyType;
        var field = FieldInput.Text;
        var value = ValueInput.Text;

        switch (type)
        {
            case "string":
                Values = [value];
                break;

            case "hash":
                if (field.Length == 0)
                {
                    Fail(Loc.T("Hash 字段不能为空"), FieldInput);
                    return;
                }
                Values = [field, value];
                break;

            case "zset":
                if (field.Length == 0)
                {
                    Fail(Loc.T("ZSet 成员不能为空"), FieldInput);
                    return;
                }
                if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var score) ||
                    double.IsNaN(score) || double.IsInfinity(score))
                {
                    Fail(Loc.T("Score 必须是有效数字"), ValueInput);
                    return;
                }
                Values = [field, score.ToString(CultureInfo.InvariantCulture)];
                break;

            case "stream":
                if (field.Length == 0)
                {
                    Fail(Loc.T("Stream 字段不能为空"), FieldInput);
                    return;
                }
                Values = [field, value];
                break;

            case "list":
            case "set":
                var lines = value.Replace("\r\n", "\n").Split('\n')
                    .Select(line => line.TrimEnd('\r'))
                    .Where(line => line.Length > 0)
                    .ToList();
                if (lines.Count == 0)
                {
                    Fail(type == "list" ? Loc.T("List 至少需要一个元素") : Loc.T("Set 至少需要一个成员"), ValueInput);
                    return;
                }
                if (lines.Count > 500)
                {
                    Fail(Loc.T("一次最多写入 500 个元素，请分批创建"), ValueInput);
                    return;
                }
                Values = lines;
                break;

            default:
                Fail(Loc.T("未知的 Key 类型"), null);
                return;
        }

        Ttl = ttl;
        DialogResult = true;
    }

    private bool TryParseTtl(out TimeSpan? ttl)
    {
        ttl = null;
        var text = TtlInput.Text.Trim();
        if (text.Length == 0 || text == "0") return true;

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
        {
            Fail(Loc.T("TTL 必须是大于 0 的整数秒数，或留空/0 表示永不过期"), TtlInput);
            return false;
        }

        if (seconds > 10L * 365 * 24 * 60 * 60)
        {
            Fail(Loc.T("TTL 过大（最多 10 年）"), TtlInput);
            return false;
        }

        ttl = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private void Fail(string message, Control? focusTarget)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        focusTarget?.Focus();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
}
