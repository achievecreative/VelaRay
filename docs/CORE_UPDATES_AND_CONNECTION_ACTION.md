# Unified connection action & V2Fly core updates (VelaRay 2.7)

## Connection action

Both the top toolbar and fixed footer display the same action, computed from the *running* configuration snapshot and the *selected* editor profile:

| Core running | Selected target | Button text | On click |
|---|---|---|---|
| No | Any node / custom config | Connect | Save/generate config and connect |
| Yes | One of the running profile IDs (including load-balancer members) | Disconnect | Stop core and restore the previous WinINET proxy configuration |
| Yes | Different profile | Switch | Explicitly stop the running core and connect the selected profile |
| Yes | Current external config and external-config mode still selected | Disconnect | Stop that configuration |
| Yes | Switching between external-config mode and a regular node | Switch | Explicitly change configuration |

**Selecting or editing a server never initiates a connection change.** The running-profile green badge remains independent of the blue selected row. Connections are serialized using the existing connection semaphore. Controls are briefly disabled during an explicit click to prevent accidental duplicate commands. The separate **Apply / switch server** editor command still explicitly re-applies changes.

## Core update UX

Under **Proxy settings → V2Ray core updates**:

1. Show the locally installed `v2ray` version, or `Not installed`/`Unknown`.
2. **Do not check at startup.** Only an explicit click on **Check for core updates** probes the locally installed version and requests the official GitHub release API. Opening Proxy settings does not trigger a check.
3. Stable release channel is the default. The **Include prereleases** toggle is explicit, persisted in `settings/settings.v5.json`, and invalidates the previous result without issuing a network request. Click **Check for core updates** to query the selected channel.
4. Display the latest eligible version and enable **Download and install** only when a newer release exists, the proxy is disconnected, and the core path is the app-managed portable `v2ray-core/` location.
5. Require the user to confirm installation. Never stop the proxy automatically.

## Download & safety

- Reads `https://api.github.com/repos/v2fly/v2ray-core/releases?per_page=40`. Excludes drafts and, by default, prereleases. Selects the newest semantic version with the official architecture-specific ZIP (`windows-64`, `windows-32`, `windows-arm64-v8a`).
- Accepts only HTTPS assets under the official `github.com/v2fly/v2ray-core/releases/download/<tag>/` path. Reads GitHub's asset SHA256 digest when present, otherwise the official adjacent `.dgst` metadata (`SHA2-256=`).
- Limits the ZIP download to 150 MiB and the extracted executable to 200 MiB; verifies archive SHA256, extracts **only** the root-level executable, and runs `v2ray version` on the staged binary to compare to the selected release.
- Uses a temporary staging directory within `v2ray-core/`. Once fully validated, moves the previous core executable to a timestamped `v2ray.exe.backup-*` and swaps the new executable in. If the final move fails, attempts to restore the old executable.
- Preserves `geosite.dat`, `geoip.dat`, user settings, configs, PACs, logs, and the custom core executable path. External/overridden core paths are **read-only for updater**.
- Does not claim cryptographic publisher authentication beyond HTTPS+official GitHub release metadata+SHA256. A maliciously modified upstream release plus its matching published hash could still pass validation.
- On network or permission failure, shows a localized error and retains diagnostics in the log; no automatic retry or restart.

**Rollback:** Disconnect the proxy. If you need to revert a successful update, rename the current `v2ray.exe` and restore the desired `v2ray.exe.backup-*` to `v2ray.exe` within the same directory; then connect again. Automatic rollback applies only when the final installation move fails, not when a later runtime incompatibility is discovered.

## Verification

On Windows with .NET 10 MAUI tooling:

```powershell
python scripts/check_core_updates_and_connection_action.py
dotnet run --project tests/VelaRay.SmokeTests -c Release
.\scripts\verify-windows.ps1 -SkipPublish
```

Manual acceptance cases: launch the app and open Proxy settings while monitoring traffic (no GitHub release request); toggle prerelease (still no request); click Check for core updates (exactly one user-initiated check); switch editor selection while connected (no runtime change); click Disconnect for current node and Switch for another node; use external config; test stable/prerelease toggle; test offline checks; interrupt a download; test incorrect SHA256 metadata (must not replace executable); check file backup and Geo file preservation. A real Windows build, official download, UI rendering, and core restart must be verified on Windows.
