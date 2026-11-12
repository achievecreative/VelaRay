namespace VelaRay.Core.Platform;

/// <summary>
/// Resolves a user-provided V2Fly core path relative to the portable application folder.
/// This does not download, bundle or install the core. The UI layer owns OS-specific
/// process and system proxy behavior; path resolution is platform-neutral.
/// </summary>
public sealed class PortableCoreLocator : ICoreLocator
{
    public string Locate(string applicationDirectory, string? overridePath)
    {
        var basePath = Path.GetFullPath(applicationDirectory);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var candidate = overridePath.Trim();
            return Path.GetFullPath(Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(basePath, candidate));
        }
        var executable = OperatingSystem.IsWindows() ? "v2ray.exe" : "v2ray";
        return Path.Combine(basePath, "v2ray-core", executable);
    }
}
