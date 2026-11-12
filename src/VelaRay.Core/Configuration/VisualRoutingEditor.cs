using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VelaRay.Core.Configuration;

/// <summary>A deliberately small, round-trip-safe editor for V2Fly router rules.
/// It edits only visible fields and preserves unrelated advanced fields verbatim.
/// New rule sets use V4 routing JSON because the existing generators can produce
/// both V4 and V5 from it. Existing V5 router JSON stays V5 on editing.</summary>
public static class VisualRoutingEditor
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    public sealed class Rule
    {
        public string Target { get; set; } = "proxy";
        public string Domains { get; set; } = "";
        public string Ips { get; set; } = "";
        public string Ports { get; set; } = "";
        public string Network { get; set; } = "tcp,udp";
    }

    public static string DefaultJson => "{\"domainStrategy\":\"IPIfNonMatch\",\"rules\":[]}";

    public static bool IsV5(JsonObject root) => root.ContainsKey("rule") && !root.ContainsKey("rules");

    private static (JsonObject Root, JsonArray Rules, bool V5) Open(string json)
    {
        var root = V5ConfigBuilder.ParseObject(json, "routing rule set");
        if (root.ContainsKey("rule") && root.ContainsKey("rules"))
            throw new ArgumentException("A rule set cannot contain both 'rules' (V4) and 'rule' (V5).");
        var v5 = IsV5(root);
        var key = v5 ? "rule" : "rules";
        if (root[key] is not JsonArray array)
            throw new ArgumentException($"The router JSON must contain a '{key}' array.");
        return (root, array, v5);
    }

    public static int Count(string json) => Open(json).Rules.Count;
    public static string Format(string json) => Open(json).V5 ? "V5" : "V4";
    public static string Strategy(string json) => (string?)Open(json).Root["domainStrategy"] ?? "AsIs";

    public static string SetStrategy(string json, string strategy)
    {
        if (strategy is not ("AsIs" or "IPIfNonMatch" or "IPOnDemand" or "IpIfNonMatch" or "IpOnDemand" or "UseIp"))
            throw new ArgumentException("Unknown routing domain strategy.");
        var (root, _, v5) = Open(json);
        root["domainStrategy"] = v5 ? strategy switch
        {
            "IPIfNonMatch" => "IpIfNonMatch", "IPOnDemand" => "IpOnDemand", _ => strategy
        } : strategy switch
        {
            "IpIfNonMatch" => "IPIfNonMatch", "IpOnDemand" => "IPOnDemand", "UseIp" => "IPOnDemand", _ => strategy
        };
        return root.ToJsonString(Pretty);
    }

    public static Rule Read(string json, int index)
    {
        var (_, rules, v5) = Open(json);
        var item = At(rules, index);
        if (item[v5 ? "balancingTag" : "balancerTag"] is not null)
            throw new NotSupportedException("Balancer routing needs the advanced JSON editor; the visual editor cannot safely change its target.");
        var target = (string?)item[v5 ? "tag" : "outboundTag"] ?? "proxy";
        var domains = new List<string>();
        var ips = new List<string>();
        if (item["domain"] is JsonArray domainItems)
            foreach (var node in domainItems)
                if (v5)
                {
                    if (node is not JsonObject domain) throw new ArgumentException("Unsupported V5 domain rule; use raw JSON.");
                    if (domain.Any(field => field.Key is not ("type" or "value")))
                        throw new NotSupportedException("Advanced domain attributes require raw JSON editing.");
                    var kind = (string?)domain["type"] ?? "Plain";
                    var prefix = kind switch { "RootDomain" => "domain:", "Full" => "full:", "Regex" => "regexp:", "Plain" => "keyword:", _ => throw new ArgumentException("Unsupported V5 domain type; use raw JSON.") };
                    domains.Add(prefix + (string?)domain["value"]);
                }
                else domains.Add(node?.GetValue<string>() ?? "");
        if (v5)
        {
            if (item["geoDomain"] is JsonArray geoDomains)
                foreach (var node in geoDomains.OfType<JsonObject>())
                {
                    var file = (string?)node["filePath"] ?? "geosite.dat";
                    var code = (string?)node["code"] ?? "";
                    if (string.IsNullOrWhiteSpace(code) || node.Any(field => field.Key is not ("filePath" or "code")))
                        throw new NotSupportedException("Complex GeoDomain entries require raw JSON editing.");
                    domains.Add(file == "geosite.dat" ? "geosite:" + code : $"ext:{file}:{code}");
                }
            if (item["geoip"] is JsonArray geoIps)
                foreach (var node in geoIps.OfType<JsonObject>())
                {
                    var code = (string?)node["code"];
                    if (!string.IsNullOrEmpty(code))
                    {
                        var file = (string?)node["filePath"] ?? "geoip.dat";
                        var negation = (bool?)node["inverseMatch"] == true ? "!" : "";
                        ips.Add(file == "geoip.dat" ? $"geoip:{negation}{code}" : $"ext:{file}:{negation}{code}");
                    }
                    if ((string.IsNullOrWhiteSpace(code) && node["cidr"] is null) ||
                        (!string.IsNullOrWhiteSpace(code) && node["cidr"] is JsonArray combined && combined.Count > 0))
                        throw new NotSupportedException("Complex GeoIP entries require raw JSON editing.");
                    if (node["cidr"] is JsonArray blocks)
                        foreach (var b in blocks.OfType<JsonObject>())
                        {
                            var address = (string?)b["ipAddr"];
                            var prefix = (int?)b["prefix"];
                            if (address is not null) ips.Add(prefix is null ? address : $"{address}/{prefix}");
                        }
                }
        }
        else if (item["ip"] is JsonArray ipItems)
            ips.AddRange(ipItems.Select(node => node?.GetValue<string>() ?? ""));
        return new Rule
        {
            Target = target,
            Domains = string.Join("\n", domains),
            Ips = string.Join("\n", ips),
            Ports = item[v5 ? "portList" : "port"]?.ToString() ?? "",
            Network = (string?)(item[v5 ? "networks" : "network"]) ?? "tcp,udp"
        };
    }

    public static IReadOnlyList<string> Summaries(string json)
    {
        var (_, rules, _) = Open(json);
        var descriptions = new List<string>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = Read(json, i);
            var match = rule.Domains.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                ?? rule.Ips.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                ?? (rule.Ports.Length > 0 ? "port:" + rule.Ports : "all traffic");
            descriptions.Add($"{i + 1}. {match}  →  {rule.Target}");
        }
        return descriptions;
    }

    public static string Upsert(string json, int index, Rule values)
    {
        if (values.Target.Length == 0) throw new ArgumentException("Choose an outbound target.");
        if (values.Network is not ("tcp" or "udp" or "tcp,udp")) throw new ArgumentException("Network must be TCP, UDP or both.");
        ValidatePorts(values.Ports);
        var (root, rules, v5) = Open(json);
        if (index < 0 || index > rules.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var item = index == rules.Count ? new JsonObject() : (JsonObject)At(rules, index).DeepClone();
        var targetKey = v5 ? "tag" : "outboundTag";
        item.Remove(v5 ? "balancingTag" : "balancerTag");
        item[targetKey] = values.Target;
        if (!v5) item["type"] = "field";
        item[v5 ? "networks" : "network"] = values.Network;
        item.Remove(v5 ? "portList" : "port");
        if (values.Ports.Trim().Length > 0) item[v5 ? "portList" : "port"] = values.Ports.Trim();

        item.Remove("domain");
        item.Remove("geoDomain");
        item.Remove(v5 ? "geoip" : "ip");
        var domainItems = new JsonArray();
        var geoDomains = new JsonArray();
        foreach (var value in SplitLines(values.Domains))
        {
            if (!v5) { domainItems.Add(value); continue; }
            if (value.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))
                geoDomains.Add(new JsonObject { ["filePath"] = "geosite.dat", ["code"] = value[8..] });
            else if (value.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = value.Split(':', 3);
                if (parts.Length < 3) throw new ArgumentException("Invalid external geosite reference.");
                geoDomains.Add(new JsonObject { ["filePath"] = parts[1], ["code"] = parts[2] });
            }
            else
            {
                var kind = value.StartsWith("domain:") ? "RootDomain" : value.StartsWith("full:") ? "Full" :
                    value.StartsWith("regexp:") ? "Regex" : "Plain";
                var pattern = kind switch { "RootDomain" => value[7..], "Full" => value[5..], "Regex" => value[7..], _ => value.StartsWith("keyword:") ? value[8..] : value };
                domainItems.Add(new JsonObject { ["type"] = kind, ["value"] = pattern });
            }
        }
        if (domainItems.Count > 0) item["domain"] = domainItems;
        if (geoDomains.Count > 0) item["geoDomain"] = geoDomains;

        var ips = new JsonArray();
        foreach (var value in SplitLines(values.Ips))
        {
            if (!v5) { ips.Add(value); continue; }
            if (value.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
            {
                var code = value[6..];
                ips.Add(new JsonObject { ["filePath"] = "geoip.dat", ["code"] = code.TrimStart('!'), ["inverseMatch"] = code.StartsWith('!') });
            }
            else if (value.StartsWith("ext:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("ext-ip:", StringComparison.OrdinalIgnoreCase))
            {
                var marker = value.StartsWith("ext-ip:", StringComparison.OrdinalIgnoreCase) ? 7 : 4;
                var parts = value[marker..].Split(':', 2);
                if (parts.Length != 2) throw new ArgumentException("Invalid external GeoIP reference.");
                ips.Add(new JsonObject { ["filePath"] = parts[0], ["code"] = parts[1].TrimStart('!'), ["inverseMatch"] = parts[1].StartsWith('!') });
            }
            else
            {
                var parts = value.Split('/', 2);
                if (!IPAddress.TryParse(parts[0], out var address)) throw new ArgumentException("Invalid IP/CIDR: " + value);
                var prefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
                if (parts.Length == 2 && !int.TryParse(parts[1], out prefix))
                    throw new ArgumentException("Invalid CIDR prefix: " + value);
                if (prefix < 0 || prefix > (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))
                    throw new ArgumentException("Invalid CIDR prefix: " + value);
                ips.Add(new JsonObject { ["cidr"] = new JsonArray(new JsonObject { ["ipAddr"] = address.ToString(), ["prefix"] = prefix }) });
            }
        }
        if (ips.Count > 0) item[v5 ? "geoip" : "ip"] = ips;

        if (index == rules.Count) rules.Add(item);
        else rules[index] = item;
        return root.ToJsonString(Pretty);
    }

    public static string Remove(string json, int index)
    {
        var (root, rules, _) = Open(json);
        At(rules, index);
        rules.RemoveAt(index);
        return root.ToJsonString(Pretty);
    }

    public static string Move(string json, int index, int delta)
    {
        var (root, rules, _) = Open(json);
        At(rules, index);
        var other = index + delta;
        if (other < 0 || other >= rules.Count) return json;
        var moved = rules[index]!.DeepClone();
        rules.RemoveAt(index);
        rules.Insert(other, moved);
        return root.ToJsonString(Pretty);
    }

    private static JsonObject At(JsonArray rules, int index) => index >= 0 && index < rules.Count
        ? rules[index] as JsonObject ?? throw new ArgumentException("A routing rule must be a JSON object.")
        : throw new ArgumentOutOfRangeException(nameof(index));

    private static IEnumerable<string> SplitLines(string value) => value.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static void ValidatePorts(string ports)
    {
        foreach (var item in ports.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var range = item.Split('-', 2);
            if (range.Length is < 1 or > 2 || range.Any(p => !int.TryParse(p, out var n) || n < 1 || n > 65535) ||
                (range.Length == 2 && int.Parse(range[0]) > int.Parse(range[1])))
                throw new ArgumentException("Invalid port range (use 443 or 1000-2000): " + item);
        }
    }
}
