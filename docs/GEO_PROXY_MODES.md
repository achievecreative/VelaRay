# VelaRay: Windows system proxy modes and V2Fly Geo routing

## Important separation

The Windows **system proxy mode** decides how applications *enter* V2Ray. The V2Fly **routing mode** decides what to do with traffic *after it enters* V2Ray. They are independent controls. Windows WinINET proxy configuration is not a transparent VPN/TUN and cannot force arbitrary apps or UDP traffic through the proxy.

| System proxy | What the client changes | Result |
|---|---|---|
| Global | Enables WinINET current-user proxy: `http=127.0.0.1:<HTTP>;https=127.0.0.1:<HTTP>;socks=127.0.0.1:<SOCKS>` | Applications obeying Windows proxy settings connect to the local listeners; localhost/local traffic is excluded. SOCKS usage depends on app support. |
| PAC | Registers `http://127.0.0.1:<PAC>/proxy.pac` with WinINET | Windows/browser PAC engine chooses `DIRECT` vs local HTTP proxy. PAC-direct requests do **not** reach V2Ray. |
| Manual | **Does not write any Windows proxy registry value.** | Only starts local HTTP and SOCKS listeners. Users configure apps manually. Disconnect does not change system proxy. |

All modes wait until the local listeners are accepting connections before declaring a successful connection. A mode switch from Global or PAC back to Manual first restores the original Windows settings. On unexpected core exit, the app restores the original system proxy if it modified it. It backs up previous registry values to `settings/wininet-proxy-backup.json`; the next run can repair abandoned local proxy settings. `AutoDetect` is also backed up and restored.

**Caution:** “Global” here means *global Windows proxy settings*, not “send every OS packet through a proxy.” The core routing strategy still determines whether intercepted requests use `direct` or `proxy`. For every intercepted request to egress through the proxy, choose the core routing preset `ProxyAll`.

## How the Geo data works

The three `.dat` files are data assets included with / downloaded for V2Fly, **not embedded into `v2ray.exe`**:

- `geosite.dat`: a compiled list of domain categories, e.g., `cn` and `category-ads-all`. The core can classify a **domain name**, not automatically a resolved IP.
- `geoip.dat`: the full compiled IP ranges grouped by country/location and other tags, such as `cn` and `private`.
- `geoip-only-cn-private.dat`: a smaller GeoIP asset containing **only** the `cn` and `private` lists; it cannot answer queries about US/JP/SG networks.

The default `BypassCN` internal routing mode applies rules in this order:
1. If the domain matches `geosite:cn` (or exactly `localhost`) -> `direct`.
2. If the destination IP matches `geoip:private` or `geoip:cn` -> `direct`.
3. Otherwise -> first proxy outbound (or selected load balancer).

The routing `domainStrategy` is IPIfNonMatch / IpIfNonMatch. If a domain doesn't match a domain rule, V2Ray may resolve it to test IP-based rules. Domain, sniffing, and DNS configuration influence the classification. A `geoip:cn` rule is not equivalent to “the website is Chinese”: it matches IP ranges, which may differ from a site's commercial ownership or CDN routing.

## Generated JSON snippets

V4 (`-format json`) uses `routing.rules` and the `ext:` resource selector for the non-default compact file:

```json
{
  "routing": {
    "domainStrategy": "IPIfNonMatch",
    "rules": [
      {"type": "field", "domain": ["localhost", "geosite:cn"], "outboundTag": "direct"},
      {"type": "field", "ip": ["ext:geoip-only-cn-private.dat:private", "ext:geoip-only-cn-private.dat:cn"], "outboundTag": "direct"}
    ]
  }
}
```

V5 (`-format jsonv5`) uses `router.rule` and explicit `filePath`:

```json
{
  "router": {
    "domainStrategy": "IpIfNonMatch",
    "rule": [
      {"tag": "direct", "geoDomain": [{"filePath": "geosite.dat", "code": "cn"}]},
      {"tag": "direct", "geoip": [
        {"filePath": "geoip-only-cn-private.dat", "code": "private"},
        {"filePath": "geoip-only-cn-private.dat", "code": "cn"}
      ]}
    ]
  }
}
```

These are routing excerpts, not complete v2ray configs. VelaRay generates the full configuration with inbounds and outbounds and displays the active snapshot under `configs/`.

## Geo files location and updating

```text
VelaRay/
  VelaRay.exe
  v2ray-core/
    v2ray.exe
    geosite.dat
    geoip.dat
    geoip-only-cn-private.dat
  configs/
  settings/
  pacs/
```

At both validation and runtime, VelaRay sets `V2RAY_LOCATION_ASSET` to the actual directory holding the selected `v2ray.exe`; this also supports a custom core executable path. The `BypassCN` preset checks the required Geo files before connecting. Custom routing rules are still ultimately validated by `v2ray.exe test`.

In **Routing and PAC**, choose either `geoip-only-cn-private.dat` (smaller, default) or `geoip.dat` (full). The **Check Geo Assets** button checks for missing files. **Update Geo Assets** downloads the official sources, verifies SHA256, writes files safely, and asks you to reconnect so that the running core reloads them.

Official upstream sources:
- [V2Fly GeoIP releases](https://github.com/v2fly/geoip/releases) for `geoip.dat` and `geoip-only-cn-private.dat` and their `.sha256sum` files
- [V2Fly domain-list-community releases](https://github.com/v2fly/domain-list-community/releases) for `dlc.dat` (renamed to `geosite.dat`) and its checksum

## PAC is different

PAC is JavaScript (`FindProxyForURL(url, host)`) interpreted by Windows/browser clients. It **cannot read V2Fly `.dat` assets directly**. VelaRay's managed PAC can instead apply independent domain rules and an editable template with user-provided direct and proxy domain entries. For fine-grained country/IP routing, use V2Ray's internal `BypassCN` preset; for client-side bypass without entering V2Ray, add PAC rules.

## Supported protocols

The client supports VMess, Trojan and Hysteria2 only. Shadowsocks is not offered or generated. No original WPF settings import is available; external V4/V5 core JSON selection remains a separate feature.

## Validation limits

The project source has static XML/JSON/event checks and C# smoke tests, but the source package is **not** verified on Windows with .NET 10, real V2Fly core, registry settings, SOCKS clients, or PAC browsers. Please execute `dotnet run --project tests/VelaRay.SmokeTests` and `dotnet build src/VelaRay.Maui/VelaRay.Maui.csproj -f net10.0-windows10.0.19041.0` on Windows, then validate V4/V5 config with the installed v2ray.exe before using system proxy mode.
