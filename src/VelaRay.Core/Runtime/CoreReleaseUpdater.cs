using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VelaRay.Core.Runtime;

/// <summary>Official V2Fly release metadata, resolved to an asset for this OS/CPU.</summary>
public sealed record CoreRelease(string Tag, Version Version, bool IsPrerelease,
    string PackageUrl, string? Sha256, string? ChecksumUrl);

/// <summary>
/// Check/install only the official V2Fly v2ray-core distribution. The caller
/// must hold the connection gate and refuse installation while the core runs.
/// Only the core executable is replaced; user settings and Geo assets survive.
/// </summary>
public sealed class CoreReleaseUpdater(HttpClient httpClient)
{
    public const string ReleasesApi = "https://api.github.com/repos/v2fly/v2ray-core/releases?per_page=40";
    private const long MaxArchiveBytes = 150L * 1024 * 1024;
    private const long MaxExecutableBytes = 200L * 1024 * 1024;
    private static readonly Regex CoreVersionPattern = new(@"\bV2Ray(?:Core)?\s+v?(\d+\.\d+\.\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new(@"(?im)^\s*SHA(?:2-)?256\s*[:=]\s*([0-9a-f]{64})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static Version? ParseVersion(string? output)
    {
        var match = CoreVersionPattern.Match(output ?? "");
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
    }

    public static string? ParseOfficialChecksum(string? checksum)
    {
        var match = Sha256Pattern.Match(checksum ?? "");
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static bool UpdateAvailable(Version? installed, CoreRelease? latest) =>
        latest is not null && (installed is null || latest.Version > installed);

    private static string[] AssetCandidates()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows()) return architecture switch
        {
            Architecture.Arm64 => ["v2ray-windows-arm64-v8a.zip", "v2ray-windows-arm64.zip"],
            Architecture.X86 => ["v2ray-windows-32.zip"],
            Architecture.X64 => ["v2ray-windows-64.zip"],
            _ => throw new PlatformNotSupportedException("Unsupported Windows architecture")
        };
        if (OperatingSystem.IsLinux()) return architecture switch
        {
            Architecture.Arm64 => ["v2ray-linux-arm64-v8a.zip", "v2ray-linux-arm64.zip"],
            Architecture.X64 => ["v2ray-linux-64.zip"],
            _ => throw new PlatformNotSupportedException("Unsupported Linux architecture")
        };
        if (OperatingSystem.IsMacOS()) return architecture switch
        {
            Architecture.Arm64 => ["v2ray-macos-arm64-v8a.zip", "v2ray-macos-arm64.zip"],
            Architecture.X64 => ["v2ray-macos-64.zip"],
            _ => throw new PlatformNotSupportedException("Unsupported macOS architecture")
        };
        throw new PlatformNotSupportedException("Unsupported desktop OS");
    }

    private static string? AssetString(JsonElement asset, string field) =>
        asset.TryGetProperty(field, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;

    private static string? OfficialAssetUrl(JsonElement asset, string tag)
    {
        var url = AssetString(asset, "browser_download_url");
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || parsed.Host != "github.com" ||
            !parsed.AbsolutePath.StartsWith("/v2fly/v2ray-core/releases/download/" + Uri.EscapeDataString(tag) + "/", StringComparison.Ordinal))
            return null;
        return url;
    }

    public static CoreRelease? SelectRelease(string json, bool includePrerelease, IEnumerable<string> assetNames)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub release list was not an array.");
        var candidates = new List<CoreRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            bool prerelease = release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
            if (prerelease && !includePrerelease) continue;
            if (!release.TryGetProperty("tag_name", out var tagElement)) continue;
            var tag = tagElement.GetString();
            if (tag is null || !Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$", RegexOptions.CultureInvariant) ||
                !Version.TryParse(tag[1..], out var version)) continue;
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            foreach (var candidate in assetNames)
            {
                var binary = assets.EnumerateArray().FirstOrDefault(a =>
                    string.Equals(AssetString(a, "name"), candidate, StringComparison.Ordinal));
                if (binary.ValueKind != JsonValueKind.Object) continue;
                var packageUrl = OfficialAssetUrl(binary, tag);
                if (packageUrl is null) continue;
                var checksum = assets.EnumerateArray().FirstOrDefault(a =>
                    string.Equals(AssetString(a, "name"), candidate + ".dgst", StringComparison.Ordinal));
                var checksumUrl = checksum.ValueKind == JsonValueKind.Object ? OfficialAssetUrl(checksum, tag) : null;
                var digest = AssetString(binary, "digest");
                var sha256 = digest is not null && Regex.IsMatch(digest, @"^sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)
                    ? digest[7..].ToUpperInvariant() : null;
                if (sha256 is null && checksumUrl is null) continue; // Never download without verifiable metadata.
                candidates.Add(new CoreRelease(tag, version, prerelease, packageUrl, sha256, checksumUrl));
                break;
            }
        }
        return candidates.OrderByDescending(r => r.Version).FirstOrDefault();
    }

    public async Task<CoreRelease> GetLatestAsync(bool includePrerelease, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        request.Headers.UserAgent.ParseAdd("VelaRay/2.7");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return SelectRelease(json, includePrerelease, AssetCandidates())
            ?? throw new InvalidDataException("No suitable V2Fly release with a verified package was found for this system.");
    }

    public static async Task<Version?> GetInstalledVersionAsync(string executable, CancellationToken ct = default)
    {
        if (!File.Exists(executable)) return null;
        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        };
        start.ArgumentList.Add("version");
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidDataException("Unable to start V2Fly version probe.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var combined = (await stdout) + "\n" + (await stderr);
            if (process.ExitCode != 0 || ParseVersion(combined) is not { } version)
                throw new InvalidDataException("The local v2ray executable did not return a recognized version.");
            return version;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("V2Fly version check timed out.");
        }
    }

    private async Task<string> ExpectedChecksumAsync(CoreRelease release, CancellationToken ct)
    {
        if (release.Sha256 is { } hash && Regex.IsMatch(hash, "^[0-9A-Fa-f]{64}$")) return hash;
        if (release.ChecksumUrl is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != "https" || parsed.Host != "github.com")
            throw new InvalidDataException("The release has no trusted SHA256 checksum.");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("VelaRay/2.7");
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return ParseOfficialChecksum(await response.Content.ReadAsStringAsync(ct))
            ?? throw new InvalidDataException("Official .dgst does not contain a SHA256 checksum.");
    }

    /// <returns>The backup filename, if a previous executable existed.</returns>
    public async Task<string?> InstallAsync(string executable, CoreRelease release, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(release.PackageUrl, UriKind.Absolute, out var url) ||
            url.Scheme != "https" || url.Host != "github.com" ||
            !url.AbsolutePath.StartsWith("/v2fly/v2ray-core/releases/download/" + Uri.EscapeDataString(release.Tag) + "/", StringComparison.Ordinal))
            throw new InvalidDataException("Only official V2Fly release packages may be installed.");
        var exe = Path.GetFullPath(executable);
        var directory = Path.GetDirectoryName(exe)!;
        var filename = OperatingSystem.IsWindows() ? "v2ray.exe" : "v2ray";
        if (Path.GetFileName(exe) != filename)
            throw new InvalidOperationException("Unexpected core filename.");
        Version? installed;
        try { installed = await GetInstalledVersionAsync(exe, ct); }
        catch (Exception ex) when (ex is InvalidDataException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            // Allow a user-confirmed repair of an existing corrupted/unknown binary.
            // It will still be backed up and the replacement version validated.
            installed = null;
        }
        if (installed is not null && release.Version <= installed)
            throw new InvalidOperationException("This release is not newer than the installed core.");
        var expected = await ExpectedChecksumAsync(release, ct);
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, ".velaray-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var archivePath = Path.Combine(staging, "release.zip");
        var candidatePath = Path.Combine(staging, filename);
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, release.PackageUrl))
            {
                request.Headers.UserAgent.ParseAdd("VelaRay/2.7");
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxArchiveBytes)
                    throw new InvalidDataException("Core archive exceeded 150 MiB limit.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[64 * 1024];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > MaxArchiveBytes) throw new InvalidDataException("Core archive exceeded 150 MiB limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            string actual;
            await using (var file = File.OpenRead(archivePath))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("V2Fly release SHA256 mismatch. No files were replaced.");
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var binaries = archive.Entries.Where(e => e.FullName.Equals(filename, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (binaries.Length != 1 || binaries[0].Length is < 1 or > MaxExecutableBytes)
                    throw new InvalidDataException("The official archive must contain exactly one valid root-level core executable.");
                await using var input = binaries[0].Open();
                await using var output = new FileStream(candidatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, ct);
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(candidatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var candidateVersion = await GetInstalledVersionAsync(candidatePath, ct);
            if (candidateVersion != release.Version)
                throw new InvalidDataException($"Downloaded executable version {candidateVersion} differs from release {release.Version}.");
            // Back up before replacing. A failed move restores the previous core.
            string? backup = null;
            if (File.Exists(exe))
            {
                backup = exe + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
                File.Move(exe, backup);
            }
            try { File.Move(candidatePath, exe); }
            catch
            {
                if (backup is not null && !File.Exists(exe)) File.Move(backup, exe);
                throw;
            }
            return backup;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
