namespace VelaRay.Core.Platform;

/// <summary>Native system proxy behavior varies by OS. Mac implementation can be added without changing Core.</summary>
public interface ISystemProxyService
{
    void EnableGlobal(int httpPort, int socksPort);
    void EnablePac(int pacPort);
    void Restore();
    void Disable();
    /// <summary>Recover a proxy backup after a previous unclean shutdown; safe to call repeatedly.</summary>
    bool RecoverAbandonedBackup();
}

public interface IStartupService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

public interface ITrayService : IDisposable
{
    event Action? ShowRequested;
    event Action? ToggleRequested;
    event Action? ExitRequested;
    void Start();
    void SetStatus(bool connected, string profile);
}

public interface IWindowShell
{
    /// <summary>Whether window close minimizes to the tray (when supported).</summary>
    bool MinimizeToTray { get; set; }
    void Show();
    void Hide();
    void Exit();
}

/// <summary>Maps app storage and optional override to an OS-specific core executable.</summary>
public interface ICoreLocator
{
    string Locate(string applicationDirectory, string? overridePath);
}

/// <summary>Opens the native file manager and highlights a file (Explorer / Finder).</summary>
public interface IFileLocationService
{
    void RevealFile(string absoluteFilePath);
}
