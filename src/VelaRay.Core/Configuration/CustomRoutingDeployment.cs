using VelaRay.Core.Models;

namespace VelaRay.Core.Configuration;

/// <summary>
/// Editor rule sets are drafts. The core uses only an explicitly activated snapshot,
/// so ordinary reconnection cannot silently promote a rule edit.
/// </summary>
public static class CustomRoutingDeployment
{
    /// <summary>Initialize a snapshot from legacy settings once, before an editor can modify it.</summary>
    public static void MigrateLegacy(ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.AppliedRoutingRuleJson is null && settings.SelectedRoutingRuleId is { } id)
            settings.AppliedRoutingRuleJson = settings.RoutingRuleSets.FirstOrDefault(r => r.Id == id)?.Json;
        settings.EditingRoutingRuleId ??= settings.SelectedRoutingRuleId;
    }

    public static string ResolveJson(ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.AppliedRoutingRuleJson is not null)
            return settings.AppliedRoutingRuleJson;
        // Allow programmatic clients with pre-migration settings to continue working;
        // persisted settings are migrated on load before any editing is possible.
        return settings.RoutingRuleSets.FirstOrDefault(r => r.Id == settings.SelectedRoutingRuleId)?.Json
            ?? throw new ArgumentException("Select and apply a custom routing rule set in Proxy settings.");
    }

    public static bool HasAppliedRule(ClientSettings settings) =>
        settings.AppliedRoutingRuleJson is not null ||
        settings.RoutingRuleSets.Any(r => r.Id == settings.SelectedRoutingRuleId);

    /// <summary>The ONLY operation that promotes editor data into the selected core rule snapshot.</summary>
    public static void Apply(ClientSettings settings, string ruleSetId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var rule = settings.RoutingRuleSets.FirstOrDefault(r => r.Id == ruleSetId)
            ?? throw new ArgumentException("The selected routing rule set no longer exists.", nameof(ruleSetId));
        // Validate before changing any persisted selection state.
        V5ConfigBuilder.ParseObject(rule.Json, "custom routing");
        settings.AppliedRoutingRuleJson = rule.Json;
        settings.SelectedRoutingRuleId = rule.Id;
    }
}
