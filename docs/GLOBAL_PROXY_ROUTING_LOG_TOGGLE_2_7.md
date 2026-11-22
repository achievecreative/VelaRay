# Global proxy-only routing and logging-toggle layout (VelaRay 2.7 / .NET 11)

## Global changes the core route, not just Windows WinINET

- For **VelaRay-generated V4 and V5** configurations, `ProxyMode=Global` makes the **effective** V2Fly routing strategy `ProxyAll`, with remote proxy outbound(s) first. Saved `RoutingMode` and `SmartUnmatchedRoute` remain unchanged and continue to apply when switching to PAC or Manual.
- This means **all HTTP/SOCKS requests that actually enter VelaRay's local proxy** go through remote proxy outbound(s), including requests to Chinese domains, unless the selected remote proxy itself has a special behavior. V2Fly's local-only statistics API maintains its own internal route.
- Windows Global mode is **not TUN**: applications ignoring Windows system proxy settings, raw UDP sockets, and Windows proxy exceptions (e.g. local/loopback) are not intercepted.
- Switching PAC/Manual to Global or back **requires reconnect and apply only when the generated core config differs**. The normal Save operation marks the config pending; it does not temporarily enable Global with a Smart/direct core. If both current and new effective routes are already ProxyAll (and all other core settings match), the WinINET mode can still switch without restarting the core.
- External custom JSON cannot be made proxy-only reliably without rewriting arbitrary user routing and inbound definitions. A clear validation error prevents `Global + external JSON`; use VelaRay-generated config for this guarantee, or PAC/Manual for an external config.
- Switching PAC/Manual restores its previously selected routing policy. Previewing built-in routes in Routing & PAC remains read-only and independent of the effective runtime override.

## Log file toggle

- `FileLoggingSwitch` is in the same FlexLayout row of general switches as Sniffing, Startup, Auto-connect, Tray and Balance.
- The extra Help label has been removed from the UI and localization resources.
- Default remains on. When off, only the newest 1,000 UI logs stay in memory; disk writes and export remain disabled. On remains a 60-second check with a 1,000-entry threshold.
- The switch event handler and immediate settings persistence are unchanged.

## Manual verification on Windows

1. Connect with PAC + Smart (Direct fallback) and confirm direct rules are in the generated/running JSON.
2. Choose Global and save without clicking reconnect. Confirm the pending-change banner appears, WinINET still uses PAC, and the running config remains unchanged.
3. Click Reconnect and apply. Confirm WinINET uses HTTP/SOCKS proxy without AutoConfigURL and **running** V2Fly JSON has proxy outbound first, no Smart/China direct routing rules.
4. Choose PAC and save. Confirm pending status. Reconnect and verify Smart routing (including configured China direct rules) is restored.
5. Repeat with preselected ProxyAll; a PAC/Global mode change with identical generated JSON can be applied without a core restart.
6. Enable multi-node balance in Global; confirm the generated V4/V5 balancer sends all inbound TCP/UDP requests to proxy nodes.
7. Confirm an external JSON configuration and Global cannot be saved together without an explicit validation error.
8. Confirm log file toggle appears aligned with other toggles, without a description; disable it and verify no disk logging occurs.

Run `dotnet build src/VelaRay.Maui/VelaRay.Maui.csproj -c Release` and `dotnet run --project tests/VelaRay.SmokeTests` on Windows with the .NET 11 MAUI workload. Python source checks are not an actual MAUI compile or runtime validation.
