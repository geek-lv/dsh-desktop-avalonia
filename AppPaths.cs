namespace DshDesktop;

/// <summary>Per-user locations, the same shape on every OS:
/// Windows %LOCALAPPDATA%\dsh-desktop-avalonia, macOS ~/Library/Application Support/dsh-desktop-avalonia,
/// Linux ~/.local/share/dsh-desktop-avalonia.</summary>
internal static class AppPaths
{
    public const string AppId = "dsh-desktop-avalonia";
    public const string AppTitle = "DeepSeek Harness Desktop";

    public static string DataDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        AppId));

    /// <summary>Browser profile (WebView2 user data folder / WebKitGTK data directory).</summary>
    public static string WebViewDir { get; } = Ensure(Path.Combine(DataDir, "webview"));

    public static string WindowStateFile => Path.Combine(DataDir, "window.json");

    public static string LogFile => Path.Combine(DataDir, "shell.log");

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string Ensure(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }
}

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(AppPaths.LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
