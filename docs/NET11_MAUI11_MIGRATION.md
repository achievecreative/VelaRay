# VelaRay 2.7: .NET 11 / MAUI 11 migration

## Scope and verified stable releases (2026-11-14)

- .NET 11 (STS), .NET SDK `11.0.100`, target Core/smoke tests `net11.0`.
- Windows MAUI project `net11.0-windows10.0.19041.0`, MAUI Controls `11.0.10`.
- Microsoft.Extensions.Logging.Debug `11.0.0`.
- Windows lifecycle test `net11.0-windows`.
- `global.json` pins the SDK to the .NET **11 feature line** (roll-forward to later 11 feature bands, not to .NET 12).
- Windows GitHub Actions setup, MAUI workload installation, Debug/Release build and single-file publishing updated.

Official release/version sources:

- https://dotnet.microsoft.com/en-us/download/dotnet/latest
- https://dotnet.microsoft.com/en-us/platform/support/policy/maui
- https://www.nuget.org/packages/Microsoft.Maui.Controls/11.0.10
- https://www.nuget.org/packages/Microsoft.Extensions.Logging.Debug/11.0.0
- https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-11

## Compatibility strategy

VelaRay now enables the .NET 11 Windows `CollectionView2` handler explicitly via
`<UseWindowsCollectionView2Handler>true</UseWindowsCollectionView2Handler>`.
The new ItemsRepeater-based handler is the default in MAUI 11. The server
card border and running indicator are bound to `IsSelected` / `IsConnected`
on `ServerProfile`, rather than relying on native Selected visual states.

**Windows visual regression required**: check scrolling, 2dp item spacing,
10dp right gutter, selection and running markers, initial startup borders,
and card height at 100–200% display scale. If the new handler causes a
regression, reverting the property to `false` remains a temporary fallback.
The change does not alter proxy connection behavior.
MAUI 11 no longer ships `Microsoft.Maui.Controls.Compatibility`; VelaRay did
not reference that package. The project also does not subclass the MAUI `Color`
record, which became sealed in 11. Windows remains the only built desktop
platform; do not infer macOS/Linux support from the newer framework.

## Requirements on Windows

1. Install .NET SDK **11.0.100 or newer within major version 11**.
2. Install or update Visual Studio with support for .NET 11 and .NET MAUI 11;
   select **.NET Multi-platform App UI development** / MAUI Windows. If using
   the CLI, run `dotnet workload install maui-windows` from an elevated prompt.
3. Verify `dotnet --version` selects `11.0.x` in the solution directory and
   `dotnet workload list` contains `maui-windows`.

From the repository root:

```powershell
dotnet --version
dotnet workload list
python scripts/check_net11_maui_migration.py
.\scripts\verify-windows.ps1 -SkipPublish
# After Debug + Release pass:
.\scripts\publish-windows.ps1 -Mode Folder
.\scripts\publish-windows.ps1 -Mode SingleFile
```

For a completely clean restore after changing TFM (optional if old `obj` files
cause stale assets):

```powershell
dotnet clean VelaRay.slnx
Get-ChildItem -Path src,tests -Directory -Recurse |
  Where-Object Name -in @('bin','obj') |
  Remove-Item -Recurse -Force
# Then restore/build again
```

`dotnet workload restore` can be used when MAUI workloads need to be resolved.
The existing standalone EXE excludes `v2ray.exe`, Geo data and user settings.

## Runtime regression checklist

- Launch, close/minimize to tray, relaunch, single instance, forced process exit.
- PAC, Global, Manual mode transitions and Windows proxy restore.
- Connect / switch / disconnect; edit inactive node while another is connected.
- V4 and V5 configuration validation, smart routing, manual-only V2Fly update.
- Server list: 78dp min card, 2dp ItemSpacing, 10dp right gutter, selection vs running indicator, vertical scrolling.
- Editor scrollbars; bilingual labels; traffic speed/status and log filtering.
- TCP/UDP node test timeout shown in red; do not regress process isolation.
- Debug/Release MAUI build and both folder/single-file publish; launch both outputs.

**Known limitation in this handoff:** The source was migrated and verified with
Python source/XML/JSON checks in a Linux environment without .NET SDK, package
restore or Windows WinUI. No successful MAUI 11 compilation or real UI run is
claimed. Windows verification remains a required final release gate.

## Rollback

Keep the prior **.NET 10 source archive** as the supported fallback while
validating .NET 11. If a blocking MAUI 11 issue occurs, first try the legacy
CollectionView flag; if unresolved, rebuild the previous source archive rather
than mixing MAUI 10 packages with the net11 target.
