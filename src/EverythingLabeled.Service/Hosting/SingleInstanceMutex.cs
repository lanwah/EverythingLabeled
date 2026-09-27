namespace EverythingLabeled.Service.Hosting;

public sealed class SingleInstanceMutex : IDisposable
{
    private readonly Mutex _mutex;
    private bool _hasHandle;

    private SingleInstanceMutex(Mutex mutex, bool hasHandle)
    {
        _mutex = mutex;
        _hasHandle = hasHandle;
    }

    /// <summary>
    /// Tries to acquire the named mutex. Returns null when another instance already holds it.
    /// </summary>
    public static SingleInstanceMutex? TryAcquire(string mutexName)
    {
        var mutex = new Mutex(false, mutexName, out _);
        var hasHandle = false;
        try
        {
            hasHandle = mutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            hasHandle = true;
        }

        if (!hasHandle)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceMutex(mutex, hasHandle);
    }

    public void Dispose()
    {
        if (_hasHandle)
        {
            _mutex.ReleaseMutex();
            _hasHandle = false;
        }

        _mutex.Dispose();
    }
}
