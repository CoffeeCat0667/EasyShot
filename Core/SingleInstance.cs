namespace EasyShot.Core;

internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\EasyShot.SingleInstance";
    private const string SignalName = @"Local\EasyShot.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly Thread _listener;
    private volatile bool _running = true;

    private SingleInstance(Mutex mutex, EventWaitHandle signal, Action onActivate)
    {
        _mutex = mutex;
        _signal = signal;
        _listener = new Thread(() =>
        {
            while (_running)
            {
                if (_signal.WaitOne(200))
                {
                    onActivate();
                }
            }
        })
        {
            IsBackground = true,
            Name = "EasyShot.SingleInstance"
        };

        _listener.Start();
    }

    public static SingleInstance? TryCreate(Action onActivate)
    {
        var mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            mutex.Dispose();
            return null;
        }

        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        return new SingleInstance(mutex, signal, onActivate);
    }

    public static void SignalExisting()
    {
        if (EventWaitHandle.TryOpenExisting(SignalName, out var signal))
        {
            signal.Set();
            signal.Dispose();
        }
    }

    public void Dispose()
    {
        _running = false;
        _listener.Join(500);
        _signal.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
