# Viewing the generated and running JSON configurations (Windows)

The application now distinguishes *generated preview* from *running configuration*.

| Action | Location | Content |
|---|---|---|
| **查看配置** | Main window footer | Currently running config when connected; otherwise generates a preview |
| **查看生成配置** | 应用设置 | Always regenerates JSON from the current form values or reads the selected external V5 config |
| **查看运行配置** | 顶部工具栏 / 底部状态栏 | Reads the independent snapshot passed to v2ray-core; requires an active connection |
| **导出 JSON V5** | 应用设置 | Generates/saves JSON and shows its path, without starting v2ray-core |

The viewer is read-only. It offers pretty-printed/raw display, copying the original bytes as text, and an Explorer action that highlights the file. It warns about clear-text credentials. The viewer uses a bounded, vertically scrollable JSON area, so large configurations do not enlarge the dialog or hide the bottom actions. A vertical scrollbar is always visible, and long lines wrap rather than being truncated. Use the **Copy JSON** button to copy text; the read-only display is not a text-selection editor.

## Files

- `<VelaRay.exe folder>/configs/config.v5.json`: latest automatically generated config (when using the built-in generator)
- `<VelaRay.exe folder>/configs/config.active.v5.json`: snapshot of the **exact file passed to** `v2ray.exe run -format jsonv5 -c ...`
- External V5: the selected file remains unchanged; connecting creates an app-owned snapshot in `config.active.v5.json`.

The precise app-data root may vary with Windows app packaging; use the viewer's displayed path or the app's **打开数据目录** button instead of assuming a fixed filesystem location.

Changing settings while connected does not update the current v2ray-core process. Use **连接 / 切换** to apply changes; this creates a new active snapshot.

## Scrolling acceptance (V4 and V5)

1. Open **查看运行配置** with a file containing hundreds of lines; confirm the bottom action bar remains visible and the JSON region scrolls independently.
2. Confirm the vertical scrollbar is visible and the end of the JSON is reachable.
3. Switch between formatted and original display. Verify long unbroken lines wrap and do not disappear, and the **复制原始 JSON** button still copies the complete original string.
4. Repeat with **查看生成配置**. Both commands share `ConfigViewerPage`.

Static check: `python scripts/check_config_viewer_scroll.py`. The on-screen scrolling behavior still requires a Windows MAUI runtime test.

## Windows acceptance tests

1. Disconnect, edit a node, click 查看配置; confirm displayed path and the generated protocol/transport/password.
2. Toggle 格式化显示 off; verify the original JSON is shown and copying still returns the original bytes.
3. Click 在资源管理器中定位; verify Explorer selects the exact config file.
4. Connect; click 查看配置 and verify the `config.active.v5.json` path matches the `-c` argument of the core process.
5. Edit a node or preview a generated JSON while still connected; verify 查看运行配置 continues showing the unchanged snapshot.
6. For an external V5 JSON, connect, change the external file, verify the snapshot stays unchanged until reconnect.
7. Do not post complete JSON from a real account: passwords, UUIDs and TLS parameters may be present.

Only XML/JSON and source-level checks have been run in the handoff environment. Build, MAUI rendering, Explorer navigation, and real V2Fly runtime behavior still require Windows testing.
