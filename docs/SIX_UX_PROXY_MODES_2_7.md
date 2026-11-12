# VelaRay 2.7 – Six UX refinements and PAC / Global / Manual proxy mode cards

## Scope

This release is **UX-only**. The V2Fly configuration builders, WinINET proxy implementation, PAC server, update downloader, runtime logger, node connection/switching events and persisted `ClientSettings` schema have **not** been changed.

Design refinements:

1. Proxy settings are grouped into coherent cards: **system proxy mode**, **local inbound ports**, **core/routing preferences**, and the existing separate **core update** area.
2. The top application bar shows a compact connected/disconnected status pill that reflects `_core.IsRunning` and the actual running node, not the selected/editing node.
3. Server cards retain blue selected-state outline, independent connected-state marker, protocol badge, endpoint and real measured TCP latency; a small green dot improves recognition of an actually running profile.
4. Replace the system proxy mode picker with three full-width, wrapping, radio-based choices **PAC / Global / Manual**. The original `ProxyModePicker` remains hidden as the *single source of truth*. Its `SelectedItem` is updated when a card is tapped; `CaptureOptions` and all connection/save handlers remain unchanged.
5. HTTP and SOCKS5 local ports occupy aligned, side-by-side cards. Existing LAN access, UDP forward, PAC port, DNS, log, startup, tray, balancer and other settings are retained.
6. The footer shows **configured ports** and the **actual running proxy mode** separately, so a pending unsaved or un-applied selection is not represented as the live connection. Runtime logging and always-on scrollbar stay unchanged.

## Exact proxy mode semantics

| Mode | Existing value | Effect on Windows proxy configuration |
| --- | --- | --- |
| PAC – Automatic system proxy | `PAC` | Publish local PAC URL to Windows. PAC rules determine direct vs proxied sites. |
| Global – Global system proxy | `Global` | Set Windows HTTP/HTTPS and SOCKS5 proxy. Only applications honoring system proxy are affected; **not a TUN or VPN**. |
| Manual – Local listening only | `Manual` | Run local HTTP/SOCKS services without changing Windows system proxy. |

A card selection is a pending UI preference only. **It does not save settings, start the core, disconnect the core, or silently switch nodes.** Existing save and explicit connection/reconnection behavior continues to apply. The pending routing/PAC reconnect banner is preserved. No new automatic V2Fly version check is introduced.

## Validation

Static checks:

```bash
python scripts/check_six_ux_proxy_modes.py
python scripts/static_check.py
python scripts/audit_ux_localization.py
python scripts/check_reference_ui_and_pending_pac.py
python scripts/check_header_action_placement.py
```

On a Windows machine with .NET 10 + MAUI workload:

```powershell
.\scripts\verify-windows.ps1 -SkipPublish
```

Manual UI acceptance: both light/dark themes; Chinese/English; 800/1024/1366 widths, 100%/125%/150%/200% display scale; selecting another server while connected; choosing each proxy mode without saving, then saving, then reconnecting explicitly; confirm footer distinguishes configured ports and running mode; ensure the three cards wrap on narrow windows; verify PAC and advanced routing pending changes still wait for manual reconnect.

**Static checks do not replace a Windows MAUI build or rendered UI verification.**
