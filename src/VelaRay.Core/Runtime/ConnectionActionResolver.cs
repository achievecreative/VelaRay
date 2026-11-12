namespace VelaRay.Core.Runtime;

/// <summary>
/// The editor selection never changes the live process. This resolves the one
/// explicit connection button from the selected and actually loaded profiles.
/// </summary>
public enum ConnectionAction { Connect, Switch, Disconnect }

public static class ConnectionActionResolver
{
    public static ConnectionAction Resolve(bool isRunning, string? selectedProfileId,
        IReadOnlySet<string> runningProfileIds, bool runningExternalConfig, bool selectedExternalConfig)
    {
        if (!isRunning) return ConnectionAction.Connect;
        if (runningExternalConfig && selectedExternalConfig) return ConnectionAction.Disconnect;
        if (!runningExternalConfig && !selectedExternalConfig &&
            selectedProfileId is not null && runningProfileIds.Contains(selectedProfileId))
            return ConnectionAction.Disconnect;
        return ConnectionAction.Switch;
    }
}