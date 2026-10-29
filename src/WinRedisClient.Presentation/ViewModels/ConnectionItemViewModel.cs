using CommunityToolkit.Mvvm.ComponentModel;
using WinRedisClient.Core.Models;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

/// <summary>单个连接的列表项 ViewModel（同时保存编辑表单的字段）。</summary>
public partial class ConnectionItemViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _group = string.Empty;
    [ObservableProperty] private ConnectionWarningColor _warningColor;
    [ObservableProperty] private EnvironmentTag _environment;
    [ObservableProperty] private string _environmentLabel = string.Empty;
    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private int _port = 6379;
    [ObservableProperty] private int _database;
    [ObservableProperty] private bool _useTls;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private bool _autoLoadKeys = true;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private ConnectionKind _kind = ConnectionKind.Standalone;
    [ObservableProperty] private int _connectTimeoutSeconds = 5;
    [ObservableProperty] private int _commandTimeoutSeconds = 30;
    [ObservableProperty] private bool _useSshTunnel;
    [ObservableProperty] private SshAuthMethod _sshAuthMode = SshAuthMethod.Password;
    [ObservableProperty] private string _sshHost = string.Empty;
    [ObservableProperty] private int _sshPort = 22;
    [ObservableProperty] private string _sshUsername = string.Empty;
    [ObservableProperty] private string _sshPassword = string.Empty;
    [ObservableProperty] private string _sshPrivateKeyPath = string.Empty;
    [ObservableProperty] private string _sshKeyPassphrase = string.Empty;

    [ObservableProperty] private ConnectionState _state = ConnectionState.Disconnected;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _latencyText = "—";
    [ObservableProperty] private string _redisVersion = string.Empty;
    [ObservableProperty] private long? _keyCount;
    [ObservableProperty] private string _lastError = string.Empty;

    public bool IsConnected => State == ConnectionState.Connected;
    public bool IsConnecting => State == ConnectionState.Connecting;
    public bool IsFailed => State == ConnectionState.Failed;

    public string EndpointText => $"{Host}:{Port}";
    public string DatabaseText => $"DB{Database}";
    public string KeyCountText => KeyCount?.ToString("N0") ?? "—";

    public string EnvironmentText => Environment switch
    {
        EnvironmentTag.Production => Loc.T("生产"),
        EnvironmentTag.Staging => Loc.T("预发"),
        EnvironmentTag.Testing => Loc.T("测试"),
        EnvironmentTag.Development => Loc.T("开发"),
        EnvironmentTag.Custom => string.IsNullOrWhiteSpace(EnvironmentLabel) ? Loc.T("自定义") : EnvironmentLabel,
        _ => Environment.ToString(),
    };

    public string EnvironmentFullText => Environment == EnvironmentTag.Production
        ? Loc.T("生产环境")
        : EnvironmentText;

    public string KindText => Kind switch
    {
        ConnectionKind.Standalone => "Standalone",
        ConnectionKind.Sentinel => "Sentinel",
        ConnectionKind.Cluster => "Cluster",
        ConnectionKind.CloudProxy => "Cloud Proxy",
        _ => Kind.ToString(),
    };

    public string StatusText => State switch
    {
        ConnectionState.Connected => Loc.T("已连接"),
        ConnectionState.Connecting => Loc.T("连接中"),
        ConnectionState.Failed => Loc.T("连接失败"),
        _ => Loc.T("未连接"),
    };

    public string SummaryText => IsReadOnly ? Loc.F("{0} · 只读", EndpointText) : EndpointText;

    public string GroupName => string.IsNullOrWhiteSpace(Group) ? Loc.T("未分组") : Group.Trim();
    public bool HasWarningColor => WarningColor != ConnectionWarningColor.None;
    public string WarningColorHex => WarningColorCatalog.For(WarningColor).Hex;
    public string WarningColorText => Loc.F("警告色：{0}", WarningColorCatalog.For(WarningColor).Display);

    public bool IsProduction => Environment == EnvironmentTag.Production;

    public static ConnectionItemViewModel FromProfile(ConnectionProfile profile)
    {
        var item = new ConnectionItemViewModel();
        item.ApplyProfile(profile);
        return item;
    }

    public void ApplyProfile(ConnectionProfile profile)
    {
        Id = profile.Id;
        Name = profile.Name;
        Group = profile.Group ?? string.Empty;
        WarningColor = profile.WarningColor;
        Environment = profile.Environment;
        EnvironmentLabel = profile.EnvironmentLabel ?? string.Empty;
        Host = profile.Host;
        Port = profile.Port;
        Database = profile.Database;
        UseTls = profile.UseTls;
        Username = profile.Username ?? string.Empty;
        Password = profile.Password ?? string.Empty;
        IsReadOnly = profile.ReadOnly;
        AutoLoadKeys = profile.AutoLoadKeys;
        Notes = profile.Notes ?? string.Empty;
        Kind = profile.Kind;
        ConnectTimeoutSeconds = (int)profile.ConnectTimeout.TotalSeconds;
        CommandTimeoutSeconds = (int)profile.CommandTimeout.TotalSeconds;
        UseSshTunnel = profile.UseSshTunnel;
        SshAuthMode = profile.SshAuthMode;
        SshHost = profile.SshHost ?? string.Empty;
        SshPort = profile.SshPort;
        SshUsername = profile.SshUsername ?? string.Empty;
        SshPassword = profile.SshPassword ?? string.Empty;
        SshPrivateKeyPath = profile.SshPrivateKeyPath ?? string.Empty;
        SshKeyPassphrase = profile.SshKeyPassphrase ?? string.Empty;
    }

    public ConnectionProfile ToProfile() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        Group = string.IsNullOrWhiteSpace(Group) ? null : Group.Trim(),
        WarningColor = WarningColor,
        Environment = Environment,
        EnvironmentLabel = string.IsNullOrWhiteSpace(EnvironmentLabel) ? null : EnvironmentLabel.Trim(),
        Host = Host.Trim(),
        Port = Port,
        Database = Math.Clamp(Database, 0, 15),
        UseTls = UseTls,
        Username = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim(),
        Password = string.IsNullOrEmpty(Password) ? null : Password,
        ReadOnly = IsReadOnly,
        AutoLoadKeys = AutoLoadKeys,
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        Kind = Kind,
        ConnectTimeout = TimeSpan.FromSeconds(Math.Clamp(ConnectTimeoutSeconds, 1, 120)),
        CommandTimeout = TimeSpan.FromSeconds(Math.Clamp(CommandTimeoutSeconds, 1, 600)),
        UseSshTunnel = UseSshTunnel,
        SshAuthMode = SshAuthMode,
        SshHost = string.IsNullOrWhiteSpace(SshHost) ? null : SshHost.Trim(),
        SshPort = SshPort,
        SshUsername = string.IsNullOrWhiteSpace(SshUsername) ? null : SshUsername.Trim(),
        SshPassword = SshAuthMode == SshAuthMethod.Password && !string.IsNullOrEmpty(SshPassword) ? SshPassword : null,
        SshPrivateKeyPath = SshAuthMode == SshAuthMethod.PrivateKey && !string.IsNullOrWhiteSpace(SshPrivateKeyPath)
            ? SshPrivateKeyPath.Trim()
            : null,
        SshKeyPassphrase = SshAuthMode == SshAuthMethod.PrivateKey && !string.IsNullOrEmpty(SshKeyPassphrase)
            ? SshKeyPassphrase
            : null,
    };

    public void ResetRuntimeState()
    {
        State = ConnectionState.Disconnected;
        IsActive = false;
        LatencyText = "—";
        RedisVersion = string.Empty;
        KeyCount = null;
        LastError = string.Empty;
    }

    partial void OnStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnGroupChanged(string value) => OnPropertyChanged(nameof(GroupName));

    partial void OnWarningColorChanged(ConnectionWarningColor value)
    {
        OnPropertyChanged(nameof(HasWarningColor));
        OnPropertyChanged(nameof(WarningColorHex));
        OnPropertyChanged(nameof(WarningColorText));
    }

    partial void OnEnvironmentChanged(EnvironmentTag value)
    {
        OnPropertyChanged(nameof(EnvironmentText));
        OnPropertyChanged(nameof(EnvironmentFullText));
        OnPropertyChanged(nameof(IsProduction));
    }

    partial void OnEnvironmentLabelChanged(string value)
    {
        if (Environment == EnvironmentTag.Custom)
            OnPropertyChanged(nameof(EnvironmentText));
    }

    partial void OnHostChanged(string value) => OnPropertyChanged(nameof(EndpointText));
    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(EndpointText));
    partial void OnDatabaseChanged(int value) => OnPropertyChanged(nameof(DatabaseText));
    partial void OnKeyCountChanged(long? value) => OnPropertyChanged(nameof(KeyCountText));
    partial void OnIsReadOnlyChanged(bool value) => OnPropertyChanged(nameof(SummaryText));
}
