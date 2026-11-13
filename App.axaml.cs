using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace DshDesktop;

/// <summary>Owns the tray icon, its menu, the window lifetime and the DSH service actions.
/// The app lives in the tray: closing the window only hides it; "退出" is the only real exit.</summary>
public sealed class App : Application
{
    private MainWindow? _window;
    private TrayIcon? _tray;
    private NativeMenuItem? _status;
    private NativeMenuItem? _autostart;
    private NativeMenuItem? _restart;
    private NativeMenuItem? _stop;
    private DispatcherTimer? _refresh;
    private bool _busy;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _window = new MainWindow();
            BuildTray();

            Program.Instance?.Listen(() => Dispatcher.UIThread.Post(ShowWindow));

            if (!Program.StartHidden) _window.Show();
            // Start the service even when booting hidden, so the page is ready the first time it opens.
            _window.EnsureLoaded();

            desktop.ShutdownRequested += (_, _) => _window?.SaveBounds();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void BuildTray()
    {
        var open = new NativeMenuItem("打开窗口");
        open.Click += (_, _) => ShowWindow();

        _status = new NativeMenuItem("DSH 服务：…") { IsEnabled = false };

        _restart = new NativeMenuItem("重启 DSH 服务");
        _restart.Click += (_, _) => Service(restart: true);

        _stop = new NativeMenuItem("停止 DSH 服务");
        _stop.Click += (_, _) => Service(restart: false);

        _autostart = new NativeMenuItem("开机自启")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = Autostart.IsEnabled(),
        };
        _autostart.Click += (_, _) =>
        {
            Autostart.Set(!Autostart.IsEnabled());
            RefreshStatus();
        };

        var devtools = new NativeMenuItem("重新加载页面");
        devtools.Click += (_, _) => _window?.Reload();

        var openLog = new NativeMenuItem("打开日志目录");
        openLog.Click += (_, _) => _window?.OpenFolder(AppPaths.DataDir);

        var exit = new NativeMenuItem("退出（保留 DSH 服务）");
        exit.Click += (_, _) => Exit();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_status);
        menu.Items.Add(_restart);
        menu.Items.Add(_stop);
        menu.Items.Add(devtools);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_autostart);
        menu.Items.Add(openLog);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);
        menu.Opening += (_, _) => RefreshStatus();

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DshDesktop/Assets/app.ico"))),
            ToolTipText = AppPaths.AppTitle,
            Menu = menu,
            IsVisible = true,
        };
        // Left click (Windows / most Linux trays) brings the window back; macOS always opens the menu.
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });

        // NativeMenu.Opening is not raised by every tray host (notably some Linux SNI hosts),
        // so keep the status line fresh on a slow timer as well.
        _refresh = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => RefreshStatus());
        _refresh.Start();
        RefreshStatus();
    }

    private async void RefreshStatus()
    {
        if (_status is null || _autostart is null) return;
        if (_busy) return;
        var state = await Task.Run(Harness.DescribeState);
        _status.Header = "DSH 服务：" + state;
        _autostart.IsChecked = Autostart.IsEnabled();
    }

    private void ShowWindow() => _window?.ShowFromTray();

    private async void Service(bool restart)
    {
        if (_busy || _window is null || _tray is null) return;
        _busy = true;
        _restart!.IsEnabled = _stop!.IsEnabled = false;
        var verb = restart ? "正在重启 DSH 服务…" : "正在停止 DSH 服务…";
        _tray.ToolTipText = verb;
        _status!.Header = "DSH 服务：" + verb.TrimEnd('…');

        if (restart)
        {
            _window.ShowSplash();
            _window.SplashStatus(verb);
        }

        var ok = await Task.Run(() => restart ? Harness.Restart() : Harness.Stop());

        _busy = false;
        _restart.IsEnabled = _stop.IsEnabled = true;
        _tray.ToolTipText = AppPaths.AppTitle;
        RefreshStatus();

        if (!ok)
        {
            Log.Write($"service: {(restart ? "restart" : "stop")} failed");
            _window.ShowSplash();
            _window.SplashStatus(restart
                ? "重启失败：无法结束或启动 DSH 进程。若它由管理员 / root 终端启动，请在那个终端里结束它后点「重试」"
                : "停止失败：当前 DSH 进程可能由管理员 / root 启动，普通权限无法结束它", failed: true);
            ShowWindow();
            return;
        }

        if (restart)
        {
            _window.SplashStatus("正在连接…");
            _window.Reload(waitForTokenMs: 20000);
        }
        else
        {
            _window.ShowSplash();
            _window.SplashStatus("DSH 服务已停止。点「重试」或托盘里的「重启 DSH 服务」再次启动", failed: true);
        }
    }

    private void Exit()
    {
        // The service is left running on purpose: it may be serving other sessions / remote clients.
        _refresh?.Stop();
        _window?.CloseForReal();
        if (_tray is not null) _tray.IsVisible = false;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }
}
