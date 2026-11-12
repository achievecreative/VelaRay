# VelaRay 2.7 — Reference-image desktop UI and staged PAC/Smart routing

## Screenshot-guided layout

- **Top toolbar** retains the original View Config and unified Connect/Switch/Disconnect actions on the far right at fixed widths. No actions beneath the logo.
- The language switch is a **globe-led, rounded segmented control** with a pale-blue track and raised white active choice. Explicit 中文 / EN options change the UI language immediately, without changing V2Fly connectivity. Dark-theme colors are also supplied.
- The left sidebar grows from 256 to **336 desktop layout units** to fit the reference-inspired cards. Each card contains the node name, a protocol badge, a monospaced `host:port` line, a latency slot, and a separate `使用中` / `In use` badge.
- **Selected / edited** uses a blue outline and neutral card fill. **Actually running** uses a blue left rail and a check-mark badge. Editing another card cannot start/switch/stop the proxy.
- **Latency is not fabricated**: `—` until the user chooses `测速 / Test`. For TCP-based transports, the app times a TCP connect with 3-second timeout and max four concurrent probes. This is a network-connect latency *estimate*, **not** end-to-end V2Fly proxy RTT. UDP-based Hysteria2/mKCP/QUIC is intentionally reported as unavailable (`—`), rather than falsely using TCP ping.
- Language, protocol, hostname and active status labels are localized/bound; card-only display fields and measured TCP times are not serialized into settings.

## Managed PAC vs running core

`RunningConfigChangeDetector` compares current settings' generated V4/V5 JSON against the **actual launch snapshot**, using the *launched* profile, not the selected editor profile. It separately checks effective system-proxy mode and PAC listener port.

1. **Pure PAC change**: updating an independent custom PAC script while PAC mode runs reloads the script in the already-running loopback PAC HTTP server and re-applies WinINET PAC configuration. The V2Fly process is untouched. Browser PAC caches may need a new request or time to refresh.
2. **Managed PAC template with unchanged core JSON**: generate and atomically replace `managed.pac`; update the PAC HTTP server without core restart.
3. **Smart managed PAC site-list change**: site rules influence generated core routing. Store edited settings but **do not replace the currently-served `managed.pac`**. Display a pending notice and a **Reconnect and apply** button. Only a user click reconnects and regenerates both together.
4. **Advanced routing change or fallback/mode change**: save settings and show pending notice while the old live proxy continues. No automatic restart or switch.
5. **Selection without edit**: does not change the running configuration or show an unnecessary pending indicator.
6. **Raw edits to managed.pac while Smart PAC mode is running**: blocked to avoid diverging from generator rules; use managed templates or a separate custom PAC file.
7. On disconnect, crash, or successful explicit reconnect, the pending runtime state is reset. Settings persist; failed reconnects may leave the proxy disconnected, as with existing forced restarts.

Note: untrusted **custom** PAC scripts are deliberately not rewritten or auto-synchronized with Smart core routing. The user owns their rules. No hot reload of V2Fly core JSON is attempted.

## Validation

Run `python scripts/check_reference_ui_and_pending_pac.py` for the new source contract; existing PAC JavaScript and Smart routing checks remain. `tests/VelaRay.SmokeTests/Program.cs` adds assertions that PAC-only changes do not require a reconnect, Smart domain/routing changes do, and selection-only changes do not. On a **Windows system with .NET 10 MAUI workload**, run `scripts/verify-windows.ps1 -SkipPublish` to execute C# smoke tests and real MAUI builds.

Manual GUI scenarios to verify on Windows: widths 800/1024/1366, 100–200% display scaling, both themes, both languages, many nodes and long node names, independent selected/active indications, no proxy changes on selection, TCP measurements vs HY2 unavailable, no PAC rewrite after Smart-domain pending edits, pure-custom-PAC hot reload, and explicit reconnect applying both new PAC and core rules.

**This source package has source-level validation only. MAUI rendering, Windows compilation and live V2Fly/PAC tests were not run in this environment.**
