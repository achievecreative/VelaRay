using System.Text.Json;
using VelaRay.Core.Configuration;
using VelaRay.Core.Models;

namespace VelaRay.Core.Runtime;

/// <summary>
/// The running core owns an immutable launch snapshot. Persisting new PAC or
/// routing settings never changes that snapshot; compare the proposed config to
/// decide whether an explicit reconnection is necessary. Pure PAC changes do not
/// affect the JSON emitted by V2Fly and can be hot-applied to the local PAC server.
/// </summary>
public static class RunningConfigChangeDetector
{
    /// <summary>
    /// True only when changing the Windows proxy mode is the sole effective change
    /// to the generated core configuration. This is the safety gate for automatically
    /// restarting an already running core after the user saves a new proxy mode.
    /// Unapplied routing, node, port and external-JSON edits must not be swept into
    /// an automatic mode transition.
    /// </summary>
    public static bool IsModeOnlyChange(
        ClientSettings current,
        ClientSettings runningSettings,
        string runningJson,
        string? launchedProfileId,
        bool wasExternalConfig,
        string runningProxyMode,
        int runningPacPort)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(runningSettings);
        ArgumentNullException.ThrowIfNull(runningJson);
        if (!SameModeIndependentConfiguration(current, runningSettings)) return false;
        if (wasExternalConfig || current.UseCustomConfig ||
            string.Equals(current.ProxyMode, runningProxyMode, StringComparison.Ordinal) ||
            (runningProxyMode == "PAC" && runningPacPort != current.PacPort))
            return false;

        // Compare with the old mode to detect *other* pending core changes.
        // Clone the model so the UI choice and persisted settings stay untouched.
        try
        {
            var previous = JsonSerializer.Deserialize<ClientSettings>(JsonSerializer.Serialize(current));
            if (previous is null) return false;
            previous.ProxyMode = runningProxyMode;
            var launched = previous.Profiles.FirstOrDefault(p => p.Id == launchedProfileId);
            return launched is not null &&
                string.Equals(ConfigFlavor.Build(previous, launched), runningJson, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // Never auto-restart when the draft is invalid or incomplete.
            return false;
        }
    }

    private static bool SameModeIndependentConfiguration(ClientSettings a, ClientSettings b)
    {
        // Global's effective ProxyAll JSON masks Smart/Custom drafts and PAC rules.
        // Do not silently activate those edits when changing back to PAC/Manual.
        return a.RoutingMode == b.RoutingMode &&
            a.SmartUnmatchedRoute == b.SmartUnmatchedRoute &&
            a.GeoIpFile == b.GeoIpFile &&
            a.SelectedRoutingRuleId == b.SelectedRoutingRuleId &&
            a.AppliedRoutingRuleJson == b.AppliedRoutingRuleJson &&
            a.SelectedPacFile == b.SelectedPacFile &&
            a.PacTemplate == b.PacTemplate &&
            a.PacDirectWebsites == b.PacDirectWebsites &&
            a.PacProxyWebsites == b.PacProxyWebsites &&
            a.DnsServers == b.DnsServers &&
            a.EnableUdp == b.EnableUdp &&
            a.Sniffing == b.Sniffing &&
            a.AllowLan == b.AllowLan &&
            a.BalanceAllNodes == b.BalanceAllNodes &&
            a.HttpPort == b.HttpPort &&
            a.SocksPort == b.SocksPort &&
            a.PacProxyPort == b.PacProxyPort &&
            a.CorePathOverride == b.CorePathOverride;
    }

    public static bool NeedsReconnect(
        ClientSettings current,
        string runningJson,
        string? launchedProfileId,
        bool wasExternalConfig,
        string? runningExternalFilePath,
        string runningProxyMode,
        int runningPacPort)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(runningJson);

        // PAC/Manual can often be switched without a core restart. Global forces
        // ProxyAll routing in generated V4/V5 JSON, so mode changes that alter
        // the effective core routing require a restart. The Save handler may
        // perform a safe mode-only restart; unrelated pending edits remain staged.
        if ((runningProxyMode == "PAC" && runningPacPort != current.PacPort) ||
            wasExternalConfig != current.UseCustomConfig)
            return true;

        if (wasExternalConfig)
            return !string.Equals(runningExternalFilePath, current.CustomConfigPath, StringComparison.Ordinal)
                || (runningProxyMode == "Global") != (current.ProxyMode == "Global");

        var original = current.Profiles.FirstOrDefault(p => p.Id == launchedProfileId);
        if (original is null) return true;
        try
        {
            // Use the launched profile rather than the currently highlighted editor card.
            // This comparison includes Smart domain rules and advanced routing JSON.
            return !string.Equals(ConfigFlavor.Build(current, original), runningJson, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // An invalid pending configuration must not be silently treated as applied.
            return true;
        }
    }
}
