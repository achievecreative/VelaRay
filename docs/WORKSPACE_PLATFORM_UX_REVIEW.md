# VelaRay — Workspace UX review and desktop-platform readiness

This change is based on the VelaRay 2.6 configuration-viewer source archive. It is a **Windows-only .NET 10 MAUI deliverable**. The cross-platform preparation is structural, not a claim that macOS or Linux builds work today.

## Workspaces and selection behavior

The sidebar is now arranged top-to-bottom as:

1. **Workspace**: Proxy settings; Routing & PAC (both shared across all server profiles).
2. **Servers**: scrolling collection of server profiles, followed by Clone / Share / Delete.
3. **Server settings**: fixed at the bottom of the sidebar (node-specific editor).

Clicking a server row **opens Server settings for editing without connecting, disconnecting or switching** the active proxy. The selected server has a blue background/border. A separate **green icon** appears only when the server is part of the active runtime configuration. The top toolbar shows the actual running server(s) and a separate connected/disconnected indicator; changing the selected row does not alter either one.

The right-side **Global settings** reminders remain in Proxy settings and Routing & PAC.

## UX review: changes applied

| Finding | Actual change | Verification |
| --- | --- | --- |
| The node-specific page was the first item in a shared-workspace menu | Move to bottom and rename `服务器设置 / Server settings` | XML + both locale dictionaries + source contract |
| Selected profile and connected profile were easy to confuse | Selected-state VisualState for the server card; green connected marker remains independent | XAML + connection-selection regression |
| Top header could overflow when window narrow or English labels are longer | Replace six fixed horizontal columns with a two-row toolbar; action controls wrap | XML and platform-UX check; **render testing pending** |
| Live status was only visible in the content panel | Add `TopConnectionStateLabel` bound to the actual runner state | XAML + code contract |
| Buttons in proxy, PAC and node operation bars could disappear beyond the right edge | Replace non-wrapping HorizontalStackLayouts with FlexLayout/Wrap | XML and UX check |
| Numeric server/port inputs did not identify input type | Add `Keyboard=Numeric` to port and HY2 bandwidth fields | XML check; desktop runtime pending |
| Windows-only font family and Consolas were hard-coded throughout styles | Centralize two OnPlatform font resources: YaHei/Consolas on WinUI, PingFang/Menlo on Mac Catalyst | Resource XML; **Mac render test pending** |
| App and page constructors referenced concrete Windows shell/proxy classes | Use `IWindowHostService`, `IWindowShell` and `ISystemProxyService` contracts | Source contracts; compilation pending |
| Core binary lookup assumed a .exe filename | Add `PortableCoreLocator` using v2ray.exe on Windows, v2ray on Unix | C# smoke-test coverage added; execution pending |
| Portable data storage was hardwired to the EXE directory on every platform | Add `DesktopDataDirectories` (Windows: EXE folder; Mac: user Application Support; Linux: XDG config folder) | C# smoke-test coverage added; execution pending |

Existing per-node V4/V5, Hysteria2, default-direct smart routing, PAC, language switch, tray, single instance, process shutdown, Windows system proxy and external `v2ray.exe` deployment remain in place. No settings migration logic has been added.

## Native/platform architecture

```text
VelaRay.Core (.NET 10, reusable)
  Configuration / PAC / routing / settings / portable binary locations
  Platform contracts (ISystemProxyService, IWindowShell, ICoreLocator,
                      IStartupService, ITrayService, IFileLocationService)
        |
VelaRay.Maui (.NET 10, currently net10.0-windows10.0.19041.0)
  Shared pages + localization + app lifecycle
        |
  Windows adapters: WinINET, Job Object, Mutex, WinUI window, native tray,
                    Explorer, startup registration

Future macOS: MAUI Mac Catalyst app + native Mac adapters (not implemented)
Future Linux: separate desktop UI technology + reuse VelaRay.Core (not implemented)
```

`IWindowHostService` stays in the MAUI layer because it takes a `Microsoft.Maui.Controls.Window`. Single-instance activation is guarded with `#if WINDOWS`; Windows service files are excluded from a *future* Mac Catalyst target. The current TFM has not changed and no non-Windows adapters are registered.

### Platform support status

| Platform | Official .NET MAUI desktop support | Current VelaRay status | Prerequisites before claiming support |
| --- | --- | --- | --- |
| Windows 10/11 | Yes, WinUI 3 | Source target; **Windows build and runtime not verified in this environment** | Actual Debug/Release build, DPI and system proxy tests |
| macOS | Yes, via Mac Catalyst | Prepared contracts/styles/storage only | Add Mac Catalyst TFM, Xcode CI, system proxy (networksetup/SystemConfiguration), tray/menu-bar, startup/launch agent, crash-safe process group, native window and single-instance adapters; packaging/codesigning |
| Linux desktop | Not officially supported by MAUI | Reusable Core only | Choose a Linux desktop UI technology (e.g. Avalonia); provide NetworkManager/GSettings proxy integration per desktop, tray, startup and process-group lifecycle, packaging |

The existing Windows **single-file executable** publish script has not been changed. It still excludes `v2ray.exe` and Geo data. A Mac Catalyst `.app` and a Linux package would have different publishing models and paths.

### Risks and future improvements (not implemented in this change)

- The remaining large `MainPage.xaml.cs` code-behind should eventually be split into testable view models and services; that is a separate, high-risk refactor and must not be merged without Windows build/runtime tests.
- PAC `HttpListener` and operating-system proxy APIs need end-to-end tests for each new platform.
- Signed macOS `.app` bundles are not writable application data stores. `DesktopDataDirectories` deliberately uses user Application Support instead.
- Linux cannot be enabled by adding a Linux MAUI TargetFramework string; an alternative UI layer is required.
- Consider CommunityToolkit.Maui `Expander` only in isolated advanced settings sections; avoid embedding an Expander inside `CollectionView` because the official toolkit documents platform-specific caveats.
- A newer MAUI CollectionView handler may improve performance, but retain the current stable .NET 10 target until the Windows application compiles and functions; upgrade and re-test before enabling .NET 11-specific handlers.

## Verification

Source-level checks (run on Linux/macOS/Windows with Python):

```bash
python scripts/static_check.py
python scripts/audit_ux_localization.py
python scripts/check_workspace_and_platform.py
python scripts/check_connection_selection.py
python scripts/check_csharp_structure.py
python scripts/check_pac_js.py
```

On **Windows with .NET 10 + MAUI Windows workload**, run real tests:

```powershell
.\scripts\verify-windows.ps1 -SkipPublish
```

Manual UI acceptance: check window widths 800/1024/1366 at 100%, 125%, 150%, 200% scale; check both languages; open each global page, select a server and confirm the editor opens but does not connect; verify blue selection vs green active icon; connect one server while editing another; ensure the running configuration and tray still identify the running server; verify routing/PAC action buttons wrap at smaller widths; verify app close and forced termination of child v2ray.

**Do not interpret passing static checks as proof of MAUI compilation or runtime correctness.**

## Official references

- Supported .NET MAUI platforms: https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms
- CollectionView selection state: https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/collectionview/selection
- Platform-specific UI and OnPlatform: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/customize-ui-appearance
- MAUI Toolkit Expander: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/views/expander
