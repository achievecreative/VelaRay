# VelaRay 2.7 / .NET 11 — dedicated, editable SOCKS5, HTTP, and PAC proxy inbounds

## Port ownership

| Field | Default | Core inbound tag | Behavior |
|---|---:|---|---|
| `SocksPort` | `1080` | `socks-in` | Explicit application SOCKS5; always remote proxy |
| `HttpPort` | `1088` | `http-in` | Explicit application HTTP CONNECT; always remote proxy |
| `PacProxyPort` | `1089` | `http-pac-in` | HTTP proxy referenced by PAC; selected Smart/BypassCN/Custom rules apply |
| `PacPort` | `18888` | **Not** a core inbound | Local PAC file web server (`/proxy.pac`) |

All **four** numeric ports can be edited in Proxy settings; the first three are proxy listeners. They must be unique and within 1–65535. The PAC proxy listener binds `127.0.0.1` even if LAN access is enabled for the normal application listeners. Windows PAC `AutoConfigURL` still points at the **PAC script server** (`PacPort`), whose script returns `PROXY 127.0.0.1:<PacProxyPort>`.

## Routing and activation

The previous `ForceExplicitProxy` toggle was removed. V4/V5 configuration writers always build the three inbounds. The first routing rule for all non-ProxyAll configurations is an `inboundTag` override for `socks-in` and `http-in` to the remote outbound (or selected balancer); **never** include `http-pac-in` in that rule. Global continues to force effective `ProxyAll`. PAC uses its own script and its own core ingress: for example, a PAC `DIRECT` does not touch the V2Ray core at all, while PAC `PROXY` goes through `http-pac-in` and its ordinary routing rules.

- Changing a core listening port requires a manual reconnect/apply; merely changing a Windows PAC/Global/Manual mode may still hot-switch if the underlying core config is unchanged.
- Any existing *hand-authored* `.pac`/`.js` may contain hardcoded `PROXY 127.0.0.1:1088`. **Review and change that to the configured `PacProxyPort`**. VelaRay deliberately does not rewrite custom PAC scripts.
- `managed.pac` is regenerated on an explicit apply or PAC reconnect and uses the new PAC proxy port. The unmodified stock `default.pac` is migrated automatically if it still exactly matches the old generated template.
- Existing `ExplicitHttpPort` in saved settings is migrated into `PacProxyPort` when the new field is absent. The old `ForceExplicitProxy` flag is ignored and not written on subsequent saves.
- External custom JSON configurations are not rewritten or given automatic inbound routing; they must independently provide their own proxy listeners and routing rules. The guaranteed three-port policy applies only to VelaRay-generated V4/V5 configurations.

## Windows acceptance checklist

1. Build and run the core config-check for V4 and V5. Confirm exactly three proxy inbounds and distinct ports; make sure the PAC HTTP inbound is loopback-only.
2. Run Smart/DirectByDefault: connect an app explicitly to `127.0.0.1:1080` and `127.0.0.1:1088` and confirm both exit through the remote node even for sites not in the PAC whitelist.
3. Enable PAC and fetch the `.pac` file from `http://127.0.0.1:18888/proxy.pac`; confirm it returns `PROXY 127.0.0.1:1089` for a matched domain, preserving PAC `DIRECT` for excluded domains.
4. Change any of the four port fields to a unique free value, save, reconnect/apply, and confirm core listeners and PAC script use the new values. Confirm duplicates and out-of-range values are rejected before start.
5. Switch PAC → Global → PAC; confirm Windows proxy settings and the expected `ProxyAll`/Smart routing are restored, with no unintentional port crossover.
6. Sleep and resume Windows while PAC mode is active; verify the resumed PAC listener and script still point to the *running* port, not a pending unsaved edit.

Source contracts and C# smoke tests are included. Windows .NET 11 MAUI build and V2Ray runtime integration tests have not been run in this environment.
