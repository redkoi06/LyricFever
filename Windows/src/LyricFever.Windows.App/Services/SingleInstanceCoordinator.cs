using System.Threading;

namespace LyricFever.Windows.App.Services;

/// <summary>
/// 保证同一用户会话中只运行一个实例。再次启动时通知已有实例显示歌词窗。
/// </summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\LyricFever.Windows.App.Instance";
    private const string ActivationEventName = @"Local\LyricFever.Windows.App.Activate";

    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _registeredWait;

    private SingleInstanceCoordinator(Mutex mutex, EventWaitHandle activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
    }

    public static bool TryAcquire(out SingleInstanceCoordinator? coordinator)
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            coordinator = null;
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                activationEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // 主实例仍在初始化；退出即可，避免产生第二个托盘实例。
            }
            return false;
        }

        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        coordinator = new SingleInstanceCoordinator(mutex, signal);
        return true;
    }

    public void StartListening(Action onActivationRequested)
    {
        if (_activationEvent == null) return;
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => onActivationRequested(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registeredWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _mutex.Dispose();
        }
    }
}
