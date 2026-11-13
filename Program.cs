using Avalonia;
using Avalonia.Controls;

namespace DshDesktop;

internal static class Program
{
    /// <summary>True when launched with --tray (autostart): boot to the tray without showing the window.</summary>
    public static bool StartHidden { get; private set; }

    public static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        Instance = SingleInstance.TryAcquire();
        if (Instance is null) return 0; // the running instance was asked to show itself

        StartHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        Log.Write($"start: {Environment.OSVersion} tray={StartHidden}");

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Log.Write("fatal: " + ex);
            throw;
        }
        finally
        {
            Instance.Dispose();
        }
    }

    // Used by the XAML previewer too.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
