using System.Text.Json;
using System.Text.Json.Nodes;
using VelaRay.Core.Models;

namespace VelaRay.Core.Configuration;

/// <summary>Legacy V4 JSON schema for a modern V2Fly v5 core. The core loader is '-format json'.</summary>
public static class V4ConfigBuilder
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string Build(ClientSettings settings, ServerProfile selected)
    {
        V5ConfigBuilder.Validate(settings, selected);
        var routingMode = EffectiveRoutingMode.Resolve(settings);
        var listen = settings.AllowLan ? "0.0.0.0" : "127.0.0.1";
        var outbounds = new JsonArray();
        var directOutbound = new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom", ["settings"] = new JsonObject() };
        // V2Fly routes unmatched connections to the FIRST outbound. Preserve the
        // previous Direct fallback unless Smart/Proxy is explicitly selected.
        if (routingMode == "Smart" && !SmartRoutingPolicy.ProxyUnmatched(settings))
            outbounds.Add(directOutbound);
        foreach (var (profile, tag) in OutboundPlanner.Get(settings, selected))
        {
            OutboundPlanner.ValidateReferenced(settings, profile, tag);
            outbounds.Add(BuildOutbound(profile, tag));
        }
        if (routingMode != "Smart" || SmartRoutingPolicy.ProxyUnmatched(settings))
            outbounds.Add(directOutbound);
        outbounds.Add(new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole", ["settings"] = new JsonObject() });
        var sniffing = new JsonObject { ["enabled"] = settings.Sniffing, ["destOverride"] = new JsonArray("http", "tls") };
        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = settings.LogLevel.ToLowerInvariant() },
            ["inbounds"] = new JsonArray(
                new JsonObject { ["tag"] = "socks-in", ["protocol"] = "socks", ["listen"] = listen,
                    ["port"] = settings.SocksPort, ["settings"] = new JsonObject { ["auth"] = "noauth", ["udp"] = settings.EnableUdp, ["ip"] = listen }, ["sniffing"] = sniffing.DeepClone() },
                new JsonObject { ["tag"] = "http-in", ["protocol"] = "http", ["listen"] = listen,
                    ["port"] = settings.HttpPort, ["settings"] = new JsonObject(), ["sniffing"] = sniffing.DeepClone() }),
            ["outbounds"] = outbounds,
            ["routing"] = BuildRouting(settings, routingMode)
        };
        // PAC scripts use a dedicated loopback HTTP inbound, isolated from manual HTTP.
        // Always listen so changing Windows proxy mode need not alter inbound layout.
        ((JsonArray)config["inbounds"]!).Add(new JsonObject
        {
            ["tag"] = ExplicitProxyPolicy.PacHttpTag, ["protocol"] = "http",
            ["listen"] = "127.0.0.1", ["port"] = settings.PacProxyPort,
            ["settings"] = new JsonObject(), ["sniffing"] = sniffing.DeepClone()
        });
        var dns = settings.DnsServers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (dns.Length > 0) config["dns"] = new JsonObject { ["servers"] = new JsonArray(dns.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) };
        return config.ToJsonString(Pretty);
    }

    public static JsonObject BuildOutbound(ServerProfile p, string tag = "proxy")
    {
        var protocol = p.Protocol.ToLowerInvariant();
        var settings = protocol switch
        {
            "vmess" => new JsonObject { ["vnext"] = new JsonArray(new JsonObject { ["address"] = p.Address, ["port"] = p.Port,
                ["users"] = new JsonArray(new JsonObject { ["id"] = p.Uuid, ["alterId"] = p.VmessAlterId, ["security"] = NormalizeVmessSecurity(p.VmessSecurity) }) }) },
            "trojan" => new JsonObject { ["servers"] = new JsonArray(BuildServer(p)) },
            "hysteria2" => new JsonObject { ["servers"] = new JsonArray(new JsonObject { ["address"] = p.Address, ["port"] = p.Port }) },
            _ => throw new ArgumentException("Unsupported protocol: " + protocol)
        };
        var transport = p.Transport.ToLowerInvariant();
        var stream = new JsonObject { ["network"] = transport, ["security"] = p.TlsEnabled || transport == "hysteria2" ? "tls" : "none" };
        var field = transport switch
        {
            "tcp" => "tcpSettings", "ws" => "wsSettings", "grpc" => "grpcSettings", "http" => "httpSettings",
            "httpupgrade" => "httpupgradeSettings", "mkcp" => "kcpSettings", "quic" => "quicSettings", "hysteria2" => "hy2steriaSettings",
            _ => throw new ArgumentException("Unsupported V4 transport: " + transport)
        };
        stream[field] = transport switch
        {
            "tcp" => BuildTcpSettings(p.TcpHeaderJson),
            "ws" => new JsonObject { ["path"] = p.WebSocketPath, ["headers"] = V5ConfigBuilder.ParseObject(p.WebSocketHeadersJson, "WebSocket headers") },
            "grpc" => new JsonObject { ["serviceName"] = p.GrpcServiceName },
            "http" => new JsonObject { ["path"] = p.Http2Path, ["host"] = Strings(p.Http2Hosts) },
            "httpupgrade" => new JsonObject { ["path"] = p.Http2Path, ["host"] = p.Http2Hosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "" },
            "mkcp" => new JsonObject { ["mtu"] = p.KcpMtu, ["tti"] = p.KcpTti, ["uplinkCapacity"] = p.KcpUplinkCapacity,
                ["downlinkCapacity"] = p.KcpDownlinkCapacity, ["readBufferSize"] = p.KcpReadBufferSize, ["writeBufferSize"] = p.KcpWriteBufferSize,
                ["congestion"] = p.KcpCongestion, ["header"] = new JsonObject { ["type"] = p.KcpHeader } },
            "quic" => new JsonObject { ["security"] = p.QuicSecurity, ["key"] = p.QuicKey, ["header"] = new JsonObject { ["type"] = p.QuicHeader } },
            "hysteria2" => V5ConfigBuilder.BuildHy2Transport(p),
            _ => throw new ArgumentException("Unsupported transport: " + transport)
        };
        if (p.TcpFastOpen) stream["sockopt"] = new JsonObject { ["tcpFastOpen"] = true };
        if (p.TlsEnabled || transport == "hysteria2")
        {
            var tls = new JsonObject();
            if (!string.IsNullOrWhiteSpace(p.ServerName)) tls["serverName"] = p.ServerName.Trim();
            var alpn = Strings(p.Alpn);
            if (alpn.Count > 0) tls["alpn"] = alpn;
            // The legacy V4 JSON schema explicitly documents tlsSettings.allowInsecure.
            // It is an explicit user opt-in, never a default, and cannot be used with V5.
            if (p.AllowInsecureTls) tls["allowInsecure"] = true;
            if (!string.IsNullOrWhiteSpace(p.PinnedCertificateSha256))
            {
                tls["pinnedPeerCertificateChainSha256"] = new JsonArray(p.PinnedCertificateSha256.Trim());
                tls["allowInsecureIfPinnedPeerCertificate"] = true;
            }
            stream["tlsSettings"] = tls;
        }
        var result = new JsonObject { ["tag"] = tag, ["protocol"] = protocol, ["settings"] = settings, ["streamSettings"] = stream };
        if (p.EnableMux && transport != "hysteria2") result["mux"] = new JsonObject { ["enabled"] = true, ["concurrency"] = p.MuxConcurrency };
        return result;
    }

    // An unmodified TCP connection needs no header configuration. In particular,
    // emitting `"header": {}` makes the V4 core loader fail with
    // "type not found in JSON context". Keep custom HTTP header disguises intact.
    private static JsonObject BuildTcpSettings(string? json)
    {
        var header = V5ConfigBuilder.ParseTcpHeader(json);
        return header.Count == 0
            ? new JsonObject()
            : new JsonObject { ["header"] = header };
    }

    private static JsonObject BuildServer(ServerProfile p) => new()
    {
        ["address"] = p.Address, ["port"] = p.Port, ["password"] = p.Password
    };

    private static JsonArray Strings(string value) => new(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

    private static string NormalizeVmessSecurity(string value) => string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim().ToLowerInvariant() switch
    {
        "auto" or "aes-128-gcm" or "chacha20-poly1305" or "none" or "zero" => value.Trim().ToLowerInvariant(),
        _ => throw new ArgumentException("Unsupported VMess encryption: " + value)
    };

    // Public preview deliberately describes the chosen routing policy, not the
    // Global runtime override. The builder passes the effective mode separately.
    public static JsonObject BuildRouting(ClientSettings options) => BuildRouting(options, options.RoutingMode);

    private static JsonObject BuildRouting(ClientSettings options, string routingMode)
    {
        JsonObject routing = routingMode switch
        {
            "Smart" => SmartRoutingPolicy.BuildV4(options),
            "ProxyAll" => new JsonObject { ["domainStrategy"] = "AsIs", ["rules"] = new JsonArray() },
            "Direct" => new JsonObject { ["domainStrategy"] = "AsIs", ["rules"] = new JsonArray(
                new JsonObject { ["type"] = "field", ["network"] = "tcp,udp", ["outboundTag"] = "direct" }) },
            "BypassCN" => new JsonObject { ["domainStrategy"] = "IPIfNonMatch", ["rules"] = new JsonArray(
                new JsonObject { ["type"] = "field", ["domain"] = new JsonArray("localhost", "geosite:cn"), ["outboundTag"] = "direct" },
                new JsonObject { ["type"] = "field", ["ip"] = new JsonArray($"ext:{GeoDataAssets.NormalizeGeoIpFile(options.GeoIpFile)}:private", $"ext:{GeoDataAssets.NormalizeGeoIpFile(options.GeoIpFile)}:cn"), ["outboundTag"] = "direct" }) },
            "Custom" => ParseCustomRouting(options),
            _ => throw new ArgumentException("Unknown routing mode.")
        };
        var rules = routing["rules"] as JsonArray ?? new JsonArray();
        routing["rules"] = rules;
        foreach (var rule in rules.OfType<JsonObject>())
        {
            if ((string?)rule["outboundTag"] == "main")
            {
                rule.Remove("outboundTag");
                rule[options.BalanceAllNodes && options.Profiles.Count > 1 ? "balancerTag" : "outboundTag"] =
                    options.BalanceAllNodes && options.Profiles.Count > 1 ? "balance" : "proxy";
            }
            if ((string?)rule["outboundTag"] == "decline") rule["outboundTag"] = "block";
        }
        if (options.BalanceAllNodes && options.Profiles.Count > 1)
        {
            routing["balancers"] = new JsonArray(new JsonObject
            {
                ["tag"] = "balance",
                ["selector"] = new JsonArray(options.Profiles.Select(p => (JsonNode?)JsonValue.Create("node-" + p.Id)).ToArray()),
                ["strategy"] = new JsonObject { ["type"] = "random" }
            });
            if (routingMode == "Smart")
            {
                // Keep the first-outbound fallback (direct or selected proxy) intact.
                // Explicit proxied PAC domains/IPs can use the balancer. Unmatched
                // proxy-fallback traffic uses the first selected node when balanced;
                // a catch-all balancer rule would bypass IPIfNonMatch GeoIP matching.
                foreach (var rule in rules.OfType<JsonObject>())
                {
                    if ((string?)rule["outboundTag"] != "proxy") continue;
                    rule.Remove("outboundTag");
                    rule["balancerTag"] = "balance";
                }
            }
            else
                rules.Add(new JsonObject { ["type"] = "field", ["network"] = "tcp,udp", ["balancerTag"] = "balance" });
        }
        // Highest priority: an explicitly configured proxy listener must not fall
        // through to Smart/PAC/Custom direct rules. PAC's own http-in is excluded.
        if (ExplicitProxyPolicy.NeedsForcedRule(routingMode))
            rules.Insert(0, ExplicitProxyPolicy.V4Rule(
                options.BalanceAllNodes && options.Profiles.Count > 1));
        return routing;
    }

    private static JsonObject ParseCustomRouting(ClientSettings options)
    {
        var routing = V5ConfigBuilder.ParseObject(CustomRoutingDeployment.ResolveJson(options), "custom routing");
        if (routing["rules"] is JsonArray) return (JsonObject)routing.DeepClone();
        // V5-only rules have different matcher shapes; do not silently emit an invalid V4 config.
        throw new NotSupportedException("This routing rule set uses V5 'rule'. Select a V4 'rules' rule set or convert it explicitly.");
    }
}
