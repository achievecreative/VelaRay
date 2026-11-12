# Connection selection and Hysteria2 V5 (VelaRay 2.6)

- Clicking a profile **only selects an editor target**. It never starts or changes a connection.
- The top bar and green indicators reflect the **actual launched configuration**, not list selection.
- Running profiles (including balancer participants) are locked for editing/deletion. Other profiles remain editable and selectable.
- To connect to a different node, explicitly click **Switch**; the old core is then stopped and the new one started. When the selected node is already active, the same button says **Disconnect**. When no core runs, it says **Connect**.
- Subscription URL management remains removed. Official V2Fly core update checks and installs have been restored under **Proxy settings**; custom executable paths are not overwritten. Geo data tools and logs remain.
- Hysteria2 V5 follows [V2Fly official example](https://github.com/v2fly/v2ray-examples/blob/master/v5/Hysteria2-Hysteria2/hy2_hy2_client.json): `settings.server`, `streamSettings.transport=hysteria2`, password/congestion/UDP extension, TLS SNI `server_name`.
- For Hysteria2 transport, TCP-specific `nextProtocol` ALPN overrides are neither shown nor emitted. Reusing `http/1.1` from an old TLS profile could break the HTTP/3 handshake.
- Hysteria2 V5 is supported in V2Fly. If validation fails, collect the full `v2ray.exe test -format jsonv5 -c ...` output. Certificate verification, Geo data files, older v2ray binary versions and runtime handshake problems are separate failure classes.

## Validation

Static checks ensure XAML/XML parse, named event handlers match, i18n keys exist, removed controls are not referenced, and new V5 Hysteria2 assertions are present. **A Windows build and V2Fly integration test have not been performed in this environment.**

```powershell
.\scripts\verify-windows.ps1 -SkipPublish
.\scripts\publish-windows.ps1 -Mode SingleFile
```
