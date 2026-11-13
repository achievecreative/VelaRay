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

        // A PAC/Global/Manual change only alters WinINET and the local PAC listener;
        // it does not change V2Fly's JSON. The UI applies mode-only saves live.
        if ((runningProxyMode == "PAC" && runningPacPort != current.PacPort) ||
            wasExternalConfig != current.UseCustomConfig)
            return true;

        if (wasExternalConfig)
            return !string.Equals(runningExternalFilePath, current.CustomConfigPath, StringComparison.Ordinal);

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
