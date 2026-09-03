namespace AnarlogTrigger;

/// <summary>
/// Ensures only one AnarlogTrigger instance runs per user session.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\AnarlogTrigger.SingleInstance";

    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
            // ignored
        }

        _mutex.Dispose();
    }
}
