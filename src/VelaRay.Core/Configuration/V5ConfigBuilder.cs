using System.Text.Json;
using System.Text.Json.Nodes;
using VelaRay.Core.Models;

namespace VelaRay.Core.Configuration;

/// <summary>V2Fly jsonv5 generator, isolated from the legacy V4 generator.</summary>
public static class V5ConfigBuilder
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string Build(ClientSettings options, ServerProfile selected)
    {
        Validate(options, selected);
        var listen = options.AllowLan ? "0.0.0.0" : "127.0.0.1";
        var useBalance = options.BalanceAllNodes && options.Profiles.Count > 1;
        var outbounds = new JsonArray();
        var directOutbound = new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom", ["settings"] = new JsonObject() };
        // Both V2Fly jsonv5 and V4 select the FIRST outbound if no routing rule matches.
        if (options.RoutingMode == "Smart" && !SmartRoutingPolicy.ProxyUnmatched(options))
            outbounds.Add(directOutbound);
        foreach (var (profile, tag) in OutboundPlanner.Get(options, selected))
        {
            OutboundPlanner.ValidateReferenced(options, profile, tag);
            outbounds.Add(BuildOutbound(profile, tag));
        }
        if (options.RoutingMode != "Smart" || SmartRoutingPolicy.ProxyUnmatched(options))
            outbounds.Add(directOutbound);
        outbounds.Add(new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole", ["settings"] = new JsonObject() });
        var config = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["access"] = new JsonObject { ["type"] = "None" },
                ["error"] = new JsonObject { ["type"] = "Console", ["level"] = options.LogLevel }
            },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "socks-in", ["protocol"] = "socks", ["listen"] = listen, ["port"] = options.SocksPort,
                    ["settings"] = new JsonObject { ["udpEnabled"] = options.EnableUdp, ["address"] = listen, ["packetEncoding"] = "Packet" },
                    ["sniffing"] = BuildSniffing(options)
                },
                new JsonObject
                {
                    ["tag"] = "http-in", ["protocol"] = "http", ["listen"] = listen, ["port"] = options.HttpPort,
                    ["settings"] = new JsonObject(), ["sniffing"] = BuildSniffing(options)
                }
            },
            ["outbounds"] = outbounds
        };
        var dns = options.DnsServers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (dns.Length > 0)
        {
            // V5 DNS schema uses service.dns with nameServer entries (not V4 dns.servers).
            config["dns"] = new JsonObject { ["nameServer"] = new JsonArray(dns.Select(s => (JsonNode?)new JsonObject { ["address"] = new JsonObject { ["address"] = s } }).ToArray()) };
        }
        config["router"] = BuildRouting(options, useBalance);
        return config.ToJsonString(Pretty);
    }

    private static JsonObject BuildSniffing(ClientSettings settings) => new()
    {
        ["enabled"] = settings.Sniffing,
        ["destOverride"] = new JsonArray("http", "tls"),
        ["metadataOnly"] = false
    };

    /// <summary>Expose the built-in routing portion for the read-only editor preview.
    /// Does not modify ClientSettings or alter production configuration generation.</summary>
    public static JsonObject PreviewBuiltInRouting(ClientSettings options)
    {
        if (options.RoutingMode is not ("Smart" or "ProxyAll" or "Direct" or "BypassCN"))
            throw new ArgumentException("Only built-in routing modes may be previewed.", nameof(options));
        return BuildRouting(options, balanced: false);
    }

    private static JsonObject BuildRouting(ClientSettings options, bool balanced)
    {
        JsonObject router = options.RoutingMode switch
        {
            "Smart" => SmartRoutingPolicy.BuildV5(options),
            "ProxyAll" => new JsonObject { ["domainStrategy"] = "AsIs", ["rule"] = new JsonArray() },
            "Direct" => new JsonObject { ["domainStrategy"] = "AsIs", ["rule"] = new JsonArray(
                new JsonObject { ["tag"] = "direct", ["networks"] = "tcp,udp" }) },
            "BypassCN" => new JsonObject
            {
                ["domainStrategy"] = "IpIfNonMatch",
                ["rule"] = new JsonArray(
                    new JsonObject { ["tag"] = "direct", ["domain"] = new JsonArray(
                        new JsonObject { ["type"] = "Full", ["value"] = "localhost" }),
                        ["geoDomain"] = new JsonArray(new JsonObject { ["filePath"] = "geosite.dat", ["code"] = "cn" }) },
                    new JsonObject { ["tag"] = "direct", ["geoip"] = new JsonArray(
                        new JsonObject { ["filePath"] = GeoDataAssets.NormalizeGeoIpFile(options.GeoIpFile), ["code"] = "private" },
                        new JsonObject { ["filePath"] = GeoDataAssets.NormalizeGeoIpFile(options.GeoIpFile), ["code"] = "cn" }) })
            },
            "Custom" => ConvertRoutingToV5(ParseObject(CustomRoutingDeployment.ResolveJson(options), "custom router")),
            _ => throw new ArgumentException("Unknown routing mode.")
        };
        // V5 config uses router.rule and router.balancingRule (not V4 routing.rules).
        var rules = router["rule"] as JsonArray;
        if (rules is null) { rules = new JsonArray(); router["rule"] = rules; }
        foreach (var rule in rules.OfType<JsonObject>())
        {
            if ((string?)rule["tag"] == "main")
            {
                rule.Remove("tag");
                rule[balanced ? "balancingTag" : "tag"] = balanced ? "balance" : "proxy";
            }
            if ((string?)rule["tag"] == "decline") rule["tag"] = "block";

        }
        if (balanced)
        {
            router["balancingRule"] = new JsonArray(new JsonObject
            {
                ["tag"] = "balance",
                ["outboundSelector"] = new JsonArray(options.Profiles.Select(p => (JsonNode?)JsonValue.Create("node-" + p.Id)).ToArray()),
                ["strategy"] = "random"
            });
            if (options.RoutingMode == "Smart")
            {
                // Balance only explicitly proxied PAC destinations to avoid a catch-all
                // rule that can preempt IpIfNonMatch GeoIP lookups. Unmatched requests
                // use the first outbound (direct, or the first node when Proxy selected).
                foreach (var rule in rules.OfType<JsonObject>())
                {
                    if ((string?)rule["tag"] != "proxy") continue;
                    rule.Remove("tag");
                    rule["balancingTag"] = "balance";
                }
            }
            else
                rules.Add(new JsonObject { ["networks"] = "tcp,udp", ["balancingTag"] = "balance" });
        }
        return router;
    }

    /// <summary>Accepts V5 router JSON and converts common old V4 routing rule sets at the import boundary.</summary>
    public static JsonObject ConvertRoutingToV5(JsonObject source)
    {
        var result = (JsonObject)source.DeepClone();
        if (result["rules"] is JsonArray v4Rules)
        {
            result.Remove("rules");
            result["rule"] = v4Rules.DeepClone();
        }
        if (result["balancers"] is JsonArray v4Balancers)
        {
            result.Remove("balancers");
            var converted = new JsonArray();
            foreach (var b in v4Balancers.OfType<JsonObject>())
                converted.Add(new JsonObject
                {
                    ["tag"] = b["tag"]?.DeepClone(),
                    ["outboundSelector"] = (b["selector"] ?? b["outboundSelector"])?.DeepClone(),
                    ["strategy"] = "random"
                });
            result["balancingRule"] = converted;
        }
        result["domainStrategy"] = ((string?)result["domainStrategy"]) switch
        {
            "IPIfNonMatch" => "IpIfNonMatch", "IPOnDemand" => "IpOnDemand", "IPIfMatch" => "IpOnDemand",
            "UseIP" => "UseIp", var other => other ?? "AsIs"
        };
        if (result["rule"] is not JsonArray rules) return result;
        foreach (var item in rules.OfType<JsonObject>())
        {
            item.Remove("type");
            Move(item, "outboundTag", "tag");
            Move(item, "balancerTag", "balancingTag");
            Move(item, "network", "networks");
            Move(item, "port", "portList");
            Move(item, "sourcePort", "sourcePortList");
            foreach (var property in new[] { "portList", "sourcePortList" })
            {
                if (item[property] is JsonValue v && v.TryGetValue<int>(out var value))
                    item[property] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (item["protocol"] is JsonValue protocol && protocol.TryGetValue<string>(out var names))
                item["protocol"] = new JsonArray(names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
            if (item["domain"] is JsonArray domains)
            {
                var converted = new JsonArray();
                var geos = new JsonArray();
                foreach (var value in domains)
                {
                    if (value is JsonObject obj) { converted.Add(obj.DeepClone()); continue; }
                    var domain = value?.GetValue<string>() ?? "";
                    if (domain.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))
                        geos.Add(new JsonObject { ["filePath"] = "geosite.dat", ["code"] = domain[8..] });
                    else if (domain.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("Legacy external geosite rule needs manual V5 conversion: " + domain);
                    else
                    {
                        var (kind, pattern) = domain.StartsWith("domain:") ? ("RootDomain", domain[7..]) :
                            domain.StartsWith("full:") ? ("Full", domain[5..]) :
                            domain.StartsWith("keyword:") ? ("Plain", domain[8..]) :
                            domain.StartsWith("regexp:") ? ("Regex", domain[7..]) : ("Plain", domain);
                        converted.Add(new JsonObject { ["type"] = kind, ["value"] = pattern });
                    }
                }
                item["domain"] = converted;
                if (geos.Count > 0) item["geoDomain"] = geos;
            }
            if (item["source"] is JsonArray sourceIps)
            {
                item.Remove("source");
                item["sourceGeoip"] = ConvertLegacyIpArray(sourceIps);
            }
            if (item["ip"] is JsonArray ips)
            {
                item.Remove("ip");
                item["geoip"] = ConvertLegacyIpArray(ips);
            }
        }
        return result;
    }

    private static JsonArray ConvertLegacyIpArray(JsonArray ips)
    {
        var converted = new JsonArray();
        foreach (var entry in ips)
        {
            if (entry is JsonObject objectEntry) { converted.Add(objectEntry.DeepClone()); continue; }
            var raw = entry?.GetValue<string>() ?? "";
            if (raw.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
                converted.Add(new JsonObject { ["filePath"] = "geoip.dat", ["code"] = raw[6..] });
            else if (raw.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Legacy external geoip rule needs manual V5 conversion: " + raw);
            else
            {
                var parts = raw.Split('/');
                if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix))
                    throw new NotSupportedException("Unsupported legacy IP route rule: " + raw);
                converted.Add(new JsonObject { ["cidr"] = new JsonArray(new JsonObject { ["ipAddr"] = parts[0], ["prefix"] = prefix }) });
            }
        }
        return converted;
    }

    private static void Move(JsonObject obj, string oldKey, string newKey)
    {
        if (obj[oldKey] is { } node)
        {
            obj.Remove(oldKey);
            if (obj[newKey] is null) obj[newKey] = node.DeepClone();
        }
    }

    public static JsonObject BuildOutbound(ServerProfile p, string tag = "proxy")
    {
        if (p.Protocol == "vmess" && p.VmessAlterId != 0)
            throw new NotSupportedException("JSON V5 only supports VMess AEAD (alterId=0). Select V4, or change alterId to 0 in the V4 editor before switching back to V5.");
        var protocol = p.Protocol.ToLowerInvariant();
        var transport = p.Transport.ToLowerInvariant();
        JsonObject protocolSettings = protocol switch
        {
            "hysteria2" => new JsonObject { ["server"] = new JsonArray(new JsonObject { ["address"] = p.Address, ["port"] = p.Port }) },
            "vmess" => new JsonObject { ["address"] = p.Address, ["port"] = p.Port, ["uuid"] = p.Uuid },
            "trojan" => new JsonObject { ["address"] = p.Address, ["port"] = p.Port, ["password"] = p.Password },
            _ => throw new ArgumentException($"Unsupported protocol: {protocol}")
        };
        JsonObject transportSettings = transport switch
        {
            "hysteria2" => BuildHy2Transport(p),
            "ws" => new JsonObject { ["path"] = p.WebSocketPath, ["headers"] = ParseObject(p.WebSocketHeadersJson, "WebSocket headers") },
            "grpc" => new JsonObject { ["serviceName"] = p.GrpcServiceName },
            "tcp" => new JsonObject { ["header"] = ParseObject(p.TcpHeaderJson, "TCP header") },
            "mkcp" => new JsonObject
            {
                ["mtu"] = p.KcpMtu, ["tti"] = p.KcpTti,
                ["uplinkCapacity"] = p.KcpUplinkCapacity, ["downlinkCapacity"] = p.KcpDownlinkCapacity,
                ["readBufferSize"] = p.KcpReadBufferSize, ["writeBufferSize"] = p.KcpWriteBufferSize,
                ["congestion"] = p.KcpCongestion, ["header"] = new JsonObject { ["type"] = p.KcpHeader }
            },
            "quic" => new JsonObject
            {
                ["security"] = p.QuicSecurity, ["key"] = p.QuicKey, ["header"] = new JsonObject { ["type"] = p.QuicHeader }
            },
            "httpupgrade" => new JsonObject
            {
                ["path"] = p.Http2Path,
                ["host"] = p.Http2Hosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ""
            },
            "http" => throw new NotSupportedException("V2Fly JSON V5 no longer documents legacy HTTP/2 transport. Upgrade the server to HTTPUpgrade/gRPC or use a separately maintained legacy V4 config."),
            _ => throw new ArgumentException($"Unsupported transport: {transport}")
        };
        var tls = p.TlsEnabled || transport == "hysteria2";
        var stream = new JsonObject { ["transport"] = transport, ["transportSettings"] = transportSettings, ["security"] = tls ? "tls" : "none" };
        if (p.TcpFastOpen && transport != "hysteria2")
            stream["socketSettings"] = new JsonObject { ["tcpFastOpen"] = true };
        if (tls)
        {
            var security = new JsonObject();
            if (!string.IsNullOrWhiteSpace(p.ServerName))
                security[transport == "hysteria2" ? "server_name" : "serverName"] = p.ServerName.Trim();
            // Hysteria2 uses HTTP/3 over QUIC. Never inherit an HTTP/1.1 / h2 ALPN
            // override from a TCP profile: it can prevent the QUIC TLS handshake.
            if (transport != "hysteria2")
            {
                var alpns = p.Alpn.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (alpns.Length > 0)
                    security["nextProtocol"] = new JsonArray(alpns.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            }
            // v2ray-core rejects the legacy allowInsecure field. Preserve imported preference as a warning only.
            if (!string.IsNullOrWhiteSpace(p.PinnedCertificateSha256))
            {
                security["pinnedPeerCertificateChainSha256"] = new JsonArray(p.PinnedCertificateSha256.Trim());
                security["allowInsecureIfPinnedPeerCertificate"] = true;
            }
            stream["securitySettings"] = security;
        }
        var result = new JsonObject { ["tag"] = tag, ["protocol"] = protocol, ["settings"] = protocolSettings, ["streamSettings"] = stream };
        if (p.EnableMux && transport != "hysteria2")
            result["mux"] = new JsonObject { ["enabled"] = true, ["concurrency"] = p.MuxConcurrency };
        return result;
    }

    public static JsonObject BuildHy2Transport(ServerProfile p)
    {
        var congestion = new JsonObject { ["type"] = p.Congestion.ToLowerInvariant() };
        if (p.Congestion == "brutal")
        {
            congestion["up_mbps"] = p.UploadMbps;
            congestion["down_mbps"] = p.DownloadMbps;
        }
        return new JsonObject { ["password"] = p.TransportPassword, ["congestion"] = congestion, ["use_udp_extension"] = p.Protocol == "hysteria2" && p.UseUdpExtension };
    }

    public static JsonObject ParseObject(string json, string label)
    {
        try { return JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException($"{label} must be a JSON object."); }
        catch (JsonException ex) { throw new ArgumentException($"Invalid {label}: {ex.Message}", ex); }
    }

    public static void Validate(ClientSettings o, ServerProfile p)
    {
        if (string.IsNullOrWhiteSpace(p.Address)) throw new ArgumentException("Server address is required.");
        if (p.Port is < 1 or > 65535) throw new ArgumentException("Server port must be 1-65535.");
        if (new[] { o.SocksPort, o.HttpPort, o.PacPort }.Any(x => x is < 1 or > 65535) ||
            o.SocksPort == o.HttpPort || o.SocksPort == o.PacPort || o.HttpPort == o.PacPort)
            throw new ArgumentException("SOCKS, HTTP, PAC ports must be distinct values 1-65535.");
        if (p.Protocol is not ("vmess" or "trojan" or "hysteria2")) throw new ArgumentException("Only VMess, Trojan, and Hysteria2 are supported.");
        if (p.Transport is not ("tcp" or "ws" or "http" or "httpupgrade" or "grpc" or "mkcp" or "quic" or "hysteria2")) throw new ArgumentException("Unsupported transport.");
        if (p.Protocol == "hysteria2" && p.Transport != "hysteria2") throw new ArgumentException("Native Hysteria2 requires Hysteria2 transport.");
        if (p.Protocol == "vmess" && !Guid.TryParse(p.Uuid, out _)) throw new ArgumentException($"VMess node '{p.Name}' requires a valid UUID. Native Hysteria2 nodes do not use UUID.");
        if (p.Protocol == "trojan" && string.IsNullOrWhiteSpace(p.Password)) throw new ArgumentException("Trojan password required.");
        if (p.Transport == "hysteria2")
        {
            if (string.IsNullOrWhiteSpace(p.TransportPassword))
                throw new ArgumentException("Hysteria2 transport requires a password.");
            if (!p.TlsEnabled) throw new ArgumentException("Hysteria2 requires TLS 1.3.");
            if (p.Congestion is not ("bbr" or "brutal")) throw new ArgumentException("Unknown Hysteria2 congestion algorithm.");
            if (p.Congestion == "brutal" && (p.UploadMbps <= 0 || p.DownloadMbps <= 0))
                throw new ArgumentException("Brutal requires positive upload/download bandwidth.");
        }
        if (p.Transport == "hysteria2" && !string.IsNullOrEmpty(p.PinnedCertificateSha256))
            throw new NotSupportedException("V2Fly Hysteria2 does not currently support TLS certificate-chain pinning. Use a certificate trusted by the system CA store.");
        if (!string.IsNullOrEmpty(p.PinnedCertificateSha256))
        {
            try { if (Convert.FromBase64String(p.PinnedCertificateSha256).Length != 32) throw new FormatException(); }
            catch (FormatException) { throw new ArgumentException("Certificate chain SHA256 pin must be base64 of 32 bytes."); }
        }
        if (p.Protocol == "vmess" && p.VmessAlterId is < 0 or > 65535)
            throw new ArgumentException("VMess alterId must be between 0 and 65535 (0 enables AEAD).");

        if (p.EnableMux && p.MuxConcurrency < 1) throw new ArgumentException("Mux concurrency must be positive.");
        if (p.Transport == "ws") ParseObject(p.WebSocketHeadersJson, "WebSocket headers");
        if (p.Transport == "tcp") ParseObject(p.TcpHeaderJson, "TCP header");
        if (p.Transport == "mkcp" && (p.KcpMtu < 576 || p.KcpMtu > 1460 || p.KcpTti < 10 || p.KcpTti > 100))
            throw new ArgumentException("mKCP MTU must be 576-1460 and TTI 10-100.");
        if (o.RoutingMode == "Custom" && !CustomRoutingDeployment.HasAppliedRule(o))
            throw new ArgumentException("Select a custom routing rule set.");
    }
}
