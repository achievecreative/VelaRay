# VelaRay 2.7 — system proxy mode, PAC template, and server card spacing

## Layout

- Native `ProfilesList` uses `LinearItemsLayout.ItemSpacing = 2` (2 MAUI device-independent units).
- Data template reserves 18 units outside the card border, approximately 8 for the Windows overlay scrollbar plus **10** visible clearance. The exact physical gap varies with Windows overlay scrollbar / DPI behavior.
- Server card minimum height remains 78, with 12px horizontal and 11px vertical padding. Name and address remain left-aligned.

## The two distinct layers

- **Windows system proxy**: PAC, Global or Manual decides whether a browser request enters the V2Fly inbounds at all.
- **V2Fly routing**: In PAC/Manual, SmartUnmatchedRoute and the selected route determine the outbound after traffic enters V2Fly. **In Global, VelaRay-generated V4/V5 config forces ProxyAll** while preserving the saved PAC/Manual policy.

Previous releases silently changed the generated PAC default from DirectByDefault to ProxyByDefault if SmartUnmatchedRoute was Proxy, making Global→PAC appear equivalent to Global. That behavior is **removed**. The selected PAC template always wins (DirectByDefault, ProxyByDefault, ChinaSitesDirect); explicit PAC site lists still take priority. The user may still explicitly select ProxyByDefault, in which case unrelated websites are sent to V2Fly by design.

## Switching modes while V2Fly is running

- Selecting a PAC/Global/Manual card **alone** never edits Windows proxy state and never reconnects.
- Saving **proxy settings** while connected changes Windows WinINET without restarting if the generated core JSON is unchanged.
- If a mode-only change alters generated V4/V5 routing (for example, Smart PAC → Global ProxyAll), Save validates the replacement and automatically restarts the *previously running* node, then updates Windows WinINET. It does not switch to another node selected for editing.
- If advanced route edits, PAC domain changes or other core settings are also pending, Save **does not auto-restart**. The pending banner remains visible until the user explicitly chooses **Reconnect and apply**. No Windows Global mode is enabled against an incompatible running core.
- Changing modes on a stopped connection takes effect on the next Connect.
- On full disconnect, VelaRay restores the prior external Windows proxy state (rather than blindly disabling a corporate proxy).
- No change to custom PAC script semantics. Browsers may cache previously compiled PAC scripts or existing connections; the next fresh request/browser refresh can be necessary (not proof that VelaRay still uses Global).

## Suggested Windows verification

1. Set Smart unmatched=Proxy, PAC template=DirectByDefault; set `google.com` as PAC proxy exception.
2. Connect in PAC. Select Global and save. If Smart routing differs from ProxyAll and no other settings are pending, expect one automatic core restart and WinINET switching to Global. Confirm the running node does not change even if another node is selected for editing.
3. Run in PowerShell:
   `Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' | Select ProxyEnable,ProxyServer,AutoConfigURL`
   Expected when PAC is active: `ProxyEnable=0`, no manual ProxyServer, `AutoConfigURL=http://127.0.0.1:<PAC-port>/proxy.pac`.
4. Fetch `http://127.0.0.1:<PAC-port>/proxy.pac`, verify the unlisted-domain fallback is `DIRECT` and `google.com` is proxied.
5. Confirm Global sets `ProxyEnable=1`, clears `AutoConfigURL`, and uses proxy-only V2Fly routing. Saving PAC restores the selected routing strategy through a mode-only restart when needed; Manual restores the original Windows proxy settings when switching away from a managed mode.
6. Edit advanced Smart routing while connected, save; expect a pending reconnect banner and no automatic core restart or premature PAC replacement.

Note: Static tests cannot prove WinINET/Chrome UI behavior on Windows; real WinUI + browser testing remains necessary.

Read-only diagnostic: run `powershell -ExecutionPolicy Bypass -File .\scripts\diagnose-windows-pac.ps1` in Windows. It prints current WinINET settings and the selected local PAC script default, without changing Windows settings or the V2Fly process.
