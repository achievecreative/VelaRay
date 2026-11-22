# VelaRay 2.7 — Windows sleep/wake recovery and compressed single-file publish

## Sleep/wake recovery

- Listens for `Microsoft.Windows.System.Power.PowerManager.SystemSuspendStatusChanged` on Windows; unsubscribes during exit.
- Records the **actually running** V2Fly executable, config path/format, currently active profile IDs, proxy mode and local ports before suspend.
- After resume, waits for networking and a short settling delay. If a connection was active, serializes a core restart using the existing connection gate, waits for local HTTP/SOCKS listeners, restarts PAC if necessary and refreshes WinINET settings only when still owned by VelaRay.
- Does **not** apply unsaved routing/node changes or switch to a merely selected profile. It does **not** auto-connect an intentionally disconnected app.
- A manual connect/disconnect/mode change supersedes the pending recovery; repeated wake events are debounced/cancelled.
- If another process or VPN changes system proxy settings, VelaRay does not silently overwrite them. If the network stays unavailable or the core fails, a warning is logged and manual reconnect may be needed.

### Real Windows acceptance tests

1. Connect VMess/TCP in PAC mode, sleep/wake, confirm browser access and original PAC selection.
2. Repeat with Global and Manual modes; confirm Manual does not edit WinINET.
3. Repeat with Hysteria2/mKCP; ensure prior active node is restored even after another node is selected for editing.
4. Enter sleep while disconnected; confirm the app stays disconnected after wake.
5. While waking, manually disconnect or change modes; confirm no stale recovery overrides the action.
6. Sleep with network disconnected; confirm the app does not enter an endless restart loop.
7. Check Settings/WinINET changes by a third-party VPN during sleep are not overwritten.
8. Verify current traffic stats reset on reconnection and no pending custom-routing edits are applied.

## Compressed Windows publication

`./scripts/publish-windows.ps1 -Mode SingleFile` now passes **`-p:EnableCompressionInSingleFile=true`** together with `PublishSingleFile=true` and self-contained settings. The existing `Folder` publish mode does not use the flag.

Compression can shrink embedded assemblies, but can increase startup cost. Measure the output `publish/VelaRay-win-x64.exe` size and cold start time to compare against an uncompressed build. V2Fly binary and Geo data remain separate from the GUI EXE.

## Verification limits

`python scripts/check_resume_recovery_and_publish_compression.py` is source-level verification only. Build with .NET 11 + Windows MAUI workload and perform real sleep/wake and publish tests on Windows before relying on the feature in production.
