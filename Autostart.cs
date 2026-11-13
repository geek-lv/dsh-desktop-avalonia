using System.Runtime.Versioning;
using System.Security;

namespace DshDesktop;

/// <summary>Start-at-login, per OS:
///   Windows  HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DSHDesktop
///   macOS    ~/Library/LaunchAgents/io.github.geeklv.dsh-desktop.plist (RunAtLoad)
///   Linux    ~/.config/autostart/dsh-desktop-avalonia.desktop (XDG autostart)
/// Every entry launches this executable with --tray so it boots straight to the tray.</summary>
internal static class Autostart
{
    private const string WinValueName = "DSHDesktop";
    private const string MacLabel = "io.github.geeklv.dsh-desktop";

    private static string Exe => Environment.ProcessPath ?? throw new InvalidOperationException("no process path");

    private static string MacPlist => Path.Combine(AppPaths.Home, "Library", "LaunchAgents", MacLabel + ".plist");

    private static string LinuxDesktopFile
    {
        get
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(config)) config = Path.Combine(AppPaths.Home, ".config");
            return Path.Combine(config, "autostart", AppPaths.AppId + ".desktop");
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WinIsEnabled();
            if (OperatingSystem.IsMacOS()) return File.Exists(MacPlist);
            return File.Exists(LinuxDesktopFile);
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows()) WinSet(enabled);
            else if (OperatingSystem.IsMacOS()) WriteOrDelete(MacPlist, enabled ? MacPlistText() : null);
            else WriteOrDelete(LinuxDesktopFile, enabled ? LinuxDesktopText() : null);
            Log.Write($"autostart: {(enabled ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            Log.Write("autostart: failed " + ex.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool WinIsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue(WinValueName) is string s && s.Length > 0;
    }

    [SupportedOSPlatform("windows")]
    private static void WinSet(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue(WinValueName, $"\"{Exe}\" --tray");
        else if (key.GetValue(WinValueName) is not null) key.DeleteValue(WinValueName, false);
    }

    private static void WriteOrDelete(string path, string? content)
    {
        if (content is null)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string MacPlistText() => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key><string>{MacLabel}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{SecurityElement.Escape(Exe)}</string>
            <string>--tray</string>
          </array>
          <key>RunAtLoad</key><true/>
          <key>ProcessType</key><string>Interactive</string>
        </dict>
        </plist>
        """;

    /// <summary>Desktop Entry spec: quote the path and escape the characters it reserves.</summary>
    private static string LinuxDesktopText()
    {
        var exe = Exe.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");
        return $"""
            [Desktop Entry]
            Type=Application
            Name={AppPaths.AppTitle}
            Comment=Native desktop shell for the local DeepSeek Harness web UI
            Exec="{exe}" --tray
            Terminal=false
            X-GNOME-Autostart-enabled=true
            """ + "\n";
    }
}
