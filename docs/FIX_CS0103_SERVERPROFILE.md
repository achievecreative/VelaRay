# Fix CS0103 in ServerProfile.cs (VelaRay 2.7)

The previous ZIP accidentally inserted `ServerProfile`-specific UI card properties
into `RoutingRuleSet`. As a result, compiling `VelaRay.Core` reported ten CS0103
errors involving `PropertyChanged`, `Protocol`, `Address`, and `Port`.

## Correction

- Keep `TcpLatencyMs`, `ProtocolBadge`, `EndpointDisplay`, `LatencyDisplay`,
  and `NotifyCardChanged()` on `ServerProfile`.
- Restore `RoutingRuleSet` to its original `Id`, `Name` and `Json` properties.
- No XAML, runtime proxy, connection, PAC, routing, settings-schema, or event-handler
  behavior is changed in this patch.

## Regression checks

From the project root:

```powershell
dotnet build src/VelaRay.Core/VelaRay.Core.csproj -c Release
dotnet run --project tests/VelaRay.SmokeTests -c Release
.\scripts\verify-windows.ps1 -SkipPublish
```

Also added `scripts/check_server_profile_model_boundary.py`, which checks the
model separation even when the .NET SDK is not installed.

This package was statically verified in an environment without the .NET SDK;
Windows MAUI compilation remains to be verified on Windows.
