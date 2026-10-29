using System.Windows;
using System.Windows.Threading;
using WinRedisClient.App.Themes;
using WinRedisClient.Core.Localization;
using WinRedisClient.Core.Models;
using WinRedisClient.Infrastructure;
using WinRedisClient.Presentation;
using WinRedisClient.Redis;
using WinRedisClient.Transport;

namespace WinRedisClient.App;

/// <summary>
/// 应用组合根：装配 Redis/存储服务，加载设置与主题，创建 Shell。
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            ConfigureServices();

            try
            {
                AppServices.Settings = await AppServices.SettingsStore!.LoadAsync();
            }
            catch
            {
                AppServices.Settings = AppSettings.Default;
            }

            ThemeManager.Apply(ThemeManager.FromPreference(AppServices.Settings.Theme));
            FontScaleManager.Apply(AppServices.Settings.FontScale);
            Localizer.Instance.SetLanguage(AppServices.Settings.Language);

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            await window.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("WinRedis 启动失败：\\n{0}", ex), "WinRedis",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void ConfigureServices()
    {
        var sshTunnel = new SshTunnelService();
        var sessionFactory = new RedisSessionFactory(sshTunnel);

        AppServices.Redis = new RedisService(sessionFactory);
        AppServices.RedisFactory = () => new RedisService(sessionFactory);
        AppServices.ConnectionTester = new RedisConnectionTester(sessionFactory);
        AppServices.ConnectionStore = new JsonConnectionStore();
        AppServices.SettingsStore = new JsonSettingsStore();
        AppServices.HistoryStore = new JsonHistoryStore();
        AppServices.DataDirectory = StoragePaths.RootDirectory;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(Loc.F("未处理异常：\\n{0}", e.Exception), "WinRedis",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show(Loc.F("严重错误：\\n{0}", ex), "WinRedis",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
