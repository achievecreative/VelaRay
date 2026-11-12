namespace VelaRay.Core.Platform;

/// <summary>
/// Selects a writable desktop data location without making the Core layer depend
/// on WinUI, AppKit or a particular Linux desktop. Windows intentionally keeps
/// VelaRay's portable folders beside the distributed EXE.
/// </summary>
public static class DesktopDataDirectories
{
    public static string DefaultForCurrentPlatform(string processExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(processExecutablePath))
            throw new ArgumentException("The process executable path is required.", nameof(processExecutablePath));

        if (OperatingSystem.IsWindows())
            return Path.GetDirectoryName(Path.GetFullPath(processExecutablePath))!;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException("A writable user home directory is required on this platform.");

        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Application Support", "VelaRay");
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var parent = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(home, ".config");
            return Path.Combine(parent, "velaray");
        }
        throw new PlatformNotSupportedException("A desktop data directory must be configured for this operating system.");
    }
}
