# VelaRay – Server-specific config versions and language UI

## Ownership and behavior

- `ServerProfile.ConfigVersion` (`V4` or `V5`) is stored independently for each node.
- `ClientSettings` only holds app-wide settings: system proxy mode, ports, DNS, routing and PAC.
- The selected node chooses the V2Fly config loader for **the entire generated config**: `json` for V4, `jsonv5` for V5. Multi-server outbound references are serialized in that same format. If the chosen format cannot express an option (for example VMess V4 alterId != 0 in V5), validation rejects the config instead of silently dropping it.
- The generated config file can be overwritten for a given format when another node is selected, but each node's preferred format remains stored in `settings/settings.v5.json`.
- No data migration is performed. Only the current settings structure is supported; newly created nodes default to V5. An obsolete application-level `ConfigVersion` is not read or carried forward.

## Language control

The native Windows MAUI language Picker popup was clipped at some window sizes and DPI scale factors. Language selection now uses a compact two-choice **中文 / EN** segmented selector, anchored to the right of the fixed-width current-node label. It applies and persists without restarting. Programmatic initialization is guarded to avoid writing settings twice.

## Per-node format selector

The node-specific JSON format Picker is on the **right of the Connection section heading**, next to the simple “JSON配置格式 / JSON format” label. The old format explanation card and version banner were removed. The V4/V5 value is still stored independently on each profile.

## Navigation

The sidebar groups the two global pages (Proxy settings, Routing & PAC) at the top, keeps the server list in the middle, and pins the node-specific **Server settings** action at the bottom. Clicking a server opens its editor without switching the running proxy. Shared scope reminders live on the right in the two global pages.

## Acceptance checklist on Windows

1. Create V4 and V5 nodes, switch between them, save, restart and confirm each retains its format.
2. Verify generated V4 uses `-format json` and V5 uses `-format jsonv5`.
3. Switch node with an unsaved edit; verify edits persist and wrong-format controls hide correctly.
4. At Windows 125%, 150%, 200% scaling and a narrow window, check that the language selector and both options remain visible and don't move when switching nodes.
5. Switch Chinese/English while connected and verify connection remains active.
6. Open both global pages and check shared-scope hints remain in the right content area.
7. For mixed-format nodes in balance/custom routing, verify one loader per generated file and receive explicit errors for incompatible options.

**Validation boundary:** Static inspection and Python checks do not verify Windows MAUI compilation or runtime rendering. Run `scripts/verify-windows.ps1 -SkipPublish` on Windows, then perform the manual tests above.
