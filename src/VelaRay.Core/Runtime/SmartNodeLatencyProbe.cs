using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using VelaRay.Core.Configuration;
using VelaRay.Core.Models;

namespace VelaRay.Core.Runtime;

/// <summary>
/// Explicit, isolated node health/latency probe. Never touches the active V2Ray
/// process, Windows system proxy, PAC, or saved client configuration.
/// TCP: socket connect time (not a proxy handshake).
/// UDP transports: time to receive an HTTPS 204 response THROUGH an isolated
/// per-node V2Fly outbound. A raw UDP send without an application response is
/// deliberately not considered successful.
/// </summary>
public sealed class SmartNodeLatencyProbe
{
    public const int MaxConcurrentProbes = 2;
    public const int MaxUdpAttempts = 2;
    public static readonly TimeSpan TcpTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan UdpRequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly Uri ProbeUri = new("https://www.gstatic.com/generate_204");

    private readonly IChildProcessLifetime _processLifetime;

    public SmartNodeLatencyProbe(IChildProcessLifetime processLifetime) =>
        _processLifetime = processLifetime ?? throw new ArgumentNullException(nameof(processLifetime));

    public static NodeProbeTransport Classify(ServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var transport = profile.Transport?.Trim().ToLowerInvariant();
        if (string.Equals(profile.Protocol, "hysteria2", StringComparison.OrdinalIgnoreCase) ||
            transport is "mkcp" or "quic" or "hysteria2") return NodeProbeTransport.Udp;
        return transport is "tcp" or "ws" or "http" or "httpupgrade" or "grpc"
            ? NodeProbeTransport.Tcp
            : NodeProbeTransport.Unsupported;
    }

    public async Task<NodeProbeResult> ProbeAsync(ServerProfile profile, string executable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var transport = Classify(profile);
        if (transport == NodeProbeTransport.Unsupported)
            return new(transport, NodeProbeOutcome.Unsupported);
        if (string.IsNullOrWhiteSpace(profile.Address) || profile.Port is < 1 or > 65535)
            return new(transport, NodeProbeOutcome.InvalidConfiguration);

        if (transport == NodeProbeTransport.Tcp)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TcpTimeout);
            try
            {
                using var socket = new TcpClient();
                var timer = Stopwatch.StartNew();
                await socket.ConnectAsync(profile.Address.Trim(), profile.Port, timeout.Token);
                timer.Stop();
                return new(transport, NodeProbeOutcome.Success, Math.Max(1, timer.ElapsedMilliseconds));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(transport, NodeProbeOutcome.Timeout);
            }
            catch (SocketException) { return new(transport, NodeProbeOutcome.Unavailable); }
            catch (ArgumentException) { return new(transport, NodeProbeOutcome.InvalidConfiguration); }
        }

        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            return new(transport, NodeProbeOutcome.CoreUnavailable);

        // Validate before creating any process or temporary credential-containing file.
        try
        {
            V5ConfigBuilder.Validate(new ClientSettings { Profiles = [profile], RoutingMode = "ProxyAll" }, profile);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or JsonException)
        {
            return new(transport, NodeProbeOutcome.InvalidConfiguration);
        }

        try { return await ProbeUdpTransportAsync(profile, executable, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(transport, NodeProbeOutcome.Timeout);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or HttpRequestException or SocketException)
        {
            // Never expose credentials, generated JSON or process command lines in the UI.
            return new(transport, NodeProbeOutcome.Unavailable);
        }
    }

    /// <summary>
    /// The probe has exactly ONE outbound, and that outbound is the tested
    /// server. No freedom/direct fallback or user custom routing is present.
    /// The inbound is loopback-only and cannot change the Windows system proxy.
    /// </summary>
    public static string BuildIsolatedConfig(ServerProfile profile, int httpPort)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (httpPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(httpPort));
        var v4 = ConfigFlavor.Normalize(profile.ConfigVersion) == ConfigFlavor.V4;
        var inbound = new JsonObject
        {
            ["tag"] = "latency-probe-in",
            ["protocol"] = "http",
            ["listen"] = "127.0.0.1",
            ["port"] = httpPort,
            ["settings"] = new JsonObject()
        };
        var root = new JsonObject
        {
            ["log"] = v4
                ? new JsonObject { ["loglevel"] = "warning" }
                : new JsonObject {
                    ["access"] = new JsonObject { ["type"] = "None" },
                    ["error"] = new JsonObject { ["type"] = "Console", ["level"] = "Warning" }
                },
            ["inbounds"] = new JsonArray(inbound),
            ["outbounds"] = new JsonArray(v4
                ? V4ConfigBuilder.BuildOutbound(profile, "latency-probe-out")
                : V5ConfigBuilder.BuildOutbound(profile, "latency-probe-out"))
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task<NodeProbeResult> ProbeUdpTransportAsync(ServerProfile profile, string executable,
        CancellationToken cancellationToken)
    {
        var port = ReserveLoopbackPort();
        var configFile = Path.Combine(Path.GetTempPath(), $"velaray-probe-{Guid.NewGuid():N}.json");
        Process? process = null;
        try
        {
            await File.WriteAllTextAsync(configFile, BuildIsolatedConfig(profile, port), cancellationToken);
            var start = new ProcessStartInfo(Path.GetFullPath(executable))
            {
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in V2RayCoreRunner.BuildConfigArguments("run", configFile, profile.ConfigVersion))
                start.ArgumentList.Add(argument);
            start.Environment["V2RAY_LOCATION_ASSET"] = start.WorkingDirectory;
            process = new Process { StartInfo = start };
            if (!process.Start()) return new(NodeProbeTransport.Udp, NodeProbeOutcome.CoreUnavailable);
            try { _processLifetime.Attach(process); }
            catch
            {
                // Fail closed if we cannot put the child in the kill-on-close job.
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            // Drain both pipes: V2Fly must not deadlock if it logs during a test.
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var ready = await WaitForHttpProxyAsync(process, port, cancellationToken);
            if (!ready) return new(NodeProbeTransport.Udp,
                process.HasExited ? NodeProbeOutcome.InvalidConfiguration : NodeProbeOutcome.CoreUnavailable);

            NodeProbeOutcome failure = NodeProbeOutcome.Unavailable;
            for (var attempt = 0; attempt < MaxUdpAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var handler = new HttpClientHandler
                {
                    UseProxy = true,
                    Proxy = new WebProxy(new Uri($"http://127.0.0.1:{port}")) { BypassProxyOnLocal = false },
                    AllowAutoRedirect = false,
                    UseCookies = false
                };
                using var client = new HttpClient(handler, disposeHandler: true);
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeout.CancelAfter(UdpRequestTimeout);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUri);
                    var watch = Stopwatch.StartNew();
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                        attemptTimeout.Token);
                    watch.Stop();
                    // Only an actual response from the expected endpoint is success.
                    // A captive portal or proxy-generated error cannot look like RTT.
                    if (response.StatusCode == HttpStatusCode.NoContent)
                        return new(NodeProbeTransport.Udp, NodeProbeOutcome.Success, Math.Max(1, watch.ElapsedMilliseconds));
                    failure = NodeProbeOutcome.Unavailable;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    failure = NodeProbeOutcome.Timeout;
                }
                catch (HttpRequestException)
                {
                    failure = NodeProbeOutcome.Unavailable;
                }
                if (process.HasExited) break;
            }
            return new(NodeProbeTransport.Udp, failure);
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                catch (Exception cleanupError) when (cleanupError is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                finally { process.Dispose(); }
            }
            try { File.Delete(configFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static async Task<bool> WaitForHttpProxyAsync(Process process, int port, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited) return false;
            try
            {
                using var probe = new TcpClient();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(350));
                await probe.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                return true;
            }
            catch (SocketException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }
}

public enum NodeProbeTransport { Tcp, Udp, Unsupported }
public enum NodeProbeOutcome { Success, Timeout, Unavailable, InvalidConfiguration, CoreUnavailable, Unsupported }
public sealed record NodeProbeResult(NodeProbeTransport Transport, NodeProbeOutcome Outcome, long? Milliseconds = null);
