using VelaRay.Core.Models;

namespace VelaRay.Core.Configuration;

/// <summary>
/// Global is a Windows system-proxy entry mode AND a proxy-only core routing
/// mode. It applies only to traffic that reaches VelaRay's local listeners;
/// it cannot transparently capture applications that bypass system proxy.
/// Never overwrite the user's preferred PAC/Manual routing strategy.
/// </summary>
public static class EffectiveRoutingMode
{
    public static string Resolve(ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ProxyMode == "Global" ? "ProxyAll" : settings.RoutingMode;
    }
}
