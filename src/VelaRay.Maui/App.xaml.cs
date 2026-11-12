using Microsoft.Extensions.DependencyInjection;
using VelaRay.Core.Platform;
using VelaRay.Core.Runtime;
using VelaRay.Maui.Services;

namespace VelaRay.Maui;

public partial class App : Application
{
    private readonly MainPage _page;
    private readonly ISystemProxyService _systemProxy;
    private readonly PacHttpServer _pac;
    private readonly V2RayCoreRunner _core;
    private readonly IChildProcessLifetime _processLifetime;
    private int _cleanupStarted;
    private readonly ITrayService _tray;
    private readonly IWindowHostService _shell;

    public App(IServiceProvider services, ISystemProxyService systemProxy, PacHttpServer pac, V2RayCoreRunner core,
        IChildProcessLifetime processLifetime, ITrayService tray, IWindowHostService shell)
    {
        // Resolve XAML pages only after Application.Resources (Theme.xaml) is loaded.
        // Injecting MainPage directly in this constructor makes DI create it before
        // App.InitializeComponent(), so MainPage StaticResource lookups fail.
        InitializeComponent();
        _page = services.GetRequiredService<MainPage>();
        _systemProxy = systemProxy; _pac = pac; _core = core; _processLifetime = processLifetime;
        _tray = tray; _shell = shell;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupOnExit();
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.IsTerminating) CleanupOnExit();
        };
#if WINDOWS
        WindowsSingleInstance.ActivationRequested += () => MainThread.BeginInvokeOnMainThread(_shell.Show);
#endif
        _tray.ShowRequested += () => MainThread.BeginInvokeOnMainThread(_shell.Show);
        _tray.ToggleRequested += () => MainThread.BeginInvokeOnMainThread(async () => await _page.ToggleConnectionAsync());
        _tray.ExitRequested += () => MainThread.BeginInvokeOnMainThread(_shell.Exit);
        _tray.Start();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_page) { Title = "VelaRay" };
        _shell.Attach(window);
        // Normal window exit, including the explicit Exit command from tray.
        // Closing to tray is intercepted by WindowsWindowShell, so it does NOT
        // destroy the window or kill v2ray.
        window.Destroying += (_, _) => CleanupOnExit();
        return window;
    }

    private void CleanupOnExit()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0) return;
        // Do not let a failing proxy restore prevent child process termination.
        // All of these operations are synchronous; no MAUI dispatcher is needed.
        try { _core.Dispose(); } catch { }
        try { _page.DisposeLogging(); } catch { }
        try { _processLifetime.Dispose(); } catch { }
        try { _pac.Stop(); } catch { }
        try { _systemProxy.Restore(); } catch { }
        try { _tray.Dispose(); } catch { }
    }
}
