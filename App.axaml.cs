using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace DshDesktop;

/// <summary>Owns the tray icon, its menu, the window lifetime and the DSH service actions.
/// The app lives in the tray: closing the window only hides it; Quit is the only real exit.</summary>
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
            if (OperatingSystem.IsMacOS()) BuildMacAppMenu();

            Program.Instance?.Listen(() => Dispatcher.UIThread.Post(ShowWindow));
            // macOS: a Dock-icon click / Finder double-click on the already running app arrives as a
            // "reopen" Apple Event via LaunchServices, not as a second process, so the pipe never sees it.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                activatable.Activated += (_, e) =>
                {
                    if (e.Kind == ActivationKind.Reopen) ShowWindow();
                };

            if (!Program.StartHidden) _window.Show();
            // Start the service even when booting hidden, so the page is ready the first time it opens.
            _window.EnsureLoaded();

            desktop.ShutdownRequested += (_, _) => _window?.SaveBounds();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void BuildTray()
    {
        var open = new NativeMenuItem(L10n.TrayOpen);
        open.Click += (_, _) => ShowWindow();

        _status = new NativeMenuItem(L10n.Status(L10n.StatusUnknown)) { IsEnabled = false };

        _restart = new NativeMenuItem(L10n.TrayRestart);
        _restart.Click += (_, _) => Service(restart: true);

        _stop = new NativeMenuItem(L10n.TrayStop);
        _stop.Click += (_, _) => Service(restart: false);

        _autostart = new NativeMenuItem(L10n.TrayAutostart)
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = Autostart.IsEnabled(),
        };
        _autostart.Click += (_, _) =>
        {
            Autostart.Set(!Autostart.IsEnabled());
            RefreshStatus();
        };

        var devtools = new NativeMenuItem(L10n.TrayReload);
        devtools.Click += (_, _) => _window?.Reload();

        var openLog = new NativeMenuItem(L10n.TrayOpenLogs);
        openLog.Click += (_, _) => _window?.OpenFolder(AppPaths.DataDir);

        var exit = new NativeMenuItem(L10n.TrayQuit);
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

        // Windows / Linux: the blue whale (reads on light and dark trays). macOS: a template image the
        // system tints to match the menu bar, like every other menu-bar extra.
        var iconUri = OperatingSystem.IsWindows() ? "avares://DshDesktop/Assets/app.ico"
            : OperatingSystem.IsMacOS() ? "avares://DshDesktop/Assets/tray-template.png"
            : "avares://DshDesktop/Assets/tray.png";
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri(iconUri))),
            ToolTipText = AppPaths.AppTitle,
            Menu = menu,
            IsVisible = true,
        };
        if (OperatingSystem.IsMacOS()) MacOSProperties.SetIsTemplateIcon(_tray, true);
        // Left click (Windows / most Linux trays) brings the window back; macOS always opens the menu.
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });

        // NativeMenu.Opening is not raised by every tray host (notably some Linux SNI hosts),
        // so keep the status line fresh on a slow timer as well.
        _refresh = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => RefreshStatus());
        _refresh.Start();
        RefreshStatus();
    }

    /// <summary>The app-name menu in the macOS menu bar (Avalonia's defaults are disabled in Program).</summary>
    private void BuildMacAppMenu()
    {
        var open = new NativeMenuItem(L10n.TrayOpen) { Gesture = KeyGesture.Parse("Cmd+N") };
        open.Click += (_, _) => ShowWindow();

        var reload = new NativeMenuItem(L10n.TrayReload) { Gesture = KeyGesture.Parse("Cmd+R") };
        reload.Click += (_, _) => _window?.Reload();

        var quit = new NativeMenuItem(L10n.AppMenuQuit) { Gesture = KeyGesture.Parse("Cmd+Q") };
        quit.Click += (_, _) => Exit();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(reload);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);
        NativeMenu.SetMenu(this, menu);
    }

    private async void RefreshStatus()
    {
        if (_status is null || _autostart is null) return;
        if (_busy) return;
        var state = await Task.Run(Harness.DescribeState);
        _status.Header = L10n.Status(state);
        _autostart.IsChecked = Autostart.IsEnabled();
    }

    private void ShowWindow() => _window?.ShowFromTray();

    private async void Service(bool restart)
    {
        if (_busy || _window is null || _tray is null) return;
        _busy = true;
        _restart!.IsEnabled = _stop!.IsEnabled = false;
        var verb = restart ? L10n.Restarting : L10n.Stopping;
        _tray.ToolTipText = verb;
        _status!.Header = L10n.Status(restart ? L10n.StateRestarting : L10n.StateStopping);

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
                ? L10n.RestartFailed
                : L10n.StopFailed, failed: true);
            ShowWindow();
            return;
        }

        if (restart)
        {
            _window.SplashStatus(L10n.Connecting);
            _window.Reload(waitForTokenMs: 20000);
        }
        else
        {
            _window.ShowSplash();
            _window.SplashStatus(L10n.StoppedHint, failed: true);
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
