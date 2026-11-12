using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using VelaRay.Core.Configuration;

namespace VelaRay.Core.Runtime;

/// <summary>
/// VelaRay-owned, local-only stats extension for generated V2Fly configurations.
/// Never modify user-supplied external JSON. No statistics counters are written to disk.
/// </summary>
public static class CoreTrafficStatistics
{
    public const string ApiTag = "velaray-stats-api";
    public const string InboundTag = "velaray-stats-in";

    /// <summary>Select an available loopback port; V2Fly still performs the authoritative bind.</summary>
    public static int FindLoopbackPort(params int[] excluded)
    {
        for (var i = 0; i < 10; i++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                if (!excluded.Contains(port)) return port;
            }
            finally { listener.Stop(); }
        }
        throw new IOException("Cannot reserve a loopback port for V2Fly traffic statistics.");
    }

    /// <summary>
    /// Isolated extension of the temporary ACTIVE config. Generated baseline remains
    /// byte-for-byte unchanged for PAC/router pending-change detection.
    /// </summary>
    public static string Enable(string source, string format, int apiPort)
    {
        if (apiPort is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(apiPort));
        var root = JsonNode.Parse(source) as JsonObject
            ?? throw new ArgumentException("Statistics require a JSON config object.", nameof(source));
        var inbounds = root["inbounds"] as JsonArray
            ?? throw new ArgumentException("Config lacks inbounds.", nameof(source));
        if (inbounds.Any(i => string.Equals((string?)i?["tag"], InboundTag, StringComparison.Ordinal)))
            throw new ArgumentException("Reserved statistics inbound tag is in use.", nameof(source));

        if (ConfigFlavor.Normalize(format) == ConfigFlavor.V4)
        {
            root["stats"] = new JsonObject();
            root["api"] = new JsonObject
            {
                ["tag"] = ApiTag,
                ["services"] = new JsonArray("StatsService")
            };
            root["policy"] = new JsonObject
            {
                ["system"] = new JsonObject
                {
                    ["statsOutboundUplink"] = true,
                    ["statsOutboundDownlink"] = true
                }
            };
            inbounds.Add(new JsonObject
            {
                ["tag"] = InboundTag, ["listen"] = "127.0.0.1", ["port"] = apiPort,
                ["protocol"] = "dokodemo-door",
                ["settings"] = new JsonObject { ["address"] = "127.0.0.1" }
            });
            var routing = root["routing"] as JsonObject
                ?? throw new ArgumentException("V4 config lacks routing.", nameof(source));
            var rules = routing["rules"] as JsonArray
                ?? throw new ArgumentException("V4 config lacks routing rules.", nameof(source));
            rules.Insert(0, new JsonObject
            {
                ["type"] = "field", ["inboundTag"] = new JsonArray(InboundTag),
                ["outboundTag"] = ApiTag
            });
        }
        else
        {
            root["services"] = new JsonObject
            {
                ["stats"] = new JsonObject(),
                ["policy"] = new JsonObject
                {
                    ["system"] = new JsonObject
                    {
                        ["stats"] = new JsonObject
                        {
                            ["outboundUplink"] = true, ["outboundDownlink"] = true
                        }
                    }
                },
                ["commander"] = new JsonObject
                {
                    ["tag"] = ApiTag, ["name"] = new JsonArray("StatsService")
                }
            };
            inbounds.Add(new JsonObject
            {
                ["tag"] = InboundTag, ["listen"] = "127.0.0.1", ["port"] = apiPort,
                ["protocol"] = "dokodemo-door",
                ["settings"] = new JsonObject { ["address"] = "127.0.0.1", ["port"] = 65535,
                    ["networks"] = new JsonArray("tcp") }
            });
            var router = root["router"] as JsonObject
                ?? throw new ArgumentException("V5 config lacks router.", nameof(source));
            var rules = router["rule"] as JsonArray
                ?? throw new ArgumentException("V5 config lacks router rules.", nameof(source));
            rules.Insert(0, new JsonObject
            {
                ["tag"] = ApiTag, ["inboundTag"] = new JsonArray(InboundTag)
            });
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Reads V2Fly's -json protobuf statistics response. Only proxy OUTBOUNDS are
    /// summed: do not misrepresent 'direct' traffic as remote proxy usage.
    /// Values may be JSON integers or quoted protobuf int64 strings.
    /// </summary>
    public static TrafficCounters Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        JsonElement stats;
        if (root.ValueKind == JsonValueKind.Array) stats = root;
        else if (!root.TryGetProperty("stat", out stats) && !root.TryGetProperty("stats", out stats))
            throw new FormatException("V2Fly statistics JSON has no 'stat' array.");
        if (stats.ValueKind != JsonValueKind.Array)
            throw new FormatException("V2Fly statistics counters are not an array.");
        long up = 0, down = 0;
        foreach (var counter in stats.EnumerateArray())
        {
            if (!counter.TryGetProperty("name", out var nameValue) ||
                !counter.TryGetProperty("value", out var value)) continue;
            var name = nameValue.GetString();
            if (name is null || !name.StartsWith("outbound>>>", StringComparison.Ordinal)) continue;
            var parts = name.Split(">>>", StringSplitOptions.None);
            if (parts.Length != 4 || parts[2] != "traffic") continue;
            // VelaRay generated core configs use proxy, node-<id> or unique profile
            // name; direct, block and stats API are not proxy transfer traffic.
            if (parts[1] is "direct" or "block" or ApiTag) continue;
            var bytes = value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt64(out var numberValue) => numberValue,
                JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var stringValue) => stringValue,
                _ => -1
            };
            if (bytes < 0) continue;
            try
            {
                if (parts[3] == "uplink") up = checked(up + bytes);
                else if (parts[3] == "downlink") down = checked(down + bytes);
            }
            catch (OverflowException) { throw new FormatException("V2Fly traffic counter overflow."); }
        }
        return new TrafficCounters(up, down);
    }

    /// <summary>Uses v2ray's official command, no third-party gRPC packages.</summary>
    public static async Task<TrafficCounters> QueryAsync(string executable, int port, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        };
        // Command flags precede positional arguments; never use -reset.
        foreach (var arg in new[] { "api", "stats", "-json", "-s", $"127.0.0.1:{port}", "-t", "2" })
            start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("Cannot launch V2Fly statistics query.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var result = await stdout;
            var errors = await stderr;
            if (process.ExitCode != 0)
                throw new IOException("V2Fly statistics unavailable: " + (errors.Length > 200 ? errors[..200] : errors));
            if (result.Length > 1_000_000) throw new FormatException("V2Fly statistics output exceeded 1 MB.");
            return Parse(result);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
    }
}

public readonly record struct TrafficCounters(long UplinkBytes, long DownlinkBytes);
public readonly record struct TrafficSnapshot(long UplinkBytes, long DownlinkBytes,
    double UplinkBytesPerSecond, double DownlinkBytesPerSecond);

/// <summary>Monotonic time deltas; counters reset with each new core process.</summary>
public sealed class TrafficRateCalculator
{
    private TrafficCounters? _previous;
    private long _lastTimestamp;

    public TrafficSnapshot Update(TrafficCounters counters, long timestamp)
    {
        double upRate = 0, downRate = 0;
        if (_previous is { } old && timestamp > _lastTimestamp)
        {
            var seconds = Stopwatch.GetElapsedTime(_lastTimestamp, timestamp).TotalSeconds;
            if (seconds > 0)
            {
                upRate = Math.Max(0L, counters.UplinkBytes - old.UplinkBytes) / seconds;
                downRate = Math.Max(0L, counters.DownlinkBytes - old.DownlinkBytes) / seconds;
            }
        }
        _previous = counters;
        _lastTimestamp = timestamp;
        return new TrafficSnapshot(counters.UplinkBytes, counters.DownlinkBytes, upRate, downRate);
    }
}
