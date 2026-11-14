using Microsoft.Extensions.Logging;
using VelaRay.Core.Platform;
using VelaRay.Core.Runtime;
using VelaRay.Maui.Services;

namespace VelaRay.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Fail closed: a second launch must not construct a second app or core runner.
#if WINDOWS
        if (!WindowsSingleInstance.TryAcquireOrActivate())
            Environment.Exit(0);
#endif
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
#if WINDOWS
        // Native WinUI TextBox already contains a ScrollViewer. Show its own
        // vertical scrollbar only when the text exceeds the bounded Editor;
        // avoid wrapping Editors in another ScrollView inside the workspace.
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping(
            "VelaRayMultilineEditorScrollbars", (handler, view) =>
            {
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(
                    handler.PlatformView,
                    Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto);
            });
#endif
        builder.Logging.AddDebug();
        // In .NET single-file mode AppContext.BaseDirectory may point to the extracted
        // bundle. Environment.ProcessPath identifies the original EXE that users copied.
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the running VelaRay executable path.");
        // Windows remains portable; macOS/Linux use writable user settings folders
        // if their platform UIs are implemented in a future release.
        var store = new ClientSettingsStore(DesktopDataDirectories.DefaultForCurrentPlatform(exePath));
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<ICoreLocator, PortableCoreLocator>();
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(5) });
        // Windows owns both validation and runtime v2ray processes through a
        // kill-on-close Job Object (also works if the MAUI process crashes).
#if WINDOWS
        // Native adapters are Windows-only. Other targets require their own platform registration.
        builder.Services.AddSingleton<IChildProcessLifetime, WindowsJobObjectLifetime>();
        builder.Services.AddSingleton<V2RayCoreRunner>();
        builder.Services.AddSingleton<GeoDataUpdater>();
        builder.Services.AddSingleton<CoreReleaseUpdater>();
        builder.Services.AddSingleton<ISystemProxyService>(new WindowsSystemProxyService(store.SettingsDirectory));
        builder.Services.AddSingleton<IStartupService, WindowsStartupService>();
        builder.Services.AddSingleton<IFileLocationService, WindowsFileLocationService>();
        builder.Services.AddSingleton<ITrayService, WindowsTrayService>();
        builder.Services.AddSingleton<WindowsWindowShell>();
        builder.Services.AddSingleton<IWindowHostService>(sp => sp.GetRequiredService<WindowsWindowShell>());
        builder.Services.AddSingleton<IWindowShell>(sp => sp.GetRequiredService<IWindowHostService>());
#else
        throw new PlatformNotSupportedException("VelaRay desktop platform adapters must be implemented before enabling this target.");
#endif
        builder.Services.AddSingleton<PacHttpServer>();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
