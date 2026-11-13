> **Current layout update (2026-11-13):** The dedicated scrollbar-side column is now **10dp**, replacing the historical 18dp value described below. The actual visible gap is not guaranteed to be 10dp because native scrollbars may occupy part of this area.

# VelaRay 2.7 — 78dp server cards and proxy traffic monitoring

## Server list UX

- Minimum height: 78 device-independent pixels (actual height grows if platform scaling requires it).
- Sidebar width unchanged at 336dp; card width remains adaptive to the 336dp sidebar with an 18dp reserved template column (approximately 10dp after a typical 8dp overlay scrollbar).
- Internal horizontal padding: 12dp; vertical padding: 11dp; row spacing: 6dp; 12dp vertical item spacing via CollectionView.LinearItemsLayout (no per-card bottom margin).
- Server name 14sp bold; address 12sp regular; protocol 11sp regular; latency and status 11sp regular.
- Blue selection and running-profile indicator are independent, and selecting a row does not switch the proxy.

## Live proxy traffic statistics

- Bottom area shows upload and download **bytes per second** averaged over ~3-second sampling intervals, and upload/download **total bytes for this connected core process**.
- **Proxy outbounds only:** `outbound>>>proxy`, `outbound>>>node-*`, or other node outbound tags. `direct`, `block`, management API, and inbound traffic are excluded to avoid double-counting and avoid overstating proxy usage.
- A temporary loopback-only V2Fly management endpoint is enabled in `config.active.v4.json` or `config.active.v5.json` (the baseline generated file and user settings are unchanged). V4 uses `api/stats/policy/routing`; V5 uses `services.commander/stats/policy/router`.
- The API is readable by other local applications; it is never bound to 0.0.0.0. Management API does not require an external internet service.
- VelaRay queries via the official `v2ray api stats -json` CLI every three seconds, without NuGet additions and **without resetting** V2Fly counters. No history is persisted to disk. This launches one short-lived local process per sample. Polling halts on disconnect/reconnect/crash.
- Optional statistics are **fail-open for the user connection**: if V2Fly rejects the injected configuration, the unchanged baseline is restored before launching the core. If API queries fail, show “Stats unavailable”, not synthetic zero rates.
- **External/custom JSON:** never modified; statistics are shown unavailable (external JSON can have unrelated admin APIs and routing).
- Counters reset with each proxy connection; this is not an all-time/per-server usage meter. In a load-balanced profile, the numbers sum the proxy outbounds of the running process.
- The core's launch comparison snapshot remains the original generated JSON to prevent false “pending reconnect” warnings from the local stats-only overlay.

## Verification

`python scripts/check_traffic_stats_and_78dp.py` and `python scripts/static_check.py` pass without .NET.

On Windows with .NET 10 + MAUI workload, run `scripts/verify-windows.ps1 -SkipPublish` for the core build, C# tests and Windows app build. Test API compatibility with your installed V2Fly V4 and V5 binaries; the current environment lacks dotnet and V2Fly binaries, so real traffic display has **not** been runtime-verified here.

## UX refinements: server spacing and centered traffic

- Each server card keeps its 78dp minimum height, 12dp horizontal / 11dp vertical padding, and original click behavior.
- CollectionView uses **LinearItemsLayout.ItemSpacing=12dp**, rather than relying on card margins. Each item template reserves an **18dp empty right column** outside the card: approximately 8dp for the WinUI overlay scrollbar and a further 10dp gap. Exact clear space depends on Windows scrollbar width/DPI. Selected visual states are applied to the item template root and forwarded to the card border.
- The bottom status bar has three stable horizontal zones: **configured ports (left)**, **traffic rates and session totals (center)**, **running mode (right)**.
- The centered traffic block has upload/download speed on the first line and cumulative session totals on the second. Its existing label names, bindings, and update methods are unchanged.
- This is only a XAML presentation change. No modification to proxy, PAC, routing, statistics, or reconnect logic.
