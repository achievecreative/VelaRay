# VelaRay 2.7 — Read-only routing overview and 10dp card gutter

## Server list
- Independent rightmost `Grid` column: **10dp**, reduced from 18dp.
- Card minimum height: **78dp**, unchanged.
- Native `CollectionView` item spacing: **2dp**, unchanged.
- The 10dp column is outside the card border, not a guaranteed 10dp *visible* distance from a scrollbar. Native scrollbar width and placement vary by WinUI/DPI/theme.

## Routing and PAC page
- All four built-in policies (`Smart`, `ProxyAll`, `Direct`, `BypassCN`) appear in a read-only gallery.
- Their generated V4/V5 routing JSON can be inspected but not edited or activated from this page.
- Custom routing sets remain editable and savable without activating them. The visual-rule button is labeled **保存规则 / Save rule** to avoid confusing draft edits with deployment.
- The actual routing policy is selected under **Proxy settings**. Applying a custom routing snapshot is an explicit action there; pending core changes still require manual reconnect.

## Verification
Run `python scripts/check_builtin_routing_readonly_and_10dp_gutter.py` and `python scripts/audit_ux_localization.py` as part of the source checks. Source checks do not prove Windows MAUI compilation or native scrollbar rendering.
