using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using VelaRay.Core.Platform;

namespace VelaRay.Maui.Services;

/// <summary>HKCU WinINET proxy with persisted original values for crash recovery.</summary>
public sealed class WindowsSystemProxyService(string settingsDirectory) : ISystemProxyService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private static readonly string[] Keys = ["ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL", "AutoDetect"];
    private readonly string _backupPath = Path.Combine(settingsDirectory, "wininet-proxy-backup.json");
    private Dictionary<string, RegistrySnapshot>? _original;
    private Dictionary<string, RegistrySnapshot>? _applied;
    private sealed record RegistrySnapshot(string Kind, string? Value);

    public void EnableGlobal(int httpPort, int socksPort)
    {
        Capture();
        using var key = OpenWritable();
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        // WinINET per-scheme map: browsers usually use HTTP/HTTPS; applications that
        // support Windows SOCKS proxy settings may use the SOCKS5 entry.
        key.SetValue("ProxyServer", WindowsProxyEndpoints.GlobalProxyServer(httpPort, socksPort), RegistryValueKind.String);
        key.SetValue("ProxyOverride", "<local>;localhost;127.*", RegistryValueKind.String);
        key.DeleteValue("AutoConfigURL", false);
        key.SetValue("AutoDetect", 0, RegistryValueKind.DWord);
        Refresh();
        RecordAppliedSnapshot();
    }

    public void EnablePac(int pacPort)
    {
        Capture();
        using var key = OpenWritable();
        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
        key.DeleteValue("ProxyServer", false);
        key.SetValue("AutoConfigURL", $"http://127.0.0.1:{pacPort}/proxy.pac", RegistryValueKind.String);
        key.SetValue("AutoDetect", 0, RegistryValueKind.DWord);
        Refresh();
        RecordAppliedSnapshot();
    }

    public void Disable()
    {
        using var key = OpenWritable();
        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
        key.DeleteValue("AutoConfigURL", false);
        Refresh();
        _original = null;
        if (File.Exists(_backupPath)) File.Delete(_backupPath);
    }

    public void Restore()
    {
        if (_original is null && File.Exists(_backupPath))
            _original = JsonSerializer.Deserialize<Dictionary<string, RegistrySnapshot>>(File.ReadAllText(_backupPath));
        if (_original is null) return;
        using var key = OpenWritable();
        // Do not overwrite a user's deliberate changes made while VelaRay was active.
        if (_applied is not null && !SameProxySettings(key, _applied))
        {
            _original = null;
            _applied = null;
            if (File.Exists(_backupPath)) File.Delete(_backupPath);
            return;
        }
        foreach (var (name, snapshot) in _original)
        {
            if (snapshot.Value is null) key.DeleteValue(name, false);
            else if (snapshot.Kind == nameof(RegistryValueKind.DWord))
                key.SetValue(name, int.Parse(snapshot.Value), RegistryValueKind.DWord);
            else key.SetValue(name, snapshot.Value, RegistryValueKind.String);
        }
        Refresh();
        _original = null;
        _applied = null;
        if (File.Exists(_backupPath)) File.Delete(_backupPath);
    }

    /// <summary>Restore only if our own loopback proxy still seems active after an abnormal exit.</summary>
    public bool RecoverAbandonedBackup()
    {
        if (!File.Exists(_backupPath)) return false;
        using var key = OpenWritable();
        var proxy = key.GetValue("ProxyServer")?.ToString() ?? "";
        var pac = key.GetValue("AutoConfigURL")?.ToString() ?? "";
        if ((proxy.StartsWith("127.0.0.1:", StringComparison.Ordinal) ||
             proxy.Contains("=127.0.0.1:", StringComparison.Ordinal)) ||
            pac.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
        {
            Restore();
            return true;
        }
        // User changed the OS proxy meanwhile; do not overwrite their changes.
        File.Delete(_backupPath);
        return false;
    }

    private void RecordAppliedSnapshot()
    {
        using var key = OpenWritable();
        _applied = ReadSnapshot(key);
    }

    private static Dictionary<string, RegistrySnapshot> ReadSnapshot(RegistryKey key)
    {
        var snapshot = new Dictionary<string, RegistrySnapshot>();
        foreach (var name in Keys)
        {
            var value = key.GetValue(name);
            snapshot[name] = new RegistrySnapshot(value is null ? "None" : key.GetValueKind(name).ToString(), value?.ToString());
        }
        return snapshot;
    }

    private static bool SameProxySettings(RegistryKey key, Dictionary<string, RegistrySnapshot> expected)
    {
        // Compare only settings controlled by us. Users may independently change AutoDetect or ProxyOverride.
        var current = ReadSnapshot(key);
        return new[] { "ProxyEnable", "ProxyServer", "AutoConfigURL" }.All(name =>
            current[name] == expected[name]);
    }

    private static RegistryKey OpenWritable() => Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
        ?? throw new InvalidOperationException("Cannot edit current-user WinINET proxy settings.");

    private void Capture()
    {
        if (_original is not null) return;
        if (File.Exists(_backupPath)) RecoverAbandonedBackup();
        using var key = OpenWritable();
        _original = ReadSnapshot(key);
        Directory.CreateDirectory(settingsDirectory);
        File.WriteAllText(_backupPath, JsonSerializer.Serialize(_original));
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOptionW(IntPtr handle, int option, IntPtr buffer, int length);
    private static void Refresh()
    {
        InternetSetOptionW(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOptionW(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }
}
