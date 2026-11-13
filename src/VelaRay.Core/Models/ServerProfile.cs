using System.ComponentModel;
using System.Text.Json.Serialization;

namespace VelaRay.Core.Models;

/// <summary>Portable, UI-facing model. Platform services and native window code never live here.</summary>
public sealed class ServerProfile : INotifyPropertyChanged
{
    // UI-only: reflects a profile included in the currently running core configuration.
    // Never persist runtime connection state as a user setting.
    private bool _isConnected;
    public event PropertyChangedEventHandler? PropertyChanged;
    [JsonIgnore]
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value) return;
            _isConnected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
        }
    }

    // UI-only card presentation data. Latency is an on-demand TCP connection test,
    // not a V2Fly transport RTT; it is never persisted or filled with fake values.
    private long? _tcpLatencyMs;
    [JsonIgnore]
    public long? TcpLatencyMs
    {
        get => _tcpLatencyMs;
        set
        {
            if (_tcpLatencyMs == value) return;
            _tcpLatencyMs = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TcpLatencyMs)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LatencyDisplay)));
        }
    }

    [JsonIgnore]
    public string ProtocolBadge => string.IsNullOrWhiteSpace(Protocol) ? "—" : Protocol.Trim().ToLowerInvariant();

    [JsonIgnore]
    public string EndpointDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Address)) return "—";
            var host = Address.Trim();
            if (host.Contains(':') && !host.StartsWith('[')) host = $"[{host}]";
            return $"{host}:{Port}";
        }
    }

    [JsonIgnore]
    public string LatencyDisplay => _tcpLatencyMs is long ms ? $"{ms} ms" : "—";

    /// <summary>Refresh computed card properties after the editable server fields are committed.</summary>
    public void NotifyCardChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProtocolBadge)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EndpointDisplay)));
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New server";
    /// <summary>Preferred V2Fly JSON loader for this node. V4 or V5, independently of other nodes.</summary>
    public string ConfigVersion { get; set; } = "V5";
    public string Protocol { get; set; } = "hysteria2";
    public string Address { get; set; } = "";
    public int Port { get; set; } = 443;
    public string Uuid { get; set; } = "";
    public string Password { get; set; } = "";
    public int VmessAlterId { get; set; } // V4 legacy servers only; V5 has no alterId.
    public string VmessSecurity { get; set; } = "auto";
    public string Transport { get; set; } = "hysteria2";
    public string TransportPassword { get; set; } = "";
    public bool TlsEnabled { get; set; } = true;
    public string ServerName { get; set; } = "";
    public string Alpn { get; set; } = "";
    public bool AllowInsecureTls { get; set; } // Explicit opt-in; do not enable by default. // comma separated
    public string PinnedCertificateSha256 { get; set; } = "";
    public string Congestion { get; set; } = "bbr";
    public int UploadMbps { get; set; }
    public int DownloadMbps { get; set; }
    public bool UseUdpExtension { get; set; } = true;
    public string WebSocketPath { get; set; } = "/";
    public string WebSocketHeadersJson { get; set; } = "{}";
    public string Http2Hosts { get; set; } = "";
    public string Http2Path { get; set; } = "/";
    public string GrpcServiceName { get; set; } = "";
    public string TcpHeaderJson { get; set; } = "{\"type\":\"none\"}";
    public bool TcpFastOpen { get; set; }
    public int KcpMtu { get; set; } = 1350;
    public int KcpTti { get; set; } = 50;
    public int KcpUplinkCapacity { get; set; } = 5;
    public int KcpDownlinkCapacity { get; set; } = 20;
    public int KcpReadBufferSize { get; set; } = 2;
    public int KcpWriteBufferSize { get; set; } = 2;
    public bool KcpCongestion { get; set; } = true;
    public string KcpHeader { get; set; } = "none";
    public string QuicSecurity { get; set; } = "none";
    public string QuicKey { get; set; } = "";
    public string QuicHeader { get; set; } = "none";
    public bool EnableMux { get; set; }
    public int MuxConcurrency { get; set; } = 8;
}

public sealed class RoutingRuleSet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom";
    /// <summary>Original V4 routing object or a V5 router object. Converters run per selected output format.</summary>
    public string Json { get; set; } = "{\"domainStrategy\":\"IpIfNonMatch\",\"rule\":[]}";
}

public sealed class ClientSettings
{
    public string Language { get; set; } = "zh-CN"; // UI-only; does not affect the proxy core's configuration.
    public List<ServerProfile> Profiles { get; set; } = [];
    public string? SelectedProfileId { get; set; }
    public int SocksPort { get; set; } = 1080;
    public int HttpPort { get; set; } = 1088;
    public bool AllowLan { get; set; }
    public bool EnableUdp { get; set; } = true;
    public bool Sniffing { get; set; } = true;
    public string ProxyMode { get; set; } = "Manual";
    public string RoutingMode { get; set; } = "Smart"; // Smart/ProxyAll/Direct/BypassCN/Custom
    /// <summary>Smart mode only: unmatched V2Fly traffic uses Direct (legacy) or Proxy.
    /// Defaults to Direct for existing settings without this property. Changes apply on reconnect.</summary>
    public string SmartUnmatchedRoute { get; set; } = "Direct";
    /// <summary>GeoIP database selected for BypassCN routing (full or CN+private only).</summary>
    public string GeoIpFile { get; set; } = "geoip-only-cn-private.dat";
    public List<RoutingRuleSet> RoutingRuleSets { get; set; } = [];
    /// <summary>Rule-set ID explicitly selected on Proxy settings; independent of the rule editor.</summary>
    public string? SelectedRoutingRuleId { get; set; }
    /// <summary>Immutable selected JSON snapshot used by the core until the user applies edits from Proxy settings.</summary>
    public string? AppliedRoutingRuleJson { get; set; }
    /// <summary>Last rule set opened in Routing & PAC; browsing/editing does not select it for the core.</summary>
    public string? EditingRoutingRuleId { get; set; }
    public bool BalanceAllNodes { get; set; }
    public int PacPort { get; set; } = 18888;
    // Managed PAC generator settings; existing hand-edited PAC files are preserved.
    public string SelectedPacFile { get; set; } = "managed.pac";
    public string PacTemplate { get; set; } = "DirectByDefault";
    public string PacDirectWebsites { get; set; } = "";
    public string PacProxyWebsites { get; set; } = "";
    public string DnsServers { get; set; } = "";
    public string LogLevel { get; set; } = "Warning";
    public bool RestoreSystemProxy { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool ConnectAtStartup { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public string? CustomConfigPath { get; set; }
    public bool UseCustomConfig { get; set; }
    public string? CorePathOverride { get; set; }
    /// <summary>Prerelease cores are opt-in; stable releases are the default.</summary>
    public bool IncludePrereleaseCoreUpdates { get; set; }
}
