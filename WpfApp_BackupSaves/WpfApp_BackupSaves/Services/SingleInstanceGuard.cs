using System.Threading;

namespace WpfApp_BackupSaves.Services;

/// <summary>
/// Ensures only one GUI instance runs. A second launch signals the first to activate.
/// Headless --backup must not use this guard.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\BackupSaves.Gui";
    private const string ActivateEventName = @"Local\BackupSaves.Gui.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private CancellationTokenSource? _listenCts;
    private bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activateEvent, bool ownsMutex)
    {
        _mutex = mutex;
        _activateEvent = activateEvent;
        _ownsMutex = ownsMutex;
    }

    /// <summary>
    /// Tries to become the primary GUI instance. If another instance owns the mutex,
    /// signals it to activate and returns null.
    /// </summary>
    public static SingleInstanceGuard? TryAcquirePrimary()
    {
        EventWaitHandle? activateEvent = null;
        Mutex? mutex = null;
        try
        {
            activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);
            if (!createdNew)
            {
                activateEvent.Set();
                activateEvent.Dispose();
                mutex.Dispose();
                return null;
            }

            return new SingleInstanceGuard(mutex, activateEvent, ownsMutex: true);
        }
        catch
        {
            activateEvent?.Dispose();
            if (mutex is not null)
            {
                try
                {
                    if (mutex.WaitOne(0))
                        mutex.ReleaseMutex();
                }
                catch
                {
                    // ignored
                }

                mutex.Dispose();
            }

            throw;
        }
    }

    public void StartListening(Action onActivate)
    {
        ArgumentNullException.ThrowIfNull(onActivate);
        if (_listenCts is not null)
            throw new InvalidOperationException("Already listening.");

        _listenCts = new CancellationTokenSource();
        var token = _listenCts.Token;
        var ev = _activateEvent;

        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!ev.WaitOne(500))
                        continue;
                    if (token.IsCancellationRequested)
                        break;
                    onActivate();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        _listenCts = null;

        _activateEvent.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // ignored
            }

            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
