using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
namespace SandboxTimeline;

public static class SingleInstanceManager
{
    private const string MutexName = @"Global\SandboxTimeline.SingleInstance.v1";
    private const string PipeName = "SandboxTimeline.ActivatePipe.v1";
    private const string WindowTitle = "Sandbox Timeline";

    private static Mutex? _mutex;
    private static CancellationTokenSource? _listenerCts;
    private static bool _ownsMutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        _ownsMutex = createdNew;
        return createdNew;
    }

    public static bool RequestActivateExistingWindow()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (TrySignalNamedPipe())
            {
                return true;
            }

            if (TryForegroundByWindowTitle())
            {
                return true;
            }

            Thread.Sleep(300);
        }

        return false;
    }

    public static void StartActivationListener(Action activateMainWindow)
    {
        StopActivationListener();
        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var command = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (string.Equals(command, "SHOW", StringComparison.OrdinalIgnoreCase))
                    {
                        activateMainWindow();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
            }
        }, token);
    }

    public static void Release()
    {
        StopActivationListener();

        if (_mutex == null)
        {
            return;
        }

        try
        {
            if (_ownsMutex)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch
        {
        }

        _mutex.Dispose();
        _mutex = null;
        _ownsMutex = false;
    }

    private static void StopActivationListener()
    {
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _listenerCts = null;
    }

    private static bool TrySignalNamedPipe()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine("SHOW");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryForegroundByWindowTitle()
    {
        var handle = FindWindow(null, WindowTitle);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (IsIconic(handle))
        {
            ShowWindow(handle, SwRestore);
        }

        ShowWindow(handle, SwShow);
        return SetForegroundWindow(handle);
    }

    private const int SwRestore = 9;
    private const int SwShow = 5;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
