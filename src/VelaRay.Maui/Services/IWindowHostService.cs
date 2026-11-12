using VelaRay.Core.Platform;

namespace VelaRay.Maui.Services;

/// <summary>
/// MAUI window lifecycle adapter. Windows currently provides the WinUI implementation;
/// a future Mac Catalyst adapter can implement this contract without changing App.
/// </summary>
public interface IWindowHostService : IWindowShell
{
    void Attach(Microsoft.Maui.Controls.Window window);
}
