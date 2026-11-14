# VelaRay 2.7 — Smart node latency testing

## Scope

Replaces the previous TCP-only manual **Test** button with one **Smart test** action. It tests all configured nodes and chooses the approach by the node's **transport**. This does not switch nodes, disconnect the active proxy, touch PAC/Windows system proxy, change routing, or persist probe results.

| Transport | Actual test | Metric on the card |
|---|---|---|
| TCP, TLS/TCP, WebSocket, gRPC, HTTPUpgrade | TCP socket connect to the configured host:port | `TCP 45 ms` — TCP connect time only; port may accept sockets even if authentication fails |
| mKCP, QUIC, Hysteria2 | Start a protected, isolated V2Fly process with only this node as outbound; send an HTTPS GET via its loopback HTTP proxy and require HTTP 204 from `https://www.gstatic.com/generate_204` | `UDP 125 ms` — end-to-end proxy request time, **not** UDP RTT or UDP port scan |
| Unknown transport | No test | Unsupported |

The two metrics **are not directly comparable**. There is no reliable general-purpose UDP port-open signal without a protocol-level response. A timeout does not prove a UDP port is closed.

## Implementation

- New `VelaRay.Core/Runtime/SmartNodeLatencyProbe.cs`: deterministic transport classification, pure V4/V5 isolated configuration builder, TCP connect probe, and UDP transport request probe.
- The V4/V5 temporary configuration binds HTTP **only** to `127.0.0.1`, has exactly one tested outbound, and contains **no** `freedom` outbound or inherited PAC/router/custom rules. This prevents bypass false positives.
- Uses the same core executable, per-node V4 or V5 loader (`json`/`jsonv5`), outbound builder, and Windows kill-on-close Job Object as normal VelaRay, without touching the normal running core.
- Temp config files contain connection secrets. They are written in the signed-in user's temporary directory, cleaned up in `finally`, and never logged. Crashes can leave an orphaned temp file; secure OS temp-directory permissions remain important.
- Limits to two concurrent checks, a 3-second timeout for TCP connect, up to two 5-second HTTPS requests for UDP, plus limited startup readiness polling.
- Uses an explicit `http://127.0.0.1:<temporary-port>` HTTP proxy in `HttpClientHandler`, never the Windows system proxy. Requires a real 204 response and normal TLS validation. Does **not** disable certificates.
- The UI captures each server profile on click to avoid editing races, labels results `TCP` or `UDP`, and shows a localized timeout/failure/configuration/core/unsupported result instead of a fabricated number.
- `TcpLatencyMs` remains as a non-persisted backward-compatible alias of `MeasuredLatencyMs` for existing card/test references.

## Known limits

1. UDP testing needs a usable `v2ray.exe` and the exact core capabilities for the profile's V4/V5 format. A test failure can be due to credentials, TLS, blocked outbound HTTPS, unreachable endpoint, incompatible core, or network.
2. The test endpoint may be blocked in some regions even when the proxy works for other sites. This is a **probe failure**, not proof the remote server is down.
3. The app does not test arbitrary UDP relay (e.g., SOCKS5 UDP ASSOCIATE); it tests the node's underlying UDP transport by sending a real proxied HTTPS request through it.
4. Windows .NET MAUI rendering, actual V2Fly process startup, and real remote nodes must be verified on a Windows machine. Python source checks do not substitute for compilation or runtime verification.

## Windows verification

```powershell
.\scripts\verify-windows.ps1 -SkipPublish
```

Manual acceptance: start a working TCP node, run Smart test and confirm `TCP`; start a working Hysteria2/mKCP/QUIC node, run Smart test and confirm `UDP` with real measured time; check a deliberately invalid credential and an unreachable server; verify the selected/running node, system proxy, and log/traffic UI are unchanged during tests; close the app during a test and check for surviving `v2ray.exe` probe children. Confirm Chinese/English status labels and check small window widths.
