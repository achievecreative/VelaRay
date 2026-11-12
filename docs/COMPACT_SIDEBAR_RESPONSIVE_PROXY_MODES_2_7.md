# VelaRay 2.7: Sidebar and proxy-mode layout refinement

This update is a **UI-only revision** of `MainPage.xaml` and the view's layout event handlers. It does not alter the routing engine, PAC generation, core connections, persisted settings, or the existing proxy selection and apply events.

## Workspace navigation

- `代理设置 / Proxy Settings`, `路由与 PAC / Routing & PAC`, and `服务器设置 / Server Settings` continue to use the original buttons and click handlers.
- On Windows/WinUI, `HorizontalContentAlignment = Left` aligns both icon and text to the **left edge** inside the full-width navigation button. `ContentLayout="Left,6"` still puts the icon to the left of its text.
- Non-Windows MAUI adapters need equivalent native alignment when those platforms are implemented.

## Server cards

- Remove the two fixed `MinimumHeightRequest=92` constraints and shrink row spacing and vertical padding.
- Name remains the only bold text. Protocol badge, endpoint, latency, and "in use" are smaller, regular-weight text.
- Right side has **5 device-independent units** of margin before the list scroll bar; bottom margin is 6 units.
- Blue selection state stays distinct from the actual running-node marker. Selecting a server does not switch the running connection.

## System proxy modes

- Keep the original hidden `ProxyModePicker` as the single source of truth and keep the existing `ProxyModeCardCheckedChanged` and card-tap handlers.
- Replace the fixed 240-unit `FlexLayout.Basis` cards with a `Grid` that reflows with its actual measured width. Layout only:
  - Width >= 830: 3 columns.
  - Width >= 540 and < 830: 2 columns, with the third card on the next row.
  - Width < 540: 1 column.
- Titles and descriptions use wrapped `Label` text so English and Chinese strings are not cut off by the native `RadioButton` caption.
- The PAC/Global/Manual `RadioButton`s retain their values, group, and CheckedChanged event handlers. Clicking a card changes only the pending selection, as before.

## Verification

Run `python scripts/check_compact_sidebar_and_proxy_modes.py`, then the project's `scripts/verify-windows.ps1 -SkipPublish`. Static checks do **not** establish that the Windows MAUI application compiles or renders correctly; Windows testing is still required at 800, 1024, 1366 pixels and in both languages.
