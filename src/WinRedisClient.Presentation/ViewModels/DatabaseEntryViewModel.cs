using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WinRedisClient.Presentation.ViewModels;

/// <summary>数据库下拉项：显示 DB 编号，并在读取后追加 Key 数量。</summary>
public partial class DatabaseEntryViewModel : ObservableObject
{
    public DatabaseEntryViewModel(int number)
    {
        Number = number;
        _text = $"DB {number}";
    }

    public int Number { get; }

    [ObservableProperty] private string _text;

    public void SetCount(long? count)
    {
        Text = count is null
            ? $"DB {Number}"
            : $"DB {Number} · {count.Value.ToString("N0", CultureInfo.InvariantCulture)}";
    }

    public void SetLoading()
    {
        Text = $"DB {Number} · …";
    }
}
