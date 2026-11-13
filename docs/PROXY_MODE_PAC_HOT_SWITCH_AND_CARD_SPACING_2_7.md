# VelaRay 2.7 — system proxy mode, PAC template, and server card spacing

## Layout

- Native `ProfilesList` uses `LinearItemsLayout.ItemSpacing = 2` (2 MAUI device-independent units).
- Data template reserves 18 units outside the card border, approximately 8 for the Windows overlay scrollbar plus **10** visible clearance. The exact physical gap varies with Windows overlay scrollbar / DPI behavior.
- Server card minimum height remains 78, with 12px horizontal and 11px vertical padding. Name and address remain left-aligned.

## The two distinct layers

- **Windows system proxy**: PAC, Global or Manual decides whether a browser request enters the V2Fly inbounds at all.
- **V2Fly routing**: SmartUnmatchedRoute determines which outbound traffic chooses **after** it enters V2Fly.

Previous releases silently changed the generated PAC default from DirectByDefault to ProxyByDefault if SmartUnmatchedRoute was Proxy, making Global→PAC appear equivalent to Global. That behavior is **removed**. The selected PAC template always wins (DirectByDefault, ProxyByDefault, ChinaSitesDirect); explicit PAC site lists still take priority. The user may still explicitly select ProxyByDefault, in which case unrelated websites are sent to V2Fly by design.

## Switching modes while V2Fly is running

- Selecting a PAC/Global/Manual card **alone** never edits Windows proxy state and never reconnects.
- Saving **proxy settings** while connected changes only Windows WinINET proxy mode and the local PAC listener when the running core config and required listener settings are unchanged; **no core restart needed**. The core itself continues to run.
- Saving when a V2Fly routing/config change is pending **does not** apply the new PAC/mode out of sync. The existing pending banner remains visible until the user clicks **Reconnect and apply**.
- Changing modes on a stopped connection takes effect on the next Connect.
- On full disconnect, VelaRay restores the prior external Windows proxy state (rather than blindly disabling a corporate proxy).
- No change to custom PAC script semantics. Browsers may cache previously compiled PAC scripts or existing connections; the next fresh request/browser refresh can be necessary (not proof that VelaRay still uses Global).

## Suggested Windows verification

1. Set Smart unmatched=Proxy, PAC template=DirectByDefault; set `google.com` as PAC proxy exception.
2. Connect in Global. Save PAC (with unchanged local ports and core settings) and verify running V2Fly process/PID is unchanged.
3. Run in PowerShell:
   `Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' | Select ProxyEnable,ProxyServer,AutoConfigURL`
   Expected when PAC is active: `ProxyEnable=0`, no manual ProxyServer, `AutoConfigURL=http://127.0.0.1:<PAC-port>/proxy.pac`.
4. Fetch `http://127.0.0.1:<PAC-port>/proxy.pac`, verify the unlisted-domain fallback is `DIRECT` and `google.com` is proxied.
5. Switch back to Global, save, confirm `ProxyEnable=1` and no `AutoConfigURL`. Switch to Manual, save, confirm the original Windows proxy settings were restored.
6. Edit advanced Smart routing while connected, save; expect a pending reconnect banner and no automatic core restart or premature PAC replacement.

Note: Static tests cannot prove WinINET/Chrome UI behavior on Windows; real WinUI + browser testing remains necessary.

Read-only diagnostic: run `powershell -ExecutionPolicy Bypass -File .\scripts\diagnose-windows-pac.ps1` in Windows. It prints current WinINET settings and the selected local PAC script default, without changing Windows settings or the V2Fly process.
