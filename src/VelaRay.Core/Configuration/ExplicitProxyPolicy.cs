using System.Text.Json.Nodes;

namespace VelaRay.Core.Configuration;

/// <summary>
/// Application-configured SOCKS5 and HTTP proxies always use the remote outbound.
/// PAC scripts point to an independent HTTP ingress whose requests still respect
/// the selected Smart/BypassCN/Custom routing rules. Inbound origin is the only
/// reliable distinction: the same port cannot distinguish PAC from manual apps.
/// </summary>
public static class ExplicitProxyPolicy
{
    public const string PacHttpTag = "http-pac-in";

    public static bool NeedsForcedRule(string effectiveRoutingMode) =>
        effectiveRoutingMode != "ProxyAll";

    private static JsonArray ManualInboundTags() => new("socks-in", "http-in");

    public static JsonObject V4Rule(bool balanced) =>
        balanced
            ? new JsonObject { ["type"] = "field", ["inboundTag"] = ManualInboundTags(), ["balancerTag"] = "balance" }
            : new JsonObject { ["type"] = "field", ["inboundTag"] = ManualInboundTags(), ["outboundTag"] = "proxy" };

    public static JsonObject V5Rule(bool balanced) =>
        balanced
            ? new JsonObject { ["inboundTag"] = ManualInboundTags(), ["balancingTag"] = "balance" }
            : new JsonObject { ["inboundTag"] = ManualInboundTags(), ["tag"] = "proxy" };
}
