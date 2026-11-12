using VelaRay.Core.Models;

namespace VelaRay.Core.Configuration;

/// <summary>
/// Determines which saved server profiles are included as proxy outbounds in the
/// generated core configuration. This does not probe remote network connectivity.
/// External JSON configs cannot reliably be mapped to saved profiles.
/// </summary>
public static class ActiveProfileResolver
{
    public static IReadOnlyList<ServerProfile> Resolve(ClientSettings settings, ServerProfile selected)
    {
        if (settings.UseCustomConfig) return [];
        return OutboundPlanner.Get(settings, selected)
            .Select(entry => entry.Profile)
            .DistinctBy(profile => profile.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
