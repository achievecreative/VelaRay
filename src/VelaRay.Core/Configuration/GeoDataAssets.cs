using VelaRay.Core.Models;

namespace VelaRay.Core.Configuration;

/// <summary>Geo files belong to V2Fly's asset directory, not the Windows PAC engine.</summary>
public static class GeoDataAssets
{
    public const string FullGeoIp = "geoip.dat";
    public const string ChinaPrivateGeoIp = "geoip-only-cn-private.dat";

    public static string NormalizeGeoIpFile(string? value) => value switch
    {
        null or "" or ChinaPrivateGeoIp => ChinaPrivateGeoIp,
        FullGeoIp => FullGeoIp,
        _ => throw new ArgumentException("GeoIP dataset must be geoip.dat or geoip-only-cn-private.dat.")
    };

    public static IReadOnlyList<string> RequiredForDefaultRouting(ClientSettings settings) =>
        (settings.RoutingMode is "BypassCN" or "Smart")
            ? ["geosite.dat", NormalizeGeoIpFile(settings.GeoIpFile)]
            : [];

    /// <summary>Check data assets referenced by either V4 external GeoIP or V5 Geo objects.</summary>
    public static IReadOnlyList<string> MissingForConfig(string configText, string coreExecutable)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(coreExecutable))
            ?? throw new ArgumentException("Invalid core executable path.");
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var doc = System.Text.Json.JsonDocument.Parse(configText);
        Scan(doc.RootElement);
        return referenced.Where(name => !File.Exists(Path.Combine(directory, name))).Order(StringComparer.Ordinal).ToArray();

        void Scan(System.Text.Json.JsonElement node)
        {
            if (node.ValueKind == System.Text.Json.JsonValueKind.Object)
                foreach (var prop in node.EnumerateObject())
                {
                    if (prop.NameEquals("filePath") && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var name = prop.Value.GetString();
                        if (name is not null && name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                            referenced.Add(Path.GetFileName(name));
                    }
                    Scan(prop.Value);
                }
            else if (node.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var element in node.EnumerateArray()) Scan(element);
            else if (node.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var value = node.GetString() ?? "";
                if (value.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase)) referenced.Add("geosite.dat");
                if (value.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase)) referenced.Add("geoip.dat");
                if (value.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
                {
                    var remaining = value[4..];
                    var colon = remaining.IndexOf(':');
                    if (colon > 0 && remaining[..colon].EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                        referenced.Add(Path.GetFileName(remaining[..colon]));
                }
            }
        }
    }

    public static IReadOnlyList<string> MissingForDefaultRouting(ClientSettings settings, string coreExecutable)
    {
        var assetDirectory = Path.GetDirectoryName(Path.GetFullPath(coreExecutable))
            ?? throw new ArgumentException("Invalid core executable path.");
        return RequiredForDefaultRouting(settings)
            .Where(name => !File.Exists(Path.Combine(assetDirectory, name))).ToArray();
    }
}
