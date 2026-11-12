using Microsoft.UI.Windowing;
using VelaRay.Core.Platform;

namespace VelaRay.Maui.Services;

/// <summary>WinUI AppWindow close-to-tray integration. A macOS implementation can replace this via DI.</summary>
public sealed class WindowsWindowShell : IWindowHostService
{
    private Microsoft.UI.Xaml.Window? _native;
    private bool _exiting;
    private string? _titleBarIconPath;
    public bool MinimizeToTray { get; set; } = true;
    public void Attach(Microsoft.Maui.Controls.Window window)
    {
        window.Destroying += (_, _) =>
        {
            if (_titleBarIconPath is { } path)
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                _titleBarIconPath = null;
            }
        };
        window.HandlerChanged += (_, _) =>
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native) return;
            if (ReferenceEquals(_native, native)) return;
            _native = native;
            _titleBarIconPath = ApplyNativeTitleBarIcon(native);
            native.AppWindow.Closing += (_, args) =>
            {
                if (_exiting || !MinimizeToTray) return;
                args.Cancel = true;
                native.AppWindow.Hide();
            };
            if (Environment.GetCommandLineArgs().Any(x => x.Equals("/background", StringComparison.OrdinalIgnoreCase)))
                native.AppWindow.Hide();
        };
    }
    /// <summary>
    /// WinUI uses a native .ico for the non-client title bar, not a MAUI Image.
    /// The icon is embedded in VelaRay.exe, so no additional file is distributed.
    /// Only the runtime title-bar cache lives under the OS temporary directory.
    /// </summary>
    private static string? ApplyNativeTitleBarIcon(Microsoft.UI.Xaml.Window native)
    {
        try
        {
            using var source = typeof(WindowsWindowShell).Assembly.GetManifestResourceStream("VelaRay.Assets.velaray.ico")
                ?? throw new InvalidOperationException("Embedded VelaRay icon is missing.");
            var folder = Path.Combine(Path.GetTempPath(), "VelaRay");
            Directory.CreateDirectory(folder);
            var iconPath = Path.Combine(folder, "velaray-titlebar-" + Environment.ProcessId + ".ico");
            using (var target = new FileStream(iconPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                source.CopyTo(target);
            native.AppWindow.SetIcon(iconPath);
            return iconPath;
        }
        catch (Exception ex)
        {
            // Icon rendering must not stop the proxy application from launching.
            System.Diagnostics.Debug.WriteLine("Could not set VelaRay title-bar icon: " + ex);
            return null;
        }
    }
    public void Show() { _native?.AppWindow.Show(); _native?.Activate(); }
    public void Hide() => _native?.AppWindow.Hide();
    public void Exit()
    {
        _exiting = true;
        _native?.Close();
    }
}
