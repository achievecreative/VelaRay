# VMess + TCP compatibility fix (VelaRay 2.7 / .NET 11)

## Scope

This change fixes the UI protocol-transition defaults and normalizes V2Ray V5 plain TCP transport generation. It does **not** add the Xray-core engine; VelaRay still runs `v2ray.exe`.

### Symptoms addressed

New server profiles started with native Hysteria2 and `TlsEnabled=true`. When the user switched `Protocol` to VMess, the transport could stay at `hysteria2` (because VMess-over-Hysteria2 is a supported combination). Even if the user switched to `tcp`, TLS could remain enabled, causing the connection to a non-TLS VMess/TCP server to fail.

### Changes

- In `ProfileCapabilities.ProtocolSwitchDefaults`, switching from **native** Hysteria2 to VMess or Trojan defaults to **TCP without TLS**. Switching to native Hysteria2 enforces the matching HY2 transport and TLS. Switching among other protocols preserves previously chosen transport/TLS values.
- In `MainPage.ProtocolChanged`, apply those defaults to the UI only when the user changes the protocol, before re-evaluating the visible transport fields. Existing saved server profiles are not silently migrated or modified. Save is still explicit.
- In V5 plain TCP configuration, empty or `{ "type": "none" }` TCP-header settings become `"transportSettings": {}`. An explicitly customized header remains included. V4 output stays as it was.
- C# smoke tests cover UI policy and V4/V5 VMess/TCP `uuid`, transport and TLS behavior.

## How to verify on Windows

1. Open VelaRay, create a new node, change **Protocol** from Hysteria2 to VMess. **Transport** should become **tcp** and **TLS** should be off. For VMess/TCP+TLS servers, explicitly turn TLS back on and enter SNI/certificate settings.
2. Fill the correct server address, port, UUID, and TCP header. Start with `{"type":"none"}` if the server uses unmodified TCP.
3. If your server uses legacy VMess `alterId > 0`, choose **V4**. V5 requires VMess AEAD (`alterId=0`). For modern servers, `alterId=0` is recommended.
4. Save, click **Check Configuration**, and inspect the generated JSON. A no-TLS V5 VMess/TCP outbound should have `settings.uuid`, `streamSettings.transport="tcp"`, `streamSettings.security="none"`, and empty `transportSettings`.
5. Test with **Global + ProxyAll** to isolate connection problems from PAC/Smart routes. Global is Windows system proxy, not TUN; only proxy-aware requests are intercepted.
6. Run `dotnet build src/VelaRay.Maui/VelaRay.Maui.csproj -c Release -f net11.0-windows10.0.19041.0` and `dotnet run --project tests/VelaRay.SmokeTests/VelaRay.SmokeTests.csproj -c Release`.

For a V4 config file, the core check resembles: `v2ray.exe test -format json -c config.v4.json`. For V5 use `-format jsonv5`. Do not share real UUIDs or passwords in logs or bug reports.

## Remaining limits

- This code review does not prove an end-to-end connection to the user's server. The server may require TLS, have a different TCP disguise (`header.type=http`), use another port/UUID, or reject legacy VMess versions.
- VelaRay's existing **TCP latency test measures only TCP connection availability**, not successful VMess authentication or data forwarding.
- The Windows .NET 11 SDK and V2Ray executable are not available in this analysis environment; actual compilation and VMess connection must be checked on Windows.
