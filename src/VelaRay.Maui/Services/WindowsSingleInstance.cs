using System.Threading;

namespace VelaRay.Maui.Services;

/// <summary>
/// Keeps exactly one VelaRay UI process per Windows logon session. A second launch
/// signals the existing instance to show its window, then exits before services
/// (including the proxy runner) are initialized. The OS releases the mutex if the
/// owner is terminated or crashes.
/// </summary>
internal static class WindowsSingleInstance
{
    private const string MutexName = @"Local\VelaRay.Singleton.7b4dcf5a";
    private const string ActivateName = @"Local\VelaRay.Activate.7b4dcf5a";
    private static Mutex? _owner;
    private static EventWaitHandle? _activation;
    private static ManualResetEvent? _shutdown;
    private static Thread? _listener;

    internal static event Action? ActivationRequested;

    internal static bool TryAcquireOrActivate()
    {
        if (_owner is not null) return true; // MAUI may recreate the builder in a development session.
        var candidate = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var first);
        if (!first)
        {
            candidate.Dispose();
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivateName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        _owner = candidate;
        _shutdown = new ManualResetEvent(false);
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
        _listener = new Thread(ListenForActivation)
        {
            Name = "VelaRay single-instance activation",
            IsBackground = true
        };
        _listener.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopListening();
        return true;
    }

    private static void ListenForActivation()
    {
        var ready = _activation;
        var stop = _shutdown;
        if (ready is null || stop is null) return;
        try
        {
            var handles = new WaitHandle[] { ready, stop };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                try { ActivationRequested?.Invoke(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }
        catch (ObjectDisposedException) { }
    }

    private static void StopListening()
    {
        _shutdown?.Set();
        // Leave mutex ownership to the OS until process exit; it is owned by the
        // UI thread, so releasing it from a ProcessExit callback would be unsafe.
    }
}
