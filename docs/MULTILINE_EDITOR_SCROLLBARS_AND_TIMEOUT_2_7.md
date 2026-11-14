# VelaRay 2.7 — Editor scrollbars and probe failure display

## Overflowing multiline inputs

The Windows MAUI `Editor` uses a WinUI `TextBox`, with its own embedded `ScrollViewer`.
In `MauiProgram.CreateMauiApp`, one Windows-only Editor handler mapping sets
`ScrollViewer.VerticalScrollBarVisibility` to `Auto`. The scrollbar appears only
if content exceeds the visible space, using the existing native editing surface.
No external/nested `ScrollView` was added and text editing, clipboard and
binding behavior are unchanged.

The mapping covers all MAUI Editors, notably:

- PAC direct/proxy website lists (125dp high each)
- Editable PAC JavaScript (200dp)
- Custom routing domain and IP lists (104dp each)
- Raw routing JSON (220dp; now explicitly disables auto-resize)
- Read-only built-in route JSON and PAC preview editors
- WebSocket and TCP header JSON editors

The main workspace's own scrolling and the separate log panel's behavior are unchanged.

## Smart node probe failure

A non-successful TCP or UDP probe shows **超时** (English: **Timeout**)
with red text on the server card. Success still displays measured latency.
The underlying diagnostic failure details remain available separately.

## Verification

Run `python scripts/check_multiline_editor_scrollbars.py` and
`python scripts/check_smart_node_latency.py`, followed by
`./scripts/verify-windows.ps1 -SkipPublish` on Windows with .NET 10 MAUI.

Manual checks: add 30+ lines to both PAC domain lists, PAC JS, routing domain/IP
lists and raw JSON; confirm the vertical scrollbar appears when overflowing,
works with mouse wheel/drag, disappears when content fits, and does not change
saved values. Test Windows 100%/150% scaling and light/dark mode. Verify a failed
TCP/UDP probe displays red Timeout with both language settings.

This environment cannot certify Windows MAUI compilation or WinUI rendering.
