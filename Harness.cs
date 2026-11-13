using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace DshDesktop;

/// <summary>Start / stop / probe the local `dsh web` host the window displays.
/// The host is spawned as our child with stdout captured, so the per-start access token is read
/// straight from its own output, and stopping it never leaves an orphan behind.
/// An already-running host (started by a terminal, a service, another app) is adopted, not replaced.</summary>
internal static partial class Harness
{
    public const int Port = 3080;
    public const string PlainUrl = "http://127.0.0.1:3080/";

    private static readonly object Gate = new();
    private static readonly StringBuilder Captured = new();
    private static Process? _host;

    /// <summary>Set when this app spawned the host itself.</summary>
    public static bool StartedByUs { get; private set; }

    public static string DshHome => Path.Combine(AppPaths.Home, ".dsh");
    public static string LogDir => Path.Combine(DshHome, "logs");
    public static string OwnLog => Path.Combine(LogDir, "shell-web.log");

    public static bool PortOpen(int timeoutMs = 300)
    {
        try
        {
            using var c = new TcpClient();
            return c.ConnectAsync("127.0.0.1", Port).Wait(timeoutMs) && c.Connected;
        }
        catch { return false; }
    }

    public static string DescribeState()
    {
        if (!PortOpen()) return L10n.StateStopped;
        var pid = OwnerPid();
        return L10n.StateRunning(Port, pid);
    }

    // ---------------------------------------------------------------- locating `dsh`

    /// <summary>How to launch `dsh`: an explicit file plus leading args, or null when it is not installed.
    /// GUI apps on macOS / Linux do not inherit the login shell's PATH (nvm, volta, Homebrew...), so the
    /// usual install locations are probed explicitly, then the login shell is asked.</summary>
    private static (string File, string[] Prefix)? ResolveDsh()
    {
        var overridePath = Environment.GetEnvironmentVariable("DSH_BIN");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return Wrap(overridePath);

        if (OperatingSystem.IsWindows())
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "dsh.cmd"),
            };
            candidates.AddRange(SearchPath("dsh.cmd", "dsh.exe"));
            var hit = candidates.FirstOrDefault(File.Exists);
            return hit is null ? null : Wrap(hit);
        }

        var home = AppPaths.Home;
        var unix = new List<string>();
        unix.AddRange(SearchPath("dsh"));
        unix.AddRange(new[]
        {
            Path.Combine(home, ".npm-global", "bin", "dsh"),
            Path.Combine(home, ".local", "bin", "dsh"),
            Path.Combine(home, ".volta", "bin", "dsh"),
            Path.Combine(home, ".bun", "bin", "dsh"),
            "/opt/homebrew/bin/dsh",
            "/usr/local/bin/dsh",
            "/usr/bin/dsh",
        });
        try
        {
            var nvm = Path.Combine(home, ".nvm", "versions", "node");
            if (Directory.Exists(nvm))
                unix.AddRange(Directory.GetDirectories(nvm)
                    .OrderByDescending(d => d, StringComparer.Ordinal)
                    .Select(d => Path.Combine(d, "bin", "dsh")));
        }
        catch { }

        var found = unix.FirstOrDefault(File.Exists) ?? FromLoginShell();
        return found is null ? null : (found, Array.Empty<string>());
    }

    /// <summary>Ask the user's login shell where `dsh` is (picks up nvm / fnm / asdf / volta init scripts).</summary>
    private static string? FromLoginShell()
    {
        try
        {
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrEmpty(shell) || !File.Exists(shell)) shell = "/bin/sh";
            var psi = new ProcessStartInfo(shell)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-lc");
            psi.ArgumentList.Add("command -v dsh");
            using var p = Process.Start(psi);
            if (p is null) return null;
            var read = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return null; }
            var path = read.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            return path is not null && path.StartsWith('/') && File.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    /// <summary>Whether a `dsh` launcher exists on this machine (it does not have to be running).</summary>
    public static bool IsInstalled() => ResolveDsh() is not null;

    /// <summary>.cmd shims need cmd.exe; real executables run directly.</summary>
    private static (string, string[]) Wrap(string path) =>
        path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            ? ("cmd.exe", new[] { "/c", path })
            : (path, Array.Empty<string>());

    private static IEnumerable<string> SearchPath(params string[] names)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var n in names)
                yield return Path.Combine(dir.Trim('"'), n);
    }

    // ---------------------------------------------------------------- token / URL

    /// <summary>Token our own child printed, or null when it has not shown up yet.</summary>
    public static string? OwnToken()
    {
        lock (Gate)
        {
            var matches = TokenPattern.Matches(Captured.ToString());
            return matches.Count > 0 ? matches[^1].Value : null;
        }
    }

    private static string? WaitOwnToken(int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var token = OwnToken();
            if (token is not null) return token;
            Thread.Sleep(150);
        }
        return OwnToken();
    }

    private static bool OwnChildAlive()
    {
        try { return _host is { HasExited: false }; } catch { return false; }
    }

    /// <summary>Current tokenised URL. When we own the child, only ITS token counts: historical logs
    /// would hand back a token from an earlier run, which the host answers with 401/404.</summary>
    public static string ResolveUrl(int waitForOwnMs = 0)
    {
        var own = waitForOwnMs > 0 ? WaitOwnToken(waitForOwnMs) : OwnToken();
        if (own is not null) return own;

        if (OwnChildAlive()) return PlainUrl;

        try
        {
            var authFile = Path.Combine(DshHome, ".web-auth-url");
            if (File.Exists(authFile))
            {
                var first = File.ReadLines(authFile).FirstOrDefault();
                if (first is not null && TokenPattern.IsMatch(first)) return TokenPattern.Match(first).Value;
            }
        }
        catch { }

        try
        {
            if (Directory.Exists(LogDir))
            {
                foreach (var f in Directory.GetFiles(LogDir, "*.log").OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    string text;
                    using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                        text = sr.ReadToEnd();
                    var matches = TokenPattern.Matches(text);
                    if (matches.Count > 0) return matches[^1].Value;
                }
            }
        }
        catch { }
        return PlainUrl;
    }

    private static void Pump(string? line)
    {
        if (line is null) return;
        lock (Gate) Captured.AppendLine(line);
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(OwnLog, line + Environment.NewLine);
        }
        catch { }
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Spawn `dsh web --no-open` as a hidden child and wait for the port.</summary>
    public static bool Start(int waitSeconds = 40)
    {
        if (PortOpen()) return true;

        var dsh = ResolveDsh();
        if (dsh is null)
        {
            Log.Write("harness: dsh not found (set DSH_BIN to override)");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(dsh.Value.File)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppPaths.Home,
            };
            foreach (var a in dsh.Value.Prefix) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("web");
            psi.ArgumentList.Add("--no-open");
            if (!OperatingSystem.IsWindows())
            {
                // GUI apps on macOS start with PATH=/usr/bin:/bin:/usr/sbin:/sbin, so the npm shim's
                // `#!/usr/bin/env node` fails. Node normally sits next to the dsh shim (Homebrew, nvm).
                var extra = new[] { Path.GetDirectoryName(dsh.Value.File), "/opt/homebrew/bin", "/usr/local/bin" };
                var current = psi.Environment.TryGetValue("PATH", out var pv) ? pv ?? "" : "";
                psi.Environment["PATH"] = string.Join(Path.PathSeparator, extra
                    .Concat(current.Split(Path.PathSeparator))
                    .Where(d => !string.IsNullOrEmpty(d)).Distinct());
            }

            var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => Pump(e.Data);
            p.ErrorDataReceived += (_, e) => Pump(e.Data);
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            _host = p;
            StartedByUs = true;
            Log.Write($"harness: spawned pid {p.Id} via {dsh.Value.File} {string.Join(' ', psi.ArgumentList)}");
        }
        catch (Exception ex)
        {
            Log.Write("harness: spawn failed " + ex.Message);
            return false;
        }

        for (var i = 0; i < waitSeconds * 4; i++)
        {
            Thread.Sleep(250);
            if (PortOpen()) return true;
            if (!OwnChildAlive())
            {
                Log.Write("harness: child exited early; last output:\n" + Tail());
                return false;
            }
        }
        Log.Write("harness: port never opened; last output:\n" + Tail());
        return false;
    }

    private static string Tail(int lines = 8)
    {
        lock (Gate)
        {
            var all = Captured.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("\n", all.TakeLast(lines));
        }
    }

    public static bool Stop()
    {
        // Prefer the child we own; fall back to whoever listens on the port.
        int? target = null;
        try { if (_host is { HasExited: false }) target = _host.Id; } catch { }
        target ??= OwnerPid();
        if (target is null) return true;

        try
        {
            using var p = Process.GetProcessById(target.Value);
            p.Kill(entireProcessTree: true);
            p.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Log.Write("harness: stop failed " + ex.Message);
            return false;
        }

        for (var i = 0; i < 25 && PortOpen(); i++) Thread.Sleep(200);
        StartedByUs = false;
        _host = null;
        return !PortOpen();
    }

    public static bool Restart(int waitSeconds = 40)
    {
        if (PortOpen() && !Stop()) return false;
        lock (Gate) Captured.Clear();
        return Start(waitSeconds);
    }

    [GeneratedRegex(@"http://127\.0\.0\.1:3080/\?token=[A-Za-z0-9_\-]+")]
    private static partial Regex TokenRegex();

    private static Regex TokenPattern => TokenRegex();

    [GeneratedRegex(@"pid=(\d+)")]
    private static partial Regex SsPidRegex();

    // ---------------------------------------------------------------- port owner

    /// <summary>PID listening on the harness port (null when nothing listens or it cannot be told).</summary>
    public static int? OwnerPid()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var text = Run("netstat.exe", "-ano", "-p", "tcp");
                foreach (var line in text.Split('\n'))
                {
                    if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!line.Contains($":{Port} ", StringComparison.Ordinal)) continue;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 && int.TryParse(parts[^1], out var pid)) return pid;
                }
                return null;
            }

            // macOS ships lsof; most Linux desktops do too. `ss` covers the rest.
            var lsof = new[] { "/usr/sbin/lsof", "/usr/bin/lsof", "/bin/lsof" }.FirstOrDefault(File.Exists);
            if (lsof is not null)
            {
                var first = Run(lsof, "-nP", $"-iTCP:{Port}", "-sTCP:LISTEN", "-t")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (int.TryParse(first?.Trim(), out var pid)) return pid;
            }
            var ss = new[] { "/usr/bin/ss", "/usr/sbin/ss", "/bin/ss" }.FirstOrDefault(File.Exists);
            if (ss is not null)
            {
                var m = SsPidRegex().Match(Run(ss, "-ltnpH", $"sport = :{Port}"));
                if (m.Success) return int.Parse(m.Groups[1].Value);
            }
        }
        catch { }
        return null;
    }

    private static string Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) return "";
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit(3000);
        return text;
    }
}
