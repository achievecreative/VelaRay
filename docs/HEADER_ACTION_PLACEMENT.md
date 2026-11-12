# Header toolbar action placement (VelaRay 2.7 patch)

The **View config** and dynamic **Connect / Switch / Disconnect** buttons have been moved out of the space below the VelaRay title and into the **far-right end of the top toolbar**. They sit on the same toolbar row as the logo, product title, current running server and status, and language selector.

- Left: **VelaRay logo and title only**. No action buttons below the brand.
- Middle: **Running server name and connection status**, followed by language selection.
- Right: **View config** and **Connect / Switch / Disconnect** side by side, with the connection action as the primary button.
- Footer: existing action bar and diagnostic log remain unchanged.

This is a XAML-only placement change: event handlers, per-node selection, connection action state machine, core update controls, localization, and runtime behavior are unchanged.

## Verification

```bash
python scripts/check_header_action_placement.py
```

On Windows, also run `scripts/verify-windows.ps1 -SkipPublish` with .NET 10 and MAUI workload, and visually inspect the header at 800px, 1024px and 1366px window widths, on 100%/150%/200% scale, and in both languages. A static XML check does not prove layout at all DPI settings.
