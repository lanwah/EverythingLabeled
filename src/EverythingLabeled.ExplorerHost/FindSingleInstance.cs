using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace EverythingLabeled.ExplorerHost;

public sealed class FindSingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly Func<nint> _onActivate;
    private readonly SynchronizationContext? _syncContext;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _pipeThread;
    private bool _disposed;

    private FindSingleInstance(Mutex mutex, string pipeName, Func<nint> onActivate)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        _onActivate = onActivate;
        _syncContext = SynchronizationContext.Current;
        _pipeThread = new Thread(PipeLoop)
        {
            IsBackground = true,
            Name = "FindHostPipe",
        };
        _pipeThread.Start();
    }

    public static bool TryAcquire(
        string mutexName,
        string pipeName,
        Func<nint> onActivate,
        out FindSingleInstance? instance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(onActivate);

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, mutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                instance = null;
                return false;
            }

            instance = new FindSingleInstance(mutex, pipeName, onActivate);
            return true;
        }
        catch
        {
            mutex?.Dispose();
            instance = null;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // ignore
        }

        if (!_pipeThread.Join(TimeSpan.FromSeconds(2)))
        {
            // Best-effort; background thread is IsBackground so process exit is safe.
        }

        _cts.Dispose();
        _mutex.Dispose();
    }

    private void PipeLoop()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                server.WaitForConnectionAsync(ct).GetAwaiter().GetResult();
                if (ct.IsCancellationRequested)
                    break;

                using var reader = new StreamReader(
                    server,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 256,
                    leaveOpen: true);
                var line = reader.ReadLine();
                if (line is not null && string.Equals(line.TrimEnd(), "Activate", StringComparison.Ordinal))
                {
                    var hwnd = InvokeOnActivate();
                    using var writer = new StreamWriter(
                        server,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        leaveOpen: true)
                    {
                        AutoFlush = true,
                        NewLine = "\n",
                    };
                    writer.WriteLine(hwnd.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (ct.IsCancellationRequested)
                    break;
            }
            finally
            {
                try
                {
                    server?.Dispose();
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private nint InvokeOnActivate()
    {
        nint hwnd = 0;
        if (_syncContext is not null)
        {
            // Send (not Post): client waits for HWND after Show/Activate completes.
            _syncContext.Send(
                _ =>
                {
                    try
                    {
                        hwnd = _onActivate();
                    }
                    catch
                    {
                        // ignore UI activation failures
                    }
                },
                null);
            return hwnd;
        }

        try
        {
            return _onActivate();
        }
        catch
        {
            return 0;
        }
    }
}
