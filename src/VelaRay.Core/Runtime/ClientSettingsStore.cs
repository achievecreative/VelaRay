using System.Text.Json;
using VelaRay.Core.Models;
using VelaRay.Core.Configuration;

namespace VelaRay.Core.Runtime;

/// <summary>
/// Portable storage rooted at the directory containing the application executable.
/// Nothing is implicitly written to LocalAppData. The target directory must be writable.
/// Keep this platform-neutral for a future Mac Catalyst file-system adapter.
/// </summary>
public sealed class ClientSettingsStore
{
    public ClientSettingsStore(string applicationDirectory)
    {
        if (string.IsNullOrWhiteSpace(applicationDirectory))
            throw new ArgumentException("Application directory is required.", nameof(applicationDirectory));
        DirectoryPath = Path.GetFullPath(applicationDirectory);
    }

    /// <summary>Absolute installation / portable application root (not the current working directory).</summary>
    public string DirectoryPath { get; }
    public string SettingsDirectory => Path.Combine(DirectoryPath, "settings");
    public string ConfigDirectory => Path.Combine(DirectoryPath, "configs");
    public string PacDirectory => Path.Combine(DirectoryPath, "pacs");
    public string LogsDirectory => Path.Combine(DirectoryPath, "logs");
    public string CoreDirectory => Path.Combine(DirectoryPath, "v2ray-core");

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.v5.json");
    public string ProxyBackupPath => Path.Combine(SettingsDirectory, "wininet-proxy-backup.json");
    public string GetCoreConfigPath(string format) => Path.Combine(ConfigDirectory,
        ConfigFlavor.Normalize(format) == ConfigFlavor.V4 ? "config.v4.json" : "config.v5.json");
    // Exact configuration supplied to the active process, never overwritten by previews.
    public string GetActiveConfigPath(string format) => Path.Combine(ConfigDirectory,
        ConfigFlavor.Normalize(format) == ConfigFlavor.V4 ? "config.active.v4.json" : "config.active.v5.json");
    public string LogFilePath => Path.Combine(LogsDirectory, "app.log");
    public string PacFilePath => Path.Combine(PacDirectory, "default.pac");

    /// <summary>
    /// Resolve an app-local relative configuration filename after the whole app is moved.
    /// Existing absolute external V5 paths remain readable for backward compatibility.
    /// </summary>
    public string ResolveFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("文件路径不能为空。", nameof(filePath));
        if (Path.IsPathRooted(filePath)) return Path.GetFullPath(filePath);
        var full = Path.GetFullPath(Path.Combine(DirectoryPath, filePath));
        if (!full.StartsWith(DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("相对文件路径必须位于 VelaRay 应用目录内。", nameof(filePath));
        return full;
    }

    /// <summary>Copy an external V5 config under configs/custom and store a portable relative path.</summary>
    public async Task<string> ImportCustomConfigAsync(string sourceFile, CancellationToken ct = default)
    {
        var source = Path.GetFullPath(sourceFile);
        if (!File.Exists(source)) throw new FileNotFoundException("无法找到 V5 配置文件。", source);
        EnsureDirectories();
        var targetDir = Path.Combine(ConfigDirectory, "custom");
        Directory.CreateDirectory(targetDir);
        var stem = Path.GetFileNameWithoutExtension(source);
        var filename = $"{stem}-{Guid.NewGuid():N}.json";
        var relative = Path.Combine("configs", "custom", filename);
        var target = ResolveFilePath(relative);
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, ct);
        return relative;
    }

    public void EnsureDirectories()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            Directory.CreateDirectory(SettingsDirectory);
            Directory.CreateDirectory(ConfigDirectory);
            Directory.CreateDirectory(PacDirectory);
            // Log directory is created lazily by RuntimeLogStore only on an actual disk write.
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"VelaRay 需要在应用目录写入 configs、settings 和 pacs：{DirectoryPath}。请将便携版放在用户可写目录（例如 D:\\Apps\\VelaRay），不要放在 Program Files。", ex);
        }
    }

    /// <summary>Snapshot bytes from generated or imported JSON for the running process.</summary>
    public async Task<string> CreateActiveConfigSnapshotAsync(string sourcePath, string format, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Core config file is missing.", sourcePath);
        EnsureDirectories();
        var activePath = GetActiveConfigPath(format);
        var temporary = activePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(destination, ct);
            File.Move(temporary, activePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return activePath;
    }

    public async Task<ClientSettings> LoadAsync(CancellationToken ct = default)
    {
        EnsureDirectories();
        if (!File.Exists(SettingsPath)) return new ClientSettings();
        var json = await File.ReadAllTextAsync(SettingsPath, ct);
        var settings = JsonSerializer.Deserialize<ClientSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new ClientSettings();
        // Migrate the previous dedicated-manual-HTTP port into the new PAC proxy
        // listener only if the user has not already set the new port.
        using (var document = JsonDocument.Parse(json))
        {
            var props = document.RootElement;
            if (!props.TryGetProperty(nameof(ClientSettings.PacProxyPort), out _) &&
                props.TryGetProperty("ExplicitHttpPort", out var legacy) &&
                legacy.TryGetInt32(out var port) && port is >= 1 and <= 65535)
                settings.PacProxyPort = port;
        }
        if (settings.Language is not ("zh-CN" or "en-US")) settings.Language = "zh-CN";
        settings.Profiles ??= [];
        foreach (var profile in settings.Profiles)
            profile.ConfigVersion = ConfigFlavor.Normalize(profile.ConfigVersion);
        // Load only the current VelaRay settings schema; do not import previous clients implicitly.
        settings.RoutingRuleSets ??= [];
        CustomRoutingDeployment.MigrateLegacy(settings);
        return settings;
    }

    public async Task SaveAsync(ClientSettings settings, CancellationToken ct = default)
    {
        EnsureDirectories();
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var temporary = SettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, json, ct);
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"无法将设置写入 {SettingsPath}。请将 VelaRay 放在当前用户可写的文件夹内，不要放在 Program Files。", ex);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
