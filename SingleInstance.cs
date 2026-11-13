using System.IO.Pipes;

namespace DshDesktop;

/// <summary>One instance per user. The first instance listens on a named pipe (a Unix domain socket on
/// macOS / Linux); a second launch just pokes it with "show" and exits, which brings the window back.</summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string PipeName = AppPaths.AppId + "." + Environment.UserName;

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Null when another instance already owns the lock (it has been asked to show itself).</summary>
    public static SingleInstance? TryAcquire()
    {
        // On Unix .NET scopes Local\ mutexes to the POSIX session id, and every app launchd starts
        // (Finder/open, login items) gets its own session, so Local\ never sees the other instance.
        // Global\ lives under the per-user $TMPDIR on macOS and the name carries the user name anyway.
        var scope = OperatingSystem.IsWindows() ? @"Local\" : @"Global\";
        var mutex = new Mutex(true, scope + PipeName, out var isFirst);
        if (isFirst) return new SingleInstance(mutex);

        mutex.Dispose();
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var w = new StreamWriter(client);
            w.WriteLine("show");
        }
        catch { }
        return null;
    }

    /// <summary>Invoke <paramref name="onShow"/> (on a worker thread) whenever another launch asks.</summary>
    public void Listen(Action onShow)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var r = new StreamReader(server);
                    var line = await r.ReadLineAsync(_cts.Token);
                    if (line == "show") onShow();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Write("single-instance: " + ex.Message);
                    await Task.Delay(500);
                }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}
