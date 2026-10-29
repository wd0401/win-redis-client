using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinRedisClient.Core.Models;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Presentation.ViewModels;

public sealed record EnvironmentOption(EnvironmentTag Value, string Display);

/// <summary>连接编辑表单：验证、测试连接、保留已保存密码（不回显明文）。</summary>
public partial class ConnectionEditorViewModel : ObservableObject
{
    private string? _storedPassword;
    private string? _storedSshPassword;
    private string? _storedKeyPassphrase;
    private readonly Guid _id;
    private bool _isEdit;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _group = string.Empty;
    [ObservableProperty] private ConnectionWarningColor _warningColor;
    [ObservableProperty] private EnvironmentTag _environment = EnvironmentTag.Development;
    [ObservableProperty] private string _environmentLabel = string.Empty;
    [ObservableProperty] private string _host = "127.0.0.1";
    [ObservableProperty] private int _port = 6379;
    [ObservableProperty] private int _database;
    [ObservableProperty] private bool _useTls;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _hasStoredPassword;
    [ObservableProperty] private bool _clearStoredPassword;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private bool _autoLoadKeys = true;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private int _connectTimeoutSeconds = 5;
    [ObservableProperty] private int _commandTimeoutSeconds = 30;
    [ObservableProperty] private bool _useSshTunnel;
    [ObservableProperty] private SshAuthMethod _sshAuthMode = SshAuthMethod.Password;
    [ObservableProperty] private string _sshAuthModeText = Loc.T("密码");
    [ObservableProperty] private string _sshHost = string.Empty;
    [ObservableProperty] private int _sshPort = 22;
    [ObservableProperty] private string _sshUsername = string.Empty;
    [ObservableProperty] private string _sshPassword = string.Empty;
    [ObservableProperty] private bool _hasStoredSshPassword;
    [ObservableProperty] private bool _clearStoredSshPassword;
    [ObservableProperty] private string _sshPrivateKeyPath = string.Empty;
    [ObservableProperty] private string _sshKeyPassphrase = string.Empty;
    [ObservableProperty] private bool _hasStoredKeyPassphrase;
    [ObservableProperty] private bool _clearStoredKeyPassphrase;

    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string _testStatus = Loc.T("尚未测试连接，可先验证再保存");
    [ObservableProperty] private bool _testSucceeded;
    [ObservableProperty] private string _errorText = string.Empty;

    public IReadOnlyList<string> SshAuthOptions { get; } = [Loc.T("密码"), Loc.T("私钥文件")];

    public bool UseSshPassword => SshAuthMode == SshAuthMethod.Password;
    public bool UseSshKey => SshAuthMode == SshAuthMethod.PrivateKey;

    public IReadOnlyList<EnvironmentOption> EnvironmentOptions { get; } =
    [
        new(EnvironmentTag.Development, Loc.T("开发")),
        new(EnvironmentTag.Testing, Loc.T("测试")),
        new(EnvironmentTag.Staging, Loc.T("预发")),
        new(EnvironmentTag.Production, Loc.T("生产")),
        new(EnvironmentTag.Custom, Loc.T("自定义")),
    ];

    public IReadOnlyList<int> DatabaseOptions { get; } = Enumerable.Range(0, 16).ToList();
    public IReadOnlyList<WarningColorOption> WarningColorOptions { get; } = WarningColorCatalog.All;

    /// <summary>分组快速选择列表：由调用方用“已有分组 + 预置常用分组”填充。</summary>
    public IReadOnlyList<string> GroupOptions { get; set; } = [];

    public ConnectionEditorViewModel()
    {
        _id = Guid.NewGuid();
        Title = Loc.T("新建连接");
        Name = Loc.T("本地 Redis");
    }

    public ConnectionEditorViewModel(ConnectionItemViewModel item)
    {
        _id = item.Id;
        _isEdit = true;
        Title = Loc.T("编辑连接");
        Name = item.Name;
        Group = item.Group;
        WarningColor = item.WarningColor;
        Environment = item.Environment;
        EnvironmentLabel = item.EnvironmentLabel;
        Host = item.Host;
        Port = item.Port;
        Database = item.Database;
        UseTls = item.UseTls;
        Username = item.Username;
        _storedPassword = string.IsNullOrEmpty(item.Password) ? null : item.Password;
        HasStoredPassword = _storedPassword is not null;
        IsReadOnly = item.IsReadOnly;
        AutoLoadKeys = item.AutoLoadKeys;
        Notes = item.Notes;
        ConnectTimeoutSeconds = item.ConnectTimeoutSeconds;
        CommandTimeoutSeconds = item.CommandTimeoutSeconds;
        UseSshTunnel = item.UseSshTunnel;
        SshAuthMode = item.SshAuthMode;
        SshAuthModeText = item.SshAuthMode == SshAuthMethod.PrivateKey ? Loc.T("私钥文件") : Loc.T("密码");
        SshHost = item.SshHost;
        SshPort = item.SshPort;
        SshUsername = item.SshUsername;
        _storedSshPassword = string.IsNullOrEmpty(item.SshPassword) ? null : item.SshPassword;
        HasStoredSshPassword = _storedSshPassword is not null;
        SshPrivateKeyPath = item.SshPrivateKeyPath;
        _storedKeyPassphrase = string.IsNullOrEmpty(item.SshKeyPassphrase) ? null : item.SshKeyPassphrase;
        HasStoredKeyPassphrase = _storedKeyPassphrase is not null;
    }

    public string Title { get; }
    public bool IsEdit => _isEdit;
    public bool IsCustomEnvironment => Environment == EnvironmentTag.Custom;
    public string ConnectionKindNote => Loc.T("当前版本支持 Standalone 连接（可启用 TLS 或 SSH 隧道）");

    partial void OnEnvironmentChanged(EnvironmentTag value)
        => OnPropertyChanged(nameof(IsCustomEnvironment));

    partial void OnSshAuthModeTextChanged(string value)
    {
        SshAuthMode = value == Loc.T("私钥文件") ? SshAuthMethod.PrivateKey : SshAuthMethod.Password;
    }

    partial void OnSshAuthModeChanged(SshAuthMethod value)
    {
        OnPropertyChanged(nameof(UseSshPassword));
        OnPropertyChanged(nameof(UseSshKey));
    }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return Loc.T("连接名称不能为空");
        if (Name.Trim().Length > 60) return Loc.T("连接名称最多 60 个字符");
        if (Group.Trim().Length > 40) return Loc.T("分组名最多 40 个字符");
        if (string.IsNullOrWhiteSpace(Host)) return Loc.T("Host 不能为空");
        if (Port is <= 0 or > 65535) return Loc.T("端口必须在 1–65535 之间");
        if (Environment == EnvironmentTag.Custom && string.IsNullOrWhiteSpace(EnvironmentLabel))
            return Loc.T("自定义环境需要填写显示名称");
        if (ConnectTimeoutSeconds is < 1 or > 120) return Loc.T("连接超时必须在 1–120 秒之间");
        if (CommandTimeoutSeconds is < 1 or > 600) return Loc.T("命令超时必须在 1–600 秒之间");
        if (UseSshTunnel)
        {
            if (string.IsNullOrWhiteSpace(SshHost)) return Loc.T("启用 SSH 隧道后，SSH Host 不能为空");
            if (SshPort is <= 0 or > 65535) return Loc.T("SSH 端口必须在 1–65535 之间");
            if (string.IsNullOrWhiteSpace(SshUsername)) return Loc.T("启用 SSH 隧道后，SSH 用户名不能为空");

            if (SshAuthMode == SshAuthMethod.PrivateKey)
            {
                if (string.IsNullOrWhiteSpace(SshPrivateKeyPath))
                    return Loc.T("SSH 私钥认证需要选择私钥文件");
                if (!File.Exists(SshPrivateKeyPath.Trim()))
                    return Loc.F("SSH 私钥文件不存在：{0}", SshPrivateKeyPath.Trim());
            }
            else if (string.IsNullOrEmpty(SshPassword) && _storedSshPassword is null)
            {
                return Loc.T("SSH 密码认证需要输入密码或已保存的密码");
            }
        }

        return null;
    }

    public string ResolvePassword()
    {
        if (!string.IsNullOrEmpty(Password)) return Password;
        if (ClearStoredPassword) return string.Empty;
        return _storedPassword ?? string.Empty;
    }

    public string ResolveSshPassword()
    {
        if (!string.IsNullOrEmpty(SshPassword)) return SshPassword;
        if (ClearStoredSshPassword) return string.Empty;
        return _storedSshPassword ?? string.Empty;
    }

    public string ResolveKeyPassphrase()
    {
        if (!string.IsNullOrEmpty(SshKeyPassphrase)) return SshKeyPassphrase;
        if (ClearStoredKeyPassphrase) return string.Empty;
        return _storedKeyPassphrase ?? string.Empty;
    }

    public ConnectionProfile ToProfile()
    {
        var password = ResolvePassword();
        var sshPassword = ResolveSshPassword();
        var keyPassphrase = ResolveKeyPassphrase();

        return new ConnectionProfile
        {
            Id = _id,
            Name = Name.Trim(),
            Group = string.IsNullOrWhiteSpace(Group) ? null : Group.Trim(),
            WarningColor = WarningColor,
            Environment = Environment,
            EnvironmentLabel = Environment == EnvironmentTag.Custom ? EnvironmentLabel.Trim() : null,
            Host = Host.Trim(),
            Port = Port,
            Database = Math.Clamp(Database, 0, 15),
            UseTls = UseTls,
            Username = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim(),
            Password = string.IsNullOrEmpty(password) ? null : password,
            ReadOnly = IsReadOnly,
            AutoLoadKeys = AutoLoadKeys,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Kind = ConnectionKind.Standalone,
            ConnectTimeout = TimeSpan.FromSeconds(ConnectTimeoutSeconds),
            CommandTimeout = TimeSpan.FromSeconds(CommandTimeoutSeconds),
            UseSshTunnel = UseSshTunnel,
            SshAuthMode = SshAuthMode,
            SshHost = UseSshTunnel && !string.IsNullOrWhiteSpace(SshHost) ? SshHost.Trim() : null,
            SshPort = SshPort,
            SshUsername = UseSshTunnel && !string.IsNullOrWhiteSpace(SshUsername) ? SshUsername.Trim() : null,
            SshPassword = UseSshTunnel && SshAuthMode == SshAuthMethod.Password && !string.IsNullOrEmpty(sshPassword)
                ? sshPassword
                : null,
            SshPrivateKeyPath = UseSshTunnel && SshAuthMode == SshAuthMethod.PrivateKey && !string.IsNullOrWhiteSpace(SshPrivateKeyPath)
                ? SshPrivateKeyPath.Trim()
                : null,
            SshKeyPassphrase = UseSshTunnel && SshAuthMode == SshAuthMethod.PrivateKey && !string.IsNullOrEmpty(keyPassphrase)
                ? keyPassphrase
                : null,
        };
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (IsTesting) return;

        var validation = Validate();
        if (validation is not null)
        {
            ErrorText = validation;
            TestStatus = string.Empty;
            TestSucceeded = false;
            return;
        }

        var tester = AppServices.ConnectionTester;
        if (tester is null)
        {
            ErrorText = Loc.T("连接测试服务未初始化");
            return;
        }

        ErrorText = string.Empty;
        IsTesting = true;
        TestStatus = Loc.T("正在连接…");
        TestSucceeded = false;

        try
        {
            var result = await tester.TestAsync(ToProfile());
            TestSucceeded = result.Success;
            TestStatus = result.Success
                ? $"{result.Message} · {result.Elapsed.TotalMilliseconds:N0} ms"
                : result.Message ?? Loc.T("连接失败");
        }
        catch (Exception ex)
        {
            TestSucceeded = false;
            TestStatus = Loc.T("测试失败：") + ex.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void ClearError() => ErrorText = string.Empty;

    partial void OnClearStoredPasswordChanged(bool value)
    {
        if (value) Password = string.Empty;
    }

    partial void OnClearStoredSshPasswordChanged(bool value)
    {
        if (value) SshPassword = string.Empty;
    }

    partial void OnUseSshTunnelChanged(bool value) => OnPropertyChanged(nameof(ShowSshFields));
    public bool ShowSshFields => UseSshTunnel;
}
