using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VelaRay.Core.Runtime;

/// <summary>Fetch V2Fly geodata into v2ray-core/ and verify upstream SHA256 before replacement.</summary>
public sealed class GeoDataUpdater(HttpClient httpClient)
{
    private static readonly (string Source, string Destination)[] DataFiles =
    [
        ("https://github.com/v2fly/domain-list-community/releases/latest/download/dlc.dat", "geosite.dat"),
        ("https://github.com/v2fly/geoip/releases/latest/download/geoip.dat", "geoip.dat"),
        ("https://github.com/v2fly/geoip/releases/latest/download/geoip-only-cn-private.dat", "geoip-only-cn-private.dat")
    ];

    public async Task<int> UpdateAsync(string v2rayExecutablePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(v2rayExecutablePath))
            ?? throw new ArgumentException("Invalid v2ray.exe path.");
        Directory.CreateDirectory(directory);
        int count = 0;
        foreach (var (source, targetName) in DataFiles)
        {
            // Checksums must come from the same official release as the binary.
            using var hashReq = new HttpRequestMessage(HttpMethod.Get, source + ".sha256sum");
            hashReq.Headers.UserAgent.ParseAdd("VelaRay/1.0");
            using var hashResult = await httpClient.SendAsync(hashReq, cancellationToken);
            hashResult.EnsureSuccessStatusCode();
            var checksumText = (await hashResult.Content.ReadAsStringAsync(cancellationToken)).Trim();
            var match = Regex.Match(checksumText, @"\b[0-9a-fA-F]{64}\b", RegexOptions.CultureInvariant);
            if (!match.Success) throw new InvalidDataException("Official SHA256 checksum missing for " + targetName);
            var expected = match.Value;
            var temporary = Path.Combine(directory, targetName + ".download-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, source);
                request.Headers.UserAgent.ParseAdd("VelaRay/1.0");
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[64 * 1024];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        total += read;
                        if (total > 100 * 1024 * 1024) throw new InvalidDataException("Geo asset exceeded 100 MiB limit.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }
                string actual;
                // Close verification handle before File.Move: Windows cannot rename this file while open.
                await using (var verification = File.OpenRead(temporary))
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(verification, cancellationToken));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA256 verification failed for " + targetName);
                File.Move(temporary, Path.Combine(directory, targetName), overwrite: true);
                count++;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return count;
    }
}
