using System.Net;
using System.Text.Json.Nodes;
using VelaRay.Core.Models;
using VelaRay.Core.Pac;

namespace VelaRay.Core.Configuration;

/// <summary>Smart routing for traffic that has already entered v2ray-core.
/// Explicit direct rules have priority, followed by proxy exceptions, local/private
/// and Chinese sites. Unmatched traffic falls through to the FIRST V2Fly outbound,
/// which is direct by default or proxy when explicitly selected. Avoid catch-all
/// network rules: those can short-circuit IPIfNonMatch/IpIfNonMatch GeoIP matching.</summary>
public static class SmartRoutingPolicy
{
    public const string UnmatchedDirect = "Direct";
    public const string UnmatchedProxy = "Proxy";

    public static bool ProxyUnmatched(ClientSettings settings) =>
        settings.RoutingMode == "Smart" &&
        string.Equals(settings.SmartUnmatchedRoute, UnmatchedProxy, StringComparison.Ordinal);

    /// <summary>With Smart Proxy fallback, a managed PAC must not discard unmatched
    /// requests before they reach V2Fly. Explicit PAC DIRECT entries still win, and
    /// the ChinaSitesDirect template retains its offline China-domain exclusions.
    /// Custom/handwritten PAC files are deliberately never rewritten.</summary>
    public static string EffectiveManagedPacTemplate(ClientSettings settings) =>
        ProxyUnmatched(settings) && settings.PacTemplate == PacTemplateGenerator.DirectByDefault
            ? PacTemplateGenerator.ProxyByDefault
            : settings.PacTemplate;

    public static JsonObject BuildV4(ClientSettings settings)
    {
        var rules = new JsonArray();
        AppendV4(rules, settings);
        return new JsonObject { ["domainStrategy"] = "IPIfNonMatch", ["rules"] = rules };
    }

    public static JsonObject BuildV5(ClientSettings settings)
    {
        var rules = new JsonArray();
        AppendV5(rules, settings);
        return new JsonObject { ["domainStrategy"] = "IpIfNonMatch", ["rule"] = rules };
    }

    public static IReadOnlyList<string> Describe(ClientSettings settings, bool english = false)
    {
        var proxyCount = PacProxyWebsites(settings).Count;
        var directCount = PacTemplateGenerator.ParseWebsites(settings.PacDirectWebsites).Count;
        var lines = new List<string>();
        if (directCount > 0)
            lines.Add(english ? $"PAC explicit direct exceptions ({directCount}) → Direct (priority)"
                              : $"PAC 明确直连例外（{directCount} 条）→ 直连（优先）");
        lines.Add(english ? $"1. PAC proxy domains/IPs ({proxyCount}) → Proxy"
                          : $"1. PAC 代理域名/IP（{proxyCount} 条）→ 代理");
        lines.Add(english ? "2. Private and local addresses → Direct"
                          : "2. 私有及本地地址 → 直连");
        lines.Add(english ? "3. China websites / IP addresses → Direct"
                          : "3. 中国大陆网站/IP → 直连");
        if (ProxyUnmatched(settings))
            lines.Add(english ? "4. Everything else → Proxy (first outbound fallback)"
                              : "4. 其余流量 → 代理（默认第一个出站）");
        else
            lines.Add(english ? "4. Everything else → Direct (first outbound fallback)"
                              : "4. 其余流量 → 直连（默认第一个出站）");
        return lines;
    }

    private static IReadOnlyList<string> PacProxyWebsites(ClientSettings settings) =>
        PacTemplateGenerator.EffectiveProxyWebsites(settings.PacDirectWebsites, settings.PacProxyWebsites);

    private static (List<string> Domains, List<string> Ips) SplitPacWebsites(ClientSettings settings)
    {
        var domains = new List<string>();
        var ips = new List<string>();
        foreach (var name in PacProxyWebsites(settings))
        {
            if (IPAddress.TryParse(name, out var ip)) ips.Add(ip.ToString());
            else domains.Add("domain:" + name);
        }
        return (domains, ips);
    }

    private static JsonArray Strings(IEnumerable<string> strings) => new(strings.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());

    private static JsonObject CidrIp(IPAddress address) => new()
    {
        ["cidr"] = new JsonArray(new JsonObject
        {
            ["ipAddr"] = address.ToString(),
            ["prefix"] = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128
        })
    };

    private static void AppendV4(JsonArray rules, ClientSettings settings)
    {
        var direct = PacTemplateGenerator.ParseWebsites(settings.PacDirectWebsites);
        var directDomains = direct.Where(n => !IPAddress.TryParse(n, out _)).Select(n => "domain:" + n).ToList();
        var directIps = direct.Where(n => IPAddress.TryParse(n, out _)).ToList();
        if (directDomains.Count > 0) rules.Add(new JsonObject { ["type"] = "field", ["outboundTag"] = "direct", ["domain"] = Strings(directDomains) });
        if (directIps.Count > 0) rules.Add(new JsonObject { ["type"] = "field", ["outboundTag"] = "direct", ["ip"] = Strings(directIps) });
        var (domains, ips) = SplitPacWebsites(settings);
        if (domains.Count > 0) rules.Add(new JsonObject { ["type"] = "field", ["outboundTag"] = "proxy", ["domain"] = Strings(domains) });
        if (ips.Count > 0) rules.Add(new JsonObject { ["type"] = "field", ["outboundTag"] = "proxy", ["ip"] = Strings(ips) });
        var file = GeoDataAssets.NormalizeGeoIpFile(settings.GeoIpFile);
        rules.Add(new JsonObject { ["type"] = "field", ["outboundTag"] = "direct", ["ip"] = new JsonArray($"ext:{file}:private") });
        rules.Add(new JsonObject { ["type"] = "field", ["outboundTag"] = "direct", ["domain"] = new JsonArray("full:localhost", "domain:local") });
        rules.Add(new JsonObject { ["type"] = "field", ["outboundTag"] = "direct", ["domain"] = new JsonArray("geosite:cn") });
        rules.Add(new JsonObject { ["type"] = "field", ["outboundTag"] = "direct", ["ip"] = new JsonArray($"ext:{file}:cn") });
    }

    private static void AppendV5(JsonArray rules, ClientSettings settings)
    {
        var direct = PacTemplateGenerator.ParseWebsites(settings.PacDirectWebsites);
        var directDomains = new JsonArray();
        var directIps = new JsonArray();
        foreach (var value in direct)
        {
            if (IPAddress.TryParse(value, out var ip))
                directIps.Add(CidrIp(ip));
            else directDomains.Add(new JsonObject { ["type"] = "RootDomain", ["value"] = value });
        }
        if (directDomains.Count > 0) rules.Add(new JsonObject { ["tag"] = "direct", ["domain"] = directDomains });
        if (directIps.Count > 0) rules.Add(new JsonObject { ["tag"] = "direct", ["geoip"] = directIps });
        var (domains, ips) = SplitPacWebsites(settings);
        if (domains.Count > 0)
        {
            var parts = new JsonArray();
            foreach (var name in domains)
                parts.Add(new JsonObject { ["type"] = "RootDomain", ["value"] = name[7..] });
            rules.Add(new JsonObject { ["tag"] = "proxy", ["domain"] = parts });
        }
        if (ips.Count > 0)
        {
            var ipParts = new JsonArray();
            foreach (var literal in ips)
            {
                var address = IPAddress.Parse(literal);
                ipParts.Add(CidrIp(address));
            }
            rules.Add(new JsonObject { ["tag"] = "proxy", ["geoip"] = ipParts });
        }
        var file = GeoDataAssets.NormalizeGeoIpFile(settings.GeoIpFile);
        rules.Add(new JsonObject { ["tag"] = "direct", ["geoip"] = new JsonArray(new JsonObject
            { ["filePath"] = file, ["code"] = "private" }) });
        rules.Add(new JsonObject { ["tag"] = "direct", ["domain"] = new JsonArray(
            new JsonObject { ["type"] = "Full", ["value"] = "localhost" },
            new JsonObject { ["type"] = "RootDomain", ["value"] = "local" }) });
        rules.Add(new JsonObject { ["tag"] = "direct", ["geoDomain"] = new JsonArray(new JsonObject
            { ["filePath"] = "geosite.dat", ["code"] = "cn" }) });
        rules.Add(new JsonObject { ["tag"] = "direct", ["geoip"] = new JsonArray(new JsonObject
            { ["filePath"] = file, ["code"] = "cn" }) });
    }
}
