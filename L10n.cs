using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DshDesktop;

/// <summary>UI strings: Chinese when the OS UI language is Chinese, English otherwise.
/// The app runs with InvariantGlobalization (Native AOT, no ICU), so CultureInfo always reports the
/// invariant culture; the language is read from the OS directly instead.</summary>
internal static partial class L10n
{
    public static bool IsChinese { get; } = DetectChinese();

    private static string T(string zh, string en) => IsChinese ? zh : en;

    // ---- tray
    public static string TrayOpen => T("打开窗口", "Open Window");
    public static string TrayRestart => T("重启 DSH 服务", "Restart DSH Service");
    public static string TrayStop => T("停止 DSH 服务", "Stop DSH Service");
    public static string TrayReload => T("重新加载页面", "Reload Page");
    public static string TrayAutostart => T("开机自启", "Start at Login");
    public static string TrayOpenLogs => T("打开日志目录", "Open Log Folder");
    public static string TrayQuit => T("退出（保留 DSH 服务）", "Quit (Keep DSH Running)");
    public static string Status(string state) => T("DSH 服务：", "DSH service: ") + state;
    public static string StatusUnknown => "…";
    public static string StateStopped => T("已停止", "stopped");
    public static string StateRunning(int port, int? pid) => pid is null
        ? T($"运行中 (:{port})", $"running (:{port})")
        : T($"运行中 (:{port}, pid {pid})", $"running (:{port}, pid {pid})");
    public static string Restarting => T("正在重启 DSH 服务…", "Restarting the DSH service…");
    public static string Stopping => T("正在停止 DSH 服务…", "Stopping the DSH service…");
    public static string StateRestarting => T("正在重启", "restarting");
    public static string StateStopping => T("正在停止", "stopping");

    // ---- macOS application menu
    public static string AppMenuQuit => T("退出 DeepSeek Harness Desktop", "Quit DeepSeek Harness Desktop");

    // ---- splash
    public static string Starting => T("正在启动 DSH 服务…", "Starting the DSH service…");
    public static string Restarting2 => T("正在重新启动 DSH 服务…", "Starting the DSH service again…");
    public static string Connecting => T("正在连接…", "Connecting…");
    public static string Retry => T("重试", "Retry");
    public static string StartFailed => T(
        "DSH 服务启动失败，点「重试」再试一次（详情见托盘 › 打开日志目录）",
        "The DSH service failed to start. Click Retry (details: tray › Open Log Folder).");
    public static string NotInstalledSplash => T("未检测到 DSH 服务。安装后点「重试」", "DSH is not installed. Install it, then click Retry.");
    public static string RestartFailed => T(
        "重启失败：无法结束或启动 DSH 进程。若它由管理员 / root 终端启动，请在那个终端里结束它后点「重试」",
        "Restart failed: the DSH process could not be stopped or started. If it was started from an administrator / root terminal, stop it there, then click Retry.");
    public static string StopFailed => T(
        "停止失败：当前 DSH 进程可能由管理员 / root 启动，普通权限无法结束它",
        "Stop failed: the DSH process was probably started as administrator / root and cannot be stopped from here.");
    public static string StoppedHint => T(
        "DSH 服务已停止。点「重试」或托盘里的「重启 DSH 服务」再次启动",
        "The DSH service is stopped. Click Retry, or Restart DSH Service in the tray, to start it again.");

    // ---- install dialog
    public static string InstallTitle => T("未检测到 DeepSeek Harness", "DeepSeek Harness not found");
    public static string InstallHeading => T("这台设备上还没有安装 DSH 服务", "DSH is not installed on this device");
    public static string InstallBody(bool windows) => T(
        $"DeepSeek Harness Desktop 显示的是官方 DSH 的网页界面，需要先安装 DSH。请打开{(windows ? "PowerShell 或命令提示符" : "终端")}，运行：",
        $"DeepSeek Harness Desktop shows the official DSH web UI, so DSH has to be installed first. Open {(windows ? "PowerShell or Command Prompt" : "a terminal")} and run:");
    public static string InstallNote => T(
        "需要 Node.js 24.9 或更高版本（自带 npm）。装好后点「已安装，重试」，本应用会自动启动 DSH 服务。",
        "Requires Node.js 24.9 or later (includes npm). Once installed, click \"Installed, Retry\" and the app starts the DSH service for you.");
    public static string Copy => T("复制", "Copy");
    public static string Copied => T("已复制 ✓", "Copied ✓");
    public static string CopyFailed => T("复制失败", "Copy failed");
    public static string DownloadNode => T("下载 Node.js", "Get Node.js");
    public static string InstalledRetry => T("已安装，重试", "Installed, Retry");
    public static string Close => T("关闭", "Close");

    // ---------------------------------------------------------------- detection

    private static bool DetectChinese()
    {
        try
        {
            var forced = Environment.GetEnvironmentVariable("DSH_DESKTOP_LANG");
            if (!string.IsNullOrEmpty(forced)) return IsZh(forced);

            if (OperatingSystem.IsWindows())
            {
                const int LANG_CHINESE = 0x04;
                return (GetUserDefaultUILanguage() & 0x3FF) == LANG_CHINESE;
            }

            if (OperatingSystem.IsMacOS())
            {
                // First entry of the user's preferred languages, e.g. "zh-Hans-CN" or "en-US".
                var psi = new ProcessStartInfo("/usr/bin/defaults")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("read");
                psi.ArgumentList.Add("-g");
                psi.ArgumentList.Add("AppleLanguages");
                using var p = Process.Start(psi);
                var text = p?.StandardOutput.ReadToEnd() ?? "";
                p?.WaitForExit(2000);
                var first = text.Split('\n').Select(l => l.Trim().Trim(',', '"', ' '))
                    .FirstOrDefault(l => l.Length > 0 && l != "(" && l != ")");
                return first is not null && IsZh(first);
            }

            // Linux / BSD: gettext precedence.
            foreach (var name in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
            {
                var v = Environment.GetEnvironmentVariable(name);
                if (string.IsNullOrEmpty(v)) continue;
                return IsZh(v.Split(':')[0]);
            }
        }
        catch { }
        return false;
    }

    private static bool IsZh(string tag) => tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();
}
