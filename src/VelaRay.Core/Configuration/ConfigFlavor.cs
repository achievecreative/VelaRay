using System.Text.Json.Nodes;
using VelaRay.Core.Models;

namespace VelaRay.Core.Configuration;

/// <summary>V2Fly v5 core supports two different configuration loaders, not two executable cores.</summary>
public static class ConfigFlavor
{
    public const string V4 = "V4";
    public const string V5 = "V5";

    public static string Normalize(string? format) => format?.Trim().ToUpperInvariant() switch
    {
        V4 => V4,
        V5 or null or "" => V5,
        _ => throw new ArgumentException($"Unsupported configuration format: {format}")
    };

    public static string Loader(string? format) => Normalize(format) == V4 ? "json" : "jsonv5";

    /// <summary>Detect unmistakable V4/V5 markers. Null means there are not enough
    /// clues; the real V2Fly validator is still the source of truth.</summary>
    public static string? DetectFromJson(string source)
    {
        var root = JsonNode.Parse(source) as JsonObject
            ?? throw new ArgumentException("Core configuration must be a JSON object.");
        if (root.ContainsKey("router") && root.ContainsKey("routing"))
            throw new ArgumentException("Mixed V4 routing and V5 router objects are not supported.");
        if (root.ContainsKey("router")) return V5;
        if (root.ContainsKey("routing")) return V4;
        if (root["outbounds"] is JsonArray outbounds)
            foreach (var entry in outbounds.OfType<JsonObject>())
                if (entry["streamSettings"] is JsonObject stream)
                {
                    if (stream.ContainsKey("transport")) return V5;
                    if (stream.ContainsKey("network")) return V4;
                }
        return null;
    }

    public static string Build(ClientSettings settings, ServerProfile profile) =>
        Normalize(profile.ConfigVersion) == V4
            ? V4ConfigBuilder.Build(settings, profile)
            : V5ConfigBuilder.Build(settings, profile);
}

public static class ProfileCapabilities
{
    /// <summary>
    /// A new profile starts as native Hysteria2 with TLS enabled. When the
    /// user changes that profile to VMess/Trojan, do not silently carry the
    /// Hysteria2 transport and mandatory TLS into an ordinary TCP node.
    /// Existing non-Hysteria2 profiles retain their explicitly chosen settings.
    /// </summary>
    public static (string Transport, bool TlsEnabled) ProtocolSwitchDefaults(
        string previousProtocol, string nextProtocol, string previousTransport, bool tlsEnabled)
    {
        if (nextProtocol == "hysteria2") return ("hysteria2", true);
        if (previousProtocol == "hysteria2" && previousTransport == "hysteria2"
            && (nextProtocol is "vmess" or "trojan"))
            return ("tcp", false);
        return (previousTransport, tlsEnabled);
    }

    public static IReadOnlyList<string> Transports(string format, string protocol)
    {
        if (protocol == "hysteria2") return ["hysteria2"];
        return ConfigFlavor.Normalize(format) == ConfigFlavor.V4
            ? ["tcp", "ws", "grpc", "httpupgrade", "mkcp", "quic", "hysteria2"]
            : ["tcp", "ws", "grpc", "httpupgrade", "mkcp", "quic", "hysteria2"];
    }

    public static bool SupportsVmessSecurity(string format, string protocol) =>
        ConfigFlavor.Normalize(format) == ConfigFlavor.V4 && protocol == "vmess";

    public static bool SupportsPinning(string transport) => transport != "hysteria2";
}

/// <summary>Keep outbound names referenced by imported V4 routing rules. Never silently remap them to proxy.</summary>
internal static class OutboundPlanner
{
    public static void ValidateReferenced(ClientSettings settings, ServerProfile profile, string outboundTag)
    {
        try { V5ConfigBuilder.Validate(settings, profile); }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Outbound '{outboundTag}', node '{profile.Name}' ({profile.Protocol}): {ex.Message}", ex);
        }
    }

    public static List<(ServerProfile Profile, string Tag)> Get(ClientSettings settings, ServerProfile selected)
    {
        var list = new List<(ServerProfile, string)> { (selected, "proxy") };
        if (settings.Profiles.Any(p => p.Id == selected.Id && p.Protocol == "shadowsocks"))
            throw new NotSupportedException("Shadowsocks has been removed.");
        var used = new HashSet<string>(StringComparer.Ordinal) { "proxy", "direct", "block" };
        if (settings.BalanceAllNodes && settings.Profiles.Count > 1)
        {
            if (settings.Profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != settings.Profiles.Count)
                throw new ArgumentException("Duplicate profile IDs cannot be balanced.");
            foreach (var profile in settings.Profiles)
            {
                var tag = "node-" + profile.Id;
                if (used.Add(tag)) list.Add((profile, tag));
            }
        }
        if (settings.RoutingMode != "Custom") return list;
        var root = V5ConfigBuilder.ParseObject(CustomRoutingDeployment.ResolveJson(settings), "routing rules");
        var references = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in new[] { "rules", "rule" })
            if (root[key] is JsonArray arr)
                foreach (var rule in arr.OfType<JsonObject>())
                {
                    var tag = (string?)(rule["outboundTag"] ?? rule["tag"]);
                    if (!string.IsNullOrWhiteSpace(tag)) references.Add(tag);
                }
        foreach (var reference in references)
        {
            if (reference is "main" or "proxy" or "direct" or "block" or "decline") continue;
            var matches = settings.Profiles.Where(p => p.Name == reference).ToList();
            if (matches.Count != 1)
                throw new ArgumentException($"Routing outbound '{reference}' requires exactly one profile with that name; found {matches.Count}.");
            if (used.Add(reference)) list.Add((matches[0], reference));
        }
        return list;
    }
}
