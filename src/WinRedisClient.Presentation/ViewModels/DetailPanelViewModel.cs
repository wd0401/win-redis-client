using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Abstractions;
using WinRedisClient.Core.Models;
using WinRedisClient.Core.Services;
using WinRedisClient.Presentation.Support;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

/// <summary>集合中的一行（Hash 字段 / List 元素 / Set 成员 / ZSet 成员 / Stream 消息）。</summary>
public sealed class ElementRowViewModel : ObservableObject
{
    private string _field;
    private string _value;
    private string _score;

    public ElementRowViewModel(ElementData data)
    {
        _field = data.Field;
        _value = data.Value;
        _score = data.Score;
    }

    public string Field
    {
        get => _field;
        private set
        {
            if (!SetProperty(ref _field, value)) return;
            OnPropertyChanged(nameof(FieldDisplay));
        }
    }

    public string Value
    {
        get => _value;
        private set
        {
            if (!SetProperty(ref _value, value)) return;
            OnPropertyChanged(nameof(ValueDisplay));
        }
    }

    public string Score
    {
        get => _score;
        private set
        {
            if (!SetProperty(ref _score, value)) return;
            OnPropertyChanged(nameof(ScoreDisplay));
        }
    }

    public string FieldDisplay => Field;
    public string ValueDisplay => Value;
    public string ScoreDisplay => Score;

    public void Update(ElementData data)
    {
        Field = data.Field;
        Value = data.Value;
        Score = data.Score;
    }
}

/// <summary>
/// Key 详情与编辑器：字符串安全预览/保存、集合分页浏览与真实元素写入。
/// 写操作均通过 IRedisService 执行，生产环境破坏性写入会在执行前二次确认。
/// </summary>
public partial class DetailPanelViewModel : ObservableObject
{
    private const int PreviewByteLimit = 256 * 1024;
    private const int ElementPageSize = 200;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _elementCts;
    private int _loadGeneration;
    private string _loadedEditorText = string.Empty;
    private bool _suppressEditorTracking;
    private string? _elementCursor;
    private KeyItemViewModel? _item;

    // ── 头部 ────────────────────────────────────────────────
    [ObservableProperty] private bool _hasKey;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _keyName = string.Empty;
    [ObservableProperty] private string _keyType = string.Empty;
    [ObservableProperty] private string _typeDisplay = "—";
    [ObservableProperty] private string _ttlText = "—";
    [ObservableProperty] private string _sizeText = "—";
    [ObservableProperty] private string _countText = "—";
    [ObservableProperty] private string _encodingText = "—";
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private string _headerStatus = Loc.T("从左侧选择一个 Key");

    // ── 字符串 ──────────────────────────────────────────────
    [ObservableProperty] private bool _isString;
    [ObservableProperty] private bool _isWrapEnabled = true;
    [ObservableProperty] private bool _isBinary;
    [ObservableProperty] private bool _isTruncated;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _editorText = string.Empty;
    [ObservableProperty] private string _valueInfo = string.Empty;
    [ObservableProperty] private string _editorInfo = string.Empty;
    [ObservableProperty] private string _editorStatus = string.Empty;
    [ObservableProperty] private bool _editorIsReadOnly = true;
    [ObservableProperty] private long _totalBytes;

    // ── 集合 ────────────────────────────────────────────────
    [ObservableProperty] private bool _isCollection;
    [ObservableProperty] private bool _isHash;
    [ObservableProperty] private bool _isList;
    [ObservableProperty] private bool _isSet;
    [ObservableProperty] private bool _isZSet;
    [ObservableProperty] private bool _isStream;
    [ObservableProperty] private bool _isLoadingElements;
    [ObservableProperty] private bool _hasMoreElements;
    [ObservableProperty] private bool _descending;
    [ObservableProperty] private string _elementSummary = string.Empty;
    [ObservableProperty] private string _elementError = string.Empty;
    [ObservableProperty] private ElementRowViewModel? _selectedElement;

    // ── 元素编辑器 ──────────────────────────────────────────
    [ObservableProperty] private bool _isElementEditorOpen;
    [ObservableProperty] private bool _isEditingElement;
    [ObservableProperty] private bool _isSavingElement;
    [ObservableProperty] private string _elementEditorTitle = Loc.T("新增元素");
    [ObservableProperty] private string _inputField = string.Empty;
    [ObservableProperty] private string _inputValue = string.Empty;
    [ObservableProperty] private string _inputScore = string.Empty;
    [ObservableProperty] private bool _showFieldInput;
    [ObservableProperty] private bool _showValueInput;
    [ObservableProperty] private bool _showScoreInput;
    [ObservableProperty] private bool _fieldInputReadOnly;
    [ObservableProperty] private string _fieldLabel = Loc.T("字段");
    [ObservableProperty] private string _valueLabel = Loc.T("值");
    [ObservableProperty] private string _addButtonText = Loc.T("新增");

    public ObservableCollection<ElementRowViewModel> Elements { get; } = [];
    public IReadOnlyList<string> TypeOptions { get; } = ["string", "hash", "list", "set", "zset", "stream"];

    public event EventHandler<NotificationMessage>? NotificationRequested;
    public event EventHandler<ConfirmationRequest>? ConfirmationRequested;
    public event EventHandler<string>? CopyRequested;
    /// <summary>Key 内容或元数据被修改（参数为 Key 名），浏览器可刷新对应行。</summary>
    public event EventHandler<string>? KeyChanged;
    /// <summary>Key 已不存在（被删除/过期）。</summary>
    public event EventHandler<string>? KeyMissing;
    public event EventHandler<string>? RenameKeyRequested;
    public event EventHandler<string>? SetTtlRequested;
    public event EventHandler<string>? DeleteKeyRequested;

    public bool CanWrite => !IsReadOnly;
    public string CopyText => string.IsNullOrEmpty(KeyName) ? string.Empty : KeyName;
    public bool IsEmptyState => !HasKey && !IsLoading;
    public bool CanSave => IsString && !IsBinary && !IsTruncated && IsDirty && !IsSaving && !IsReadOnly && HasKey;
    public bool CanLoadMore => HasMoreElements && !IsLoadingElements;
    public bool ShowOrderToggle => IsList || IsZSet;
    public string OrderText => Descending ? Loc.T("降序 ↓") : Loc.T("升序 ↑");

    public DetailPanelViewModel()
    {
        Reset();
    }

    public async Task LoadAsync(KeyItemViewModel? item, bool force = false)
    {
        if (!force && ReferenceEquals(item, _item) && HasKey) return;

        var generation = Interlocked.Increment(ref _loadGeneration);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _elementCts?.Cancel();
        _elementCts?.Dispose();
        _elementCts = null;

        _item = item;

        if (item is null)
        {
            Reset();
            return;
        }

        var cts = new CancellationTokenSource();
        _loadCts = cts;

        HasKey = true;
        IsLoading = true;
        ErrorClear();
        IsString = false;
        IsCollection = false;
        IsHash = IsList = IsSet = IsZSet = IsStream = false;
        Elements.Clear();
        _elementCursor = null;
        HasMoreElements = false;
        IsElementEditorOpen = false;
        SelectedElement = null;
        EditorText = string.Empty;
        _loadedEditorText = string.Empty;
        IsDirty = false;
        IsBinary = false;
        IsTruncated = false;
        TotalBytes = 0;
        KeyName = item.Name;
        KeyType = item.Type;
        TypeDisplay = TypeDisplayName(item.Type);
        TtlText = item.TtlText;
        SizeText = item.SizeText;
        CountText = item.ElementCountText;
        EncodingText = "—";
        ValueInfo = Loc.T("正在读取…");
        EditorInfo = string.Empty;
        EditorStatus = string.Empty;
        HeaderStatus = Loc.T("正在加载…");
        NotifyEditorState();

        try
        {
            var redis = RequireRedis();
            var meta = await redis.GetMetadataAsync(item.Name, true, cts.Token);
            if (generation != _loadGeneration) return;

            if (meta is null || meta.Type is "none" || meta.Type == "unknown")
            {
                HeaderStatus = Loc.T("Key 不存在或已过期");
                ValueInfo = string.Empty;
                EditorIsReadOnly = true;
                KeyMissing?.Invoke(this, item.Name);
                OnPropertyChanged(nameof(IsEmptyState));
                return;
            }

            ApplyMetadata(meta);
            item.UpdateMetadata(meta);

            if (meta.Type == "string")
                await LoadStringAsync(redis, cts.Token);
            else
                await LoadElementsPageAsync(redis, reset: true, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 切换选择导致取消
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration) return;
            HeaderStatus = Loc.T("加载失败");
            EditorStatus = Loc.T("读取失败：") + UiText.Redact(ex.Message);
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmptyState));
                NotifyEditorState();
            }
        }
    }

    public async Task ReloadAsync(bool userInitiated = false)
    {
        if (_item is null) return;

        if (IsDirty && userInitiated)
        {
            var discard = await ConfirmAsync(new ConfirmationRequest
            {
                Title = Loc.T("放弃未保存的修改？"),
                Message = Loc.F("Key“{0}”的编辑器中存在未保存的修改。重新加载将丢弃这些修改。", KeyName),
                ConfirmText = Loc.T("放弃并重新加载"),
                CancelText = Loc.T("继续编辑"),
                IsDanger = true,
            });
            if (!discard) return;
        }

        await LoadAsync(_item, force: true);
    }

    private void Reset()
    {
        HasKey = false;
        IsLoading = false;
        KeyName = string.Empty;
        KeyType = string.Empty;
        TypeDisplay = "—";
        TtlText = "—";
        SizeText = "—";
        CountText = "—";
        EncodingText = "—";
        HeaderStatus = Loc.T("从左侧选择一个 Key");
        IsString = false;
        IsCollection = false;
        IsHash = IsList = IsSet = IsZSet = IsStream = false;
        Elements.Clear();
        _elementCursor = null;
        HasMoreElements = false;
        IsElementEditorOpen = false;
        SelectedElement = null;
        IsBinary = false;
        IsTruncated = false;
        IsDirty = false;
        TotalBytes = 0;
        ValueInfo = string.Empty;
        EditorInfo = string.Empty;
        EditorStatus = string.Empty;
        _loadedEditorText = string.Empty;
        _suppressEditorTracking = true;
        EditorText = string.Empty;
        _suppressEditorTracking = false;
        EditorIsReadOnly = true;
        ElementSummary = string.Empty;
        ElementError = string.Empty;
        OnPropertyChanged(nameof(IsEmptyState));
        NotifyEditorState();
    }

    private void ApplyMetadata(KeyMetadata meta)
    {
        KeyName = meta.Key;
        KeyType = meta.Type;
        TypeDisplay = TypeDisplayName(meta.Type);
        TtlText = TtlFormatter.Format(meta.TimeToLive);
        SizeText = TtlFormatter.FormatBytes(meta.SizeBytes);
        CountText = meta.ElementCount switch
        {
            null => "—",
            var count when meta.Type == "string" => TtlFormatter.FormatBytes(count),
            var count => count.Value.ToString("N0"),
        };
        SizeText = TtlFormatter.FormatBytes(meta.SizeBytes);
        HeaderStatus = $"{TypeDisplay} · {TtlText}";
        IsReadOnly = AppServices.Redis?.CurrentProfile?.ReadOnly ?? false;

        IsString = meta.Type == "string";
        IsHash = meta.Type == "hash";
        IsList = meta.Type == "list";
        IsSet = meta.Type == "set";
        IsZSet = meta.Type == "zset";
        IsStream = meta.Type == "stream";
        IsCollection = IsHash || IsList || IsSet || IsZSet || IsStream;

        if (IsCollection)
        {
            AddButtonText = meta.Type switch
            {
                "hash" => Loc.T("新增字段"),
                "list" => Loc.T("追加元素"),
                "set" => Loc.T("添加成员"),
                "zset" => Loc.T("添加成员"),
                "stream" => Loc.T("追加消息"),
                _ => Loc.T("新增"),
            };
        }

        OnPropertyChanged(nameof(ShowOrderToggle));
        NotifyEditorState();
    }

    partial void OnIsListChanged(bool value) => OnPropertyChanged(nameof(ShowOrderToggle));
    partial void OnIsZSetChanged(bool value) => OnPropertyChanged(nameof(ShowOrderToggle));
    partial void OnDescendingChanged(bool value) => OnPropertyChanged(nameof(OrderText));

    private async Task LoadStringAsync(IRedisService redis, CancellationToken cancellationToken)
    {
        var preview = await redis.GetStringBytesAsync(KeyName, PreviewByteLimit, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (preview is null)
        {
            HeaderStatus = Loc.T("Key 不存在或已过期");
            ValueInfo = string.Empty;
            EditorIsReadOnly = true;
            KeyMissing?.Invoke(this, KeyName);
            return;
        }

        TotalBytes = preview.TotalLength;
        IsTruncated = preview.IsTruncated;

        _suppressEditorTracking = true;
        var jsonAutoFormatted = false;
        if (UiText.TryDecodeText(preview.Data, out var text, out var encodingName))
        {
            IsBinary = false;
            EncodingText = encodingName;
            var displayText = text;
            if (!IsTruncated && TryPrettyJson(text, objectsOnly: true, out var pretty))
            {
                displayText = pretty;
                jsonAutoFormatted = true;
            }

            EditorText = displayText;
            _loadedEditorText = displayText;
            ValueInfo = IsTruncated
                ? Loc.F("前 {0} / 共 {1}（{2}，已截断只读）", TtlFormatter.FormatBytes(preview.Data.Length), TtlFormatter.FormatBytes(TotalBytes), encodingName)
                : Loc.F("{0} · {1} 文本", TtlFormatter.FormatBytes(TotalBytes), encodingName);
        }
        else
        {
            IsBinary = true;
            EncodingText = Loc.T("二进制");
            EditorText = UiText.HexPreview(preview.Data);
            _loadedEditorText = EditorText;
            ValueInfo = IsTruncated
                ? Loc.F("二进制 · 前 {0} / 共 {1}", TtlFormatter.FormatBytes(preview.Data.Length), TtlFormatter.FormatBytes(TotalBytes))
                : Loc.F("二进制 · {0} · 仅预览", TtlFormatter.FormatBytes(TotalBytes));
        }
        _suppressEditorTracking = false;

        IsDirty = false;
        EditorStatus = jsonAutoFormatted ? Loc.T("JSON 已自动格式化") : string.Empty;
        UpdateEditorInfo();
        NotifyEditorState();
    }

    private async Task LoadElementsPageAsync(IRedisService redis, bool reset, CancellationToken cancellationToken)
    {
        if (!IsCollection) return;

        _elementCts?.Cancel();
        _elementCts?.Dispose();
        _elementCts = null;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _elementCts = cts;

        if (reset)
        {
            Elements.Clear();
            _elementCursor = null;
            ElementError = string.Empty;
        }

        IsLoadingElements = true;
        ElementError = string.Empty;

        try
        {
            var page = await redis.GetCollectionPageAsync(
                KeyName, KeyType, _elementCursor, ElementPageSize, Descending, cts.Token);
            if (cancellationToken.IsCancellationRequested) return;

            if (!string.IsNullOrEmpty(page.Error))
            {
                ElementError = page.Error;
                ElementSummary = page.Error;
                HasMoreElements = false;
                return;
            }

            foreach (var data in page.Items)
                Elements.Add(new ElementRowViewModel(data));

            _elementCursor = page.NextCursor;
            HasMoreElements = !page.IsComplete && !string.IsNullOrEmpty(page.NextCursor);
            ElementSummary = page.TotalCount == 0
                ? Loc.T("集合为空")
                : Loc.F("已显示 {0:N0} / 共 {1:N0} 项", Elements.Count, page.TotalCount);
            CountText = page.TotalCount.ToString("N0");
        }
        catch (OperationCanceledException)
        {
            // 翻页/切换取消
        }
        catch (Exception ex)
        {
            ElementError = Loc.T("加载元素失败：") + UiText.Redact(ex.Message);
            ElementSummary = ElementError;
        }
        finally
        {
            IsLoadingElements = false;
            OnPropertyChanged(nameof(CanLoadMore));
        }
    }

    [RelayCommand]
    private Task Refresh() => ReloadAsync(userInitiated: true);

    [RelayCommand]
    private Task LoadMoreElements() => LoadMoreElementsCoreAsync();

    private async Task LoadMoreElementsCoreAsync()
    {
        if (!CanLoadMore || AppServices.Redis is null) return;
        await LoadElementsPageAsync(AppServices.Redis, reset: false, CancellationToken.None);
    }

    [RelayCommand]
    private async Task ToggleElementOrder()
    {
        if (!ShowOrderToggle || AppServices.Redis is null) return;
        Descending = !Descending;
        await LoadElementsPageAsync(AppServices.Redis, reset: true, CancellationToken.None);
    }

    [RelayCommand]
    private void CopyKey()
    {
        if (!string.IsNullOrEmpty(KeyName))
            CopyRequested?.Invoke(this, KeyName);
    }

    [RelayCommand]
    private void RenameKey()
    {
        if (!string.IsNullOrEmpty(KeyName))
            RenameKeyRequested?.Invoke(this, KeyName);
    }

    [RelayCommand]
    private void SetTtl()
    {
        if (!string.IsNullOrEmpty(KeyName))
            SetTtlRequested?.Invoke(this, KeyName);
    }

    [RelayCommand]
    private void DeleteKey()
    {
        if (!string.IsNullOrEmpty(KeyName))
            DeleteKeyRequested?.Invoke(this, KeyName);
    }

    // ── 字符串保存 ─────────────────────────────────────────

    [RelayCommand]
    private async Task SaveString()
    {
        if (!CanSave || AppServices.Redis is null) return;

        var redis = AppServices.Redis;
        var profile = redis.CurrentProfile;
        var productionWrite = profile?.Environment == EnvironmentTag.Production;

        if (IsReadOnly)
        {
            EditorStatus = Loc.T("只读连接，无法保存");
            return;
        }

        if (productionWrite)
        {
            var confirmed = await ConfirmAsync(new ConfirmationRequest
            {
                Title = Loc.T("生产环境写入确认"),
                Message = Loc.F("即将覆盖生产环境连接“{0}” DB{1} 中的 Key：\\n{2}\\n\\n", profile!.Name, redis.Database, KeyName) +
                          Loc.T("原值将被替换（TTL 保持不变）。此操作不可自动撤销。"),
                ConfirmText = Loc.T("确认写入"),
                CancelText = Loc.T("取消"),
                IsDanger = true,
                RequiredText = KeyName,
            });
            if (!confirmed)
            {
                EditorStatus = Loc.T("已取消保存");
                return;
            }
        }

        IsSaving = true;
        EditorStatus = Loc.T("正在保存…");
        NotifyEditorState();

        try
        {
            var bytes = Encoding.UTF8.GetBytes(EditorText);
            var result = await redis.SetStringAsync(KeyName, bytes, ttl: null, preserveTtl: true,
                confirmed: productionWrite);
            if (result.IsSuccess)
            {
                _loadedEditorText = EditorText;
                IsDirty = false;
                EditorStatus = Loc.F("已保存 · {0} · TTL 已保留", TtlFormatter.FormatBytes(bytes.LongLength));
                await RefreshHeaderAfterWriteAsync(redis);
                KeyChanged?.Invoke(this, KeyName);
                NotificationRequested?.Invoke(this, new NotificationMessage(Loc.F("已保存 Key“{0}”", KeyName)));
            }
            else
            {
                EditorStatus = DescribeFailure(result);
                NotificationRequested?.Invoke(this, new NotificationMessage(EditorStatus, true));
            }
        }
        catch (Exception ex)
        {
            EditorStatus = Loc.T("保存失败：") + UiText.Redact(ex.Message, profile?.Password);
            NotificationRequested?.Invoke(this, new NotificationMessage(EditorStatus, true));
        }
        finally
        {
            IsSaving = false;
            NotifyEditorState();
        }
    }

    [RelayCommand]
    private void RevertString()
    {
        if (!IsString || isEditorUnavailable()) return;
        _suppressEditorTracking = true;
        EditorText = _loadedEditorText;
        _suppressEditorTracking = false;
        IsDirty = false;
        EditorStatus = Loc.T("已还原为最近一次读取的内容");
        UpdateEditorInfo();
        NotifyEditorState();
    }

    [RelayCommand]
    private void FormatJson()
    {
        if (!IsString || IsBinary || IsTruncated || string.IsNullOrWhiteSpace(EditorText)) return;

        if (TryPrettyJson(EditorText, objectsOnly: false, out var formatted))
        {
            EditorText = formatted;
            EditorStatus = Loc.T("JSON 已格式化");
        }
        else
        {
            try
            {
                using var _ = JsonDocument.Parse(EditorText);
                EditorStatus = Loc.T("JSON 无需格式化");
            }
            catch (JsonException ex)
            {
                EditorStatus = Loc.T("JSON 解析失败：") + ex.Message;
            }
        }
    }

    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    /// <summary>解析成功且文本确实变化时输出缩进后的 JSON。</summary>
    private static bool TryPrettyJson(string text, bool objectsOnly, out string formatted)
    {
        formatted = text;
        if (string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            using var document = JsonDocument.Parse(text);
            if (objectsOnly &&
                document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                return false;

            var pretty = JsonSerializer.Serialize(document.RootElement, IndentedJsonOptions);
            if (string.Equals(pretty, text, StringComparison.Ordinal)) return false;

            formatted = pretty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    partial void OnEditorTextChanged(string value)
    {
        if (_suppressEditorTracking)
        {
            UpdateEditorInfo();
            return;
        }

        IsDirty = !string.Equals(value, _loadedEditorText, StringComparison.Ordinal);
        UpdateEditorInfo();
        NotifyEditorState();
    }

    private void UpdateEditorInfo()
    {
        if (!IsString) { EditorInfo = string.Empty; return; }
        var bytes = Encoding.UTF8.GetByteCount(EditorText);
        EditorInfo = IsBinary
            ? Loc.T("二进制内容不参与文本编辑")
            : Loc.F("{0:N0} 字符 · {1}（UTF-8）", EditorText.Length, TtlFormatter.FormatBytes(bytes));
        OnPropertyChanged(nameof(CanSave));
    }

    private bool isEditorUnavailable() => IsBinary || IsTruncated || IsReadOnly;

    private void NotifyEditorState()
    {
        EditorIsReadOnly = !IsString || IsBinary || IsTruncated || IsReadOnly || IsSaving;
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(IsEmptyState));
    }

    // ── 元素编辑 ───────────────────────────────────────────

    [RelayCommand]
    private void BeginAddElement()
    {
        if (!IsCollection || IsReadOnly) return;

        IsEditingElement = false;
        IsElementEditorOpen = true;
        SelectedElement = null;
        ElementEditorTitle = AddButtonText;
        InputField = string.Empty;
        InputValue = string.Empty;
        InputScore = string.Empty;
        FieldInputReadOnly = false;

        ConfigureInputsForType();
    }

    [RelayCommand]
    private void BeginEditElement(ElementRowViewModel? row)
    {
        if (row is null || !IsCollection || IsReadOnly || IsStream) return;

        SelectedElement = row;
        IsEditingElement = true;
        IsElementEditorOpen = true;
        ElementEditorTitle = Loc.T("编辑元素");
        InputField = row.Field;
        InputValue = row.Value;
        InputScore = row.Score;
        FieldInputReadOnly = IsList;
        ConfigureInputsForType();
    }

    [RelayCommand]
    private void CancelElementEditor()
    {
        IsElementEditorOpen = false;
        IsEditingElement = false;
        ElementError = string.Empty;
    }

    private void ConfigureInputsForType()
    {
        ShowFieldInput = IsHash || IsSet || IsZSet || IsStream;
        ShowValueInput = IsHash || IsList || IsStream;
        ShowScoreInput = IsZSet;
        FieldLabel = IsSet ? Loc.T("成员") : IsZSet ? Loc.T("成员") : IsStream ? Loc.T("字段") : IsList ? Loc.T("索引") : Loc.T("字段");
        ValueLabel = IsList ? Loc.T("元素值") : Loc.T("值");
        FieldInputReadOnly = IsList;
    }

    [RelayCommand]
    private async Task CommitElement()
    {
        if (!IsElementEditorOpen || IsSavingElement || AppServices.Redis is null || !IsCollection) return;

        var redis = AppServices.Redis;
        var validation = ValidateElementInput();
        if (validation is not null)
        {
            ElementError = validation;
            return;
        }

        var overwrite = IsEditingElement;
        var destructive = false;
        if (!await ConfirmWriteAsync(overwrite ? Loc.T("覆盖该元素") : Loc.T("写入新元素"), overwrite, destructive))
            return;

        IsSavingElement = true;
        ElementError = string.Empty;

        try
        {
            var result = IsEditingElement
                ? await UpdateElementAsync(redis)
                : await AddElementAsync(redis);

            if (!result.IsSuccess)
            {
                ElementError = DescribeFailure(result);
                NotificationRequested?.Invoke(this, new NotificationMessage(ElementError, true));
                return;
            }

            IsElementEditorOpen = false;
            IsEditingElement = false;
            SelectedElement = null;
            ElementStatusOrSummary(Loc.T("写入成功"));
            await ReloadElementsAndMetadataAsync(redis);
        }
        catch (Exception ex)
        {
            ElementError = Loc.T("写入失败：") + UiText.Redact(ex.Message, redis.CurrentProfile?.Password);
            NotificationRequested?.Invoke(this, new NotificationMessage(ElementError, true));
        }
        finally
        {
            IsSavingElement = false;
        }
    }

    private string? ValidateElementInput()
    {
        if (IsHash || IsStream)
        {
            if (string.IsNullOrEmpty(InputField)) return Loc.T("字段名不能为空");
            if (InputValue is null) return Loc.T("值不能为空（允许空字符串）");
        }
        else if (IsList)
        {
            if (IsEditingElement && SelectedElement is null) return Loc.T("请选择要编辑的元素");
            if (!IsEditingElement && string.IsNullOrEmpty(InputValue)) return Loc.T("元素值不能为空");
        }
        else if (IsSet)
        {
            if (!IsEditingElement && string.IsNullOrEmpty(InputField)) return Loc.T("成员不能为空");
            if (IsEditingElement && string.IsNullOrEmpty(InputField)) return Loc.T("成员不能为空");
        }
        else if (IsZSet)
        {
            if (string.IsNullOrEmpty(InputField)) return Loc.T("成员不能为空");
            if (!double.TryParse(InputScore, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                return Loc.T("Score 必须是有效数字");
        }

        return null;
    }

    private async Task<CommandResult> AddElementAsync(IRedisService redis)
    {
        var confirmed = await IsProductionAsync() && IsProductionConfirmationEnabled();
        return KeyType switch
        {
            "hash" => await redis.ExecuteArgsAsync("HSET", [KeyName, InputField, InputValue],
                CommandRiskLevel.SingleWrite, confirmed),
            "list" => await redis.ExecuteArgsAsync("RPUSH", [KeyName, InputValue],
                CommandRiskLevel.SingleWrite, confirmed),
            "set" => await redis.ExecuteArgsAsync("SADD", [KeyName, InputField],
                CommandRiskLevel.SingleWrite, confirmed),
            "zset" => await redis.ExecuteArgsAsync("ZADD", [KeyName, InputScore, InputField],
                CommandRiskLevel.SingleWrite, confirmed),
            "stream" => await redis.StreamAddAsync(KeyName,
                [new KeyValuePair<string, string>(InputField, InputValue)]),
            _ => FailResult("unsupported_type", Loc.T("当前类型不支持新增元素")),
        };
    }

    private async Task<CommandResult> UpdateElementAsync(IRedisService redis)
    {
        if (SelectedElement is null)
            return FailResult("no_selection", Loc.T("未选择元素"));

        var oldField = SelectedElement.Field;
        var production = await IsProductionAsync();
        var confirmed = production;

        switch (KeyType)
        {
            case "hash":
                if (!string.Equals(oldField, InputField, StringComparison.Ordinal))
                {
                    var set = await redis.ExecuteArgsAsync("HSET", [KeyName, InputField, InputValue],
                        CommandRiskLevel.SingleWrite, confirmed);
                    if (!set.IsSuccess) return set;
                    return await redis.ExecuteArgsAsync("HDEL", [KeyName, oldField],
                        CommandRiskLevel.SingleWrite, confirmed);
                }
                return await redis.ExecuteArgsAsync("HSET", [KeyName, InputField, InputValue],
                    CommandRiskLevel.SingleWrite, confirmed);

            case "list":
                if (!long.TryParse(oldField, out var index))
                    return FailResult("invalid_index", Loc.T("列表索引无效，请刷新后重试"));
                return await redis.ExecuteArgsAsync("LSET", [KeyName, oldField, InputValue],
                    CommandRiskLevel.SingleWrite, confirmed);

            case "set":
                if (string.Equals(oldField, InputField, StringComparison.Ordinal))
                    return SuccessResult();
                var add = await redis.ExecuteArgsAsync("SADD", [KeyName, InputField],
                    CommandRiskLevel.SingleWrite, confirmed);
                if (!add.IsSuccess) return add;
                return await redis.ExecuteArgsAsync("SREM", [KeyName, oldField],
                    CommandRiskLevel.SingleWrite, confirmed);

            case "zset":
                if (!string.Equals(oldField, InputField, StringComparison.Ordinal))
                {
                    var addScore = await redis.ExecuteArgsAsync("ZADD", [KeyName, InputScore, InputField],
                        CommandRiskLevel.SingleWrite, confirmed);
                    if (!addScore.IsSuccess) return addScore;
                    return await redis.ExecuteArgsAsync("ZREM", [KeyName, oldField],
                        CommandRiskLevel.SingleWrite, confirmed);
                }
                return await redis.ExecuteArgsAsync("ZADD", [KeyName, InputScore, InputField],
                    CommandRiskLevel.SingleWrite, confirmed);

            default:
                return FailResult("unsupported_type", Loc.T("Stream 消息不支持编辑"));
        }
    }

    [RelayCommand]
    private async Task DeleteElement(ElementRowViewModel? row)
    {
        if (row is null || AppServices.Redis is null || IsReadOnly) return;

        var redis = AppServices.Redis;
        var what = IsHash ? Loc.F("字段“{0}”", row.Field)
            : IsList ? Loc.F("索引 {0} 处的元素", row.Field)
            : IsSet ? Loc.F("成员“{0}”", row.Field)
            : IsZSet ? Loc.F("成员“{0}”", row.Field)
            : Loc.F("消息“{0}”", row.Field);

        var confirmed = await ConfirmAsync(new ConfirmationRequest
        {
            Title = Loc.T("删除元素确认"),
            Message = Loc.F("将从 Key“{0}”中删除{1}。\\n该操作立即生效，且不可自动撤销。", KeyName, what),
            ConfirmText = Loc.T("删除元素"),
            CancelText = Loc.T("取消"),
            IsDanger = true,
            RequiredText = await IsProductionAsync() ? KeyName : null,
        });
        if (!confirmed) return;

        IsLoadingElements = true;
        try
        {
            CommandResult result = KeyType switch
            {
                "hash" => await redis.ExecuteArgsAsync("HDEL", [KeyName, row.Field],
                    CommandRiskLevel.SingleWrite, true),
                "list" => long.TryParse(row.Field, out var index)
                    ? await redis.ListRemoveAtAsync(KeyName, index, confirmed: true)
                    : FailResult("invalid_index", Loc.T("列表索引无效")),
                "set" => await redis.ExecuteArgsAsync("SREM", [KeyName, row.Field],
                    CommandRiskLevel.SingleWrite, true),
                "zset" => await redis.ExecuteArgsAsync("ZREM", [KeyName, row.Field],
                    CommandRiskLevel.SingleWrite, true),
                "stream" => await redis.ExecuteArgsAsync("XDEL", [KeyName, row.Field],
                    CommandRiskLevel.SingleWrite, true),
                _ => FailResult("unsupported_type", Loc.T("当前类型不支持删除元素")),
            };

            if (!result.IsSuccess)
            {
                ElementError = DescribeFailure(result);
                NotificationRequested?.Invoke(this, new NotificationMessage(ElementError, true));
                return;
            }

            if (SelectedElement == row) SelectedElement = null;
            if (IsElementEditorOpen && IsEditingElement) IsElementEditorOpen = false;
            ElementStatusOrSummary(Loc.T("元素已删除"));
            await ReloadElementsAndMetadataAsync(redis);
        }
        catch (Exception ex)
        {
            ElementError = Loc.T("删除失败：") + UiText.Redact(ex.Message, redis.CurrentProfile?.Password);
            NotificationRequested?.Invoke(this, new NotificationMessage(ElementError, true));
        }
        finally
        {
            IsLoadingElements = false;
        }
    }

    private async Task ReloadElementsAndMetadataAsync(IRedisService redis)
    {
        await LoadElementsPageAsync(redis, reset: true, CancellationToken.None);
        await RefreshHeaderAfterWriteAsync(redis);
        KeyChanged?.Invoke(this, KeyName);
    }

    private void ElementStatusOrSummary(string message)
    {
        ElementSummary = Elements.Count == 0
            ? message
            : Loc.F("{0} · 已显示 {1:N0} 项", message, Elements.Count);
        NotificationRequested?.Invoke(this, new NotificationMessage($"{message}：{KeyName}"));
    }

    private async Task RefreshHeaderAfterWriteAsync(IRedisService redis)
    {
        try
        {
            var meta = await redis.GetMetadataAsync(KeyName, true);
            if (meta is null || meta.Type is "none" or "unknown") return;

            TtlText = TtlFormatter.Format(meta.TimeToLive);
            SizeText = TtlFormatter.FormatBytes(meta.SizeBytes);
            CountText = meta.ElementCount?.ToString("N0") ?? "—";
            KeyType = meta.Type;
            TypeDisplay = TypeDisplayName(meta.Type);
            IsReadOnly = redis.CurrentProfile?.ReadOnly ?? IsReadOnly;

            if (IsString && meta.Type == "string")
            {
                TotalBytes = meta.ElementCount ?? TotalBytes;
                var truncated = IsTruncated;
                ValueInfo = truncated
                    ? Loc.F("前 {0} / 共 {1}（已截断，只读预览）", TtlFormatter.FormatBytes(Math.Min(TotalBytes, PreviewByteLimit)), TtlFormatter.FormatBytes(TotalBytes))
                    : $"{TtlFormatter.FormatBytes(TotalBytes)} · {EncodingText}";
            }
        }
        catch
        {
            // 头部刷新失败不影响已完成的写操作
        }
    }

    // ── 确认与结果辅助 ─────────────────────────────────────

    private async Task<bool> ConfirmWriteAsync(string action, bool overwrite, bool destructive)
    {
        var production = await IsProductionAsync();
        var settings = AppServices.Settings;
        var needConfirm = destructive || (production && (overwrite || settings.ConfirmProductionWrites));
        if (!needConfirm) return true;

        var redis = AppServices.Redis;
        var target = Loc.F("{0} · DB{1} · {2}", redis?.CurrentProfile?.Name ?? "当前连接", redis?.Database ?? 0, KeyName);
        var request = new ConfirmationRequest
        {
            Title = production ? Loc.T("生产环境写入确认") : Loc.T("写操作确认"),
            Message = Loc.F("即将对 {0} {1}。\\n", target, action) +
                      (production ? Loc.T("生产环境操作不可自动撤销。") : Loc.T("该操作立即生效。")),
            ConfirmText = Loc.T("确认写入"),
            CancelText = Loc.T("取消"),
            IsDanger = true,
            RequiredText = production ? KeyName : null,
        };

        return await ConfirmAsync(request);
    }

    private Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        if (ConfirmationRequested is null) return Task.FromResult(false);
        ConfirmationRequested.Invoke(this, request);
        return request.WaitAsync();
    }

    private Task<bool> IsProductionAsync()
        => Task.FromResult(AppServices.Redis?.CurrentProfile?.Environment == EnvironmentTag.Production);

    private static bool IsProductionConfirmationEnabled()
        => AppServices.Settings.ConfirmProductionWrites;

    private static IRedisService RequireRedis()
        => AppServices.Redis is { IsConnected: true } redis
            ? redis
            : throw new InvalidOperationException(Loc.T("尚未连接 Redis"));

    private void ErrorClear() => ElementError = string.Empty;

    private static string DescribeFailure(CommandResult result) => result.ErrorCode switch
    {
        "readonly_mode" => Loc.T("只读连接，写操作已被阻止"),
        "confirmation_required" or "production_confirmation_required" => Loc.T("执行被安全策略阻止：需要确认"),
        "key_not_found" => Loc.T("Key 已不存在或已过期"),
        "index_out_of_range" => Loc.T("索引已失效，列表可能已被其他客户端修改"),
        "target_exists" => Loc.T("目标已存在"),
        "outcome_unknown" => Loc.T("写入结果未知，请刷新确认后重试"),
        _ => result.ErrorMessage ?? (Loc.T("执行失败：") + (result.ErrorCode ?? "unknown")),
    };

    private static CommandResult FailResult(string code, string message) => new()
    {
        Outcome = ExecutionOutcome.Failure,
        ErrorCode = code,
        ErrorMessage = message,
    };

    private static CommandResult SuccessResult() => new() { Outcome = ExecutionOutcome.Success, Output = "OK" };

    private static string TypeDisplayName(string type) => type switch
    {
        "string" => "String",
        "hash" => "Hash",
        "list" => "List",
        "set" => "Set",
        "zset" => "ZSet",
        "stream" => "Stream",
        _ => type,
    };
}
