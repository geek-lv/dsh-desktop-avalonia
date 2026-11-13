using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace DshDesktop;

/// <summary>The window: the native title bar above a NativeWebView
/// (WebView2 / WKWebView / WebKitGTK) that shows the splash screen and then the local DSH web UI.
/// Closing hides to the tray.</summary>
public sealed partial class MainWindow : Window
{
    private readonly NativeWebView _web;
    private bool _reallyClosing;
    private bool _retriedNavigation;
    private bool _loadStarted;
    private bool _onSplash;
    private bool _pendingSplash;
    private string _splashText = L10n.Starting;
    private bool _splashFailed;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _web = this.FindControl<NativeWebView>("Web")!;
        RestoreBounds();

        _web.Background = new SolidColorBrush(Color.Parse("#151517"));
        _web.EnvironmentRequested += OnEnvironmentRequested;
        _web.AdapterCreated += (_, _) =>
        {
            Log.Write("webview: adapter created " + _web.AdapterInfo);
            if (_pendingSplash) ShowSplash();
        };
        _web.NavigationStarted += (_, e) => Log.Write("navigate -> " + Redact(e.Request));
        _web.NavigationCompleted += OnNavigationCompleted;
        _web.WebMessageReceived += (_, e) => OnWebMessage(e.Body);
        _web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is not null) OpenExternal(e.Request.AbsoluteUri);
        };
        _web.ProcessTerminated += (_, _) =>
        {
            Log.Write("webview: browser process terminated, reloading");
            Dispatcher.UIThread.Post(() => Reload());
        };

        Closing += OnClosing;
    }

    // ------------------------------------------------------------------ theme

    /// <summary>Follow the DSH page's own light / dark theme so the title bar never clashes with it.</summary>
    private void ApplyTheme(bool dark)
    {
        var bg = new SolidColorBrush(dark ? Color.Parse("#151517") : Color.Parse("#FBFBFC"));
        Background = bg;
        _web.Background = bg;
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; // native title bar follows
    }

    // ------------------------------------------------------------------ webview

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = true;
        switch (e)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs w:
                w.UserDataFolder = AppPaths.WebViewDir;
                break;
            case GtkWebViewEnvironmentRequestedEventArgs g:
                g.BaseDataDirectory = Path.Combine(AppPaths.WebViewDir, "data");
                g.BaseCacheDirectory = Path.Combine(AppPaths.WebViewDir, "cache");
                break;
        }
    }

    /// <summary>Kick off the service and the first navigation (also when booting hidden to the tray).</summary>
    public void EnsureLoaded()
    {
        if (_loadStarted) return;
        _loadStarted = true;
        ShowSplash();
        _ = LoadHarnessAsync();
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        Log.Write(e.IsSuccess ? "navigate ok" : "navigate FAILED " + Redact(e.Request));

        if (_onSplash)
        {
            ApplySplashStatus();
            return;
        }

        if (e.IsSuccess)
        {
            _ = _web.InvokeScript(ThemeScript);
            return;
        }

        if (!_retriedNavigation && Harness.PortOpen())
        {
            _retriedNavigation = true;
            Log.Write("navigate failed -> retrying once with a fresh token");
            DispatcherTimer.RunOnce(() =>
            {
                _retriedNavigation = false;
                Reload(waitForTokenMs: 5000);
            }, TimeSpan.FromMilliseconds(1500));
        }
    }

    private void OnWebMessage(string? body)
    {
        switch (body)
        {
            case "retry":
                _ = LoadHarnessAsync(fromRetry: true);
                break;
            case "theme:dark":
                ApplyTheme(dark: true);
                break;
            case "theme:light":
                ApplyTheme(dark: false);
                break;
        }
    }

    /// <summary>Make sure the service is up, then navigate to it (or leave the splash in its retry state).</summary>
    private async Task LoadHarnessAsync(bool fromRetry = false)
    {
        if (fromRetry) ShowSplash();
        SplashStatus(fromRetry ? L10n.Restarting2 : L10n.Starting);

        // Nothing listening and no `dsh` on this machine: explain how to install it instead of failing.
        if (!await Task.Run(() => Harness.PortOpen() || Harness.IsInstalled()))
        {
            Log.Write("harness: dsh is not installed");
            SplashStatus(L10n.NotInstalledSplash, failed: true);
            await ShowInstallDialogAsync();
            return;
        }

        var sw = Stopwatch.StartNew();
        var ready = await Task.Run(() => Harness.PortOpen() || Harness.Start());
        Log.Write($"harness: check/start took {sw.ElapsedMilliseconds}ms ready={ready}");

        if (ready)
        {
            SplashStatus(L10n.Connecting);
            // Only a child WE spawned needs waiting for: it prints its tokenised URL a moment after it
            // starts listening. An adopted instance already logged its token.
            Reload(waitForTokenMs: Harness.StartedByUs ? 20000 : 0);
            return;
        }
        SplashStatus(L10n.StartFailed, failed: true);
    }

    private bool _installDialogOpen;

    /// <summary>Modal "DSH is not installed" dialog with a copyable npm command; "重试" re-runs the start.</summary>
    private async Task ShowInstallDialogAsync()
    {
        if (_installDialogOpen) return;
        _installDialogOpen = true;
        try
        {
            if (!IsVisible) ShowFromTray(); // booted to the tray (autostart): the user still has to see this
            var retry = await new InstallDshDialog().ShowDialog<bool>(this);
            if (retry) _ = LoadHarnessAsync(fromRetry: true);
        }
        finally
        {
            _installDialogOpen = false;
        }
    }

    /// <summary>Re-resolve the tokenised URL and navigate.</summary>
    public async void Reload(int waitForTokenMs = 0)
    {
        try
        {
            var url = await Task.Run(() => Harness.ResolveUrl(waitForTokenMs));
            Log.Write("reload -> " + Redact(new Uri(url)));
            _onSplash = false;
            _web.Navigate(new Uri(url));
        }
        catch (Exception ex)
        {
            Log.Write("reload failed: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ splash

    /// <summary>Boot screen (also shown while a tray restart runs). Rendered from a string with the
    /// artwork inlined as data URIs: the cross-platform WebView has no virtual-host folder mapping.</summary>
    public void ShowSplash()
    {
        _onSplash = true;
        try
        {
            // WKWebView treats a load issued while the splash is showing as initiated by the splash
            // document (default base http://localhost:12345/), i.e. cross-site to 127.0.0.1:3080, so the
            // SameSite=Strict auth cookie set by ?token= is withheld on the 303 to "/" (page says
            // "authentication required"). Giving the splash the harness origin keeps the hop same-site.
            _web.NavigateToString(SplashHtml.Value, OperatingSystem.IsMacOS() ? new Uri(Harness.PlainUrl) : null!);
            _pendingSplash = false;
        }
        catch (Exception ex)
        {
            // The native adapter is created when the control is first attached (the window shown);
            // before that, remember the request and replay it from AdapterCreated.
            _pendingSplash = true;
            Log.Write("splash deferred: " + ex.Message);
        }
    }

    /// <summary>Update the splash line; failed=true swaps the progress bar for a retry button.</summary>
    public void SplashStatus(string text, bool failed = false)
    {
        _splashText = text;
        _splashFailed = failed;
        if (_onSplash) ApplySplashStatus();
    }

    private void ApplySplashStatus()
    {
        var js = $"window.__dshSplash && window.__dshSplash({JsonSerializer.Serialize(_splashText, AppJson.Default.String)}, {(_splashFailed ? "true" : "false")});";
        try { _ = _web.InvokeScript(js); } catch { }
    }

    private static readonly Lazy<string> SplashHtml = new(() =>
    {
        var html = ReadResource("splash.html")
            .Replace("{{LANG}}", L10n.IsChinese ? "zh-CN" : "en")
            .Replace("{{STARTING}}", L10n.Starting)
            .Replace("{{RESTARTING}}", L10n.Restarting2)
            .Replace("{{RETRY}}", L10n.Retry);
        return html
            .Replace("'splash-whale-idle.webp'", $"'{DataUri("splash-whale-idle.webp")}'")
            .Replace("'splash-whale-sorry.webp'", $"'{DataUri("splash-whale-sorry.webp")}'")
            .Replace("src=\"splash-whale-idle.webp\"", $"src=\"{DataUri("splash-whale-idle.webp")}\"");
    });

    private static string ReadResource(string name)
    {
        using var s = typeof(MainWindow).Assembly.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private static string DataUri(string name)
    {
        using var s = typeof(MainWindow).Assembly.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return "data:image/webp;base64," + Convert.ToBase64String(ms.ToArray());
    }

    // ------------------------------------------------------------------ tray / lifetime

    public void ShowFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (_pendingSplash) ShowSplash();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_reallyClosing && e.CloseReason is not (WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown))
        {
            e.Cancel = true;
            SaveBounds();
            Hide();
            return;
        }
        SaveBounds();
    }

    public void CloseForReal()
    {
        _reallyClosing = true;
        Close();
    }

    public void OpenFolder(string path) => OpenExternal(path);

    internal static void OpenExternal(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            else
                Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", new[] { target });
        }
        catch (Exception ex) { Log.Write("open failed: " + ex.Message); }
    }

    // ------------------------------------------------------------------ bounds

    public void SaveBounds()
    {
        try
        {
            if (WindowState == WindowState.Minimized) return;
            var maximized = WindowState == WindowState.Maximized;
            // While maximized keep the last normal geometry so restoring lands where it was.
            var b = maximized && _lastNormal is not null
                ? _lastNormal with { Maximized = true }
                : new SavedBounds(Position.X, Position.Y, Width, Height, maximized);
            File.WriteAllText(AppPaths.WindowStateFile, JsonSerializer.Serialize(b, AppJson.Default.SavedBounds));
        }
        catch { }
    }

    private SavedBounds? _lastNormal;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (WindowState == WindowState.Normal &&
            (change.Property == WidthProperty || change.Property == HeightProperty || change.Property == WindowStateProperty))
            _lastNormal = new SavedBounds(Position.X, Position.Y, Width, Height, false);
    }

    private void RestoreBounds()
    {
        try
        {
            if (!File.Exists(AppPaths.WindowStateFile)) return;
            var b = JsonSerializer.Deserialize(File.ReadAllText(AppPaths.WindowStateFile), AppJson.Default.SavedBounds);
            if (b is null || b.Width < MinWidth || b.Height < MinHeight) return;

            Width = b.Width;
            Height = b.Height;
            var pos = new PixelPoint(b.X, b.Y);
            // Only reuse the position when it is still on some screen (monitors come and go).
            Opened += (_, _) =>
            {
                if (Screens.All.Any(s => s.WorkingArea.Contains(pos + new PixelPoint(40, 20))))
                    Position = pos;
                if (b.Maximized) WindowState = WindowState.Maximized;
            };
            _lastNormal = b with { Maximized = false };
        }
        catch { }
    }

    private static string Redact(Uri? uri)
    {
        if (uri is null) return "(null)";
        if (uri.Scheme == "data") return "data:…";
        var s = uri.ToString();
        var i = s.IndexOf("token=", StringComparison.Ordinal);
        return i < 0 ? s : s[..(i + 6)] + "…";
    }

    /// <summary>Report the DSH page's theme (body[data-ds-dark-theme]) now and whenever it flips.</summary>
    private const string ThemeScript = """
        (() => {
          if (window.__dshDesktopTheme) { window.__dshDesktopTheme(); return; }
          const post = (m) => {
            try { if (window.invokeCSharpAction) return window.invokeCSharpAction(m); } catch (e) {}
            try { window.chrome.webview.postMessage(m); } catch (e) {}
          };
          let last = null;
          const report = () => {
            const b = document.body;
            if (!b) return;
            const dark = b.hasAttribute('data-ds-dark-theme')
              || document.documentElement.classList.contains('dark');
            const v = dark ? 'theme:dark' : 'theme:light';
            if (v !== last) { last = v; post(v); }
          };
          window.__dshDesktopTheme = report;
          new MutationObserver(report).observe(document.documentElement, { attributes: true, subtree: true, attributeFilter: ['data-ds-dark-theme', 'class'] });
          report();
        })();
        """;
}
