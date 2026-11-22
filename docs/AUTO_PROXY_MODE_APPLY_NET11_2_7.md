# VelaRay 2.7 — Save-driven PAC / Global / Manual automatic apply (.NET 11)

## Why the old version required a manual reconnect

Global forces `ProxyAll` in generated V4/V5. The existing mode-switch handler would check the generated JSON, notice the route had changed, and exit without updating the live Windows proxy or restarting the running core. Users had to disconnect and reconnect manually.

## New behavior

1. Select **PAC**, **Global**, or **Manual** and click **Save settings**. Merely selecting a card remains non-disruptive.
2. If the existing V2Fly JSON remains correct, update only Windows WinINET / PAC on the live connection. Do not restart V2Fly.
3. If the change of proxy mode **alone** changes effective V2Fly routing (typically entering/leaving Global), prevalidate a temporary V4/V5 config using the **running** executable, then automatically stop and restart V2Fly with **the originally running node**. Apply Windows WinINET only after local HTTP/SOCKS listeners are ready.
4. If another core setting or hidden Smart/Custom/PAC draft is pending, **do not** auto-restart or activate it. Keep the existing pending banner and offer **Reconnect and apply**.
5. External custom core JSON continues to be blocked in Global, as VelaRay cannot promise that external routing is ProxyAll.
6. When disconnected, saving chooses the mode for the next connection.

**Important:** A V2Fly routing change cannot be hot reloaded. Automatic restarts still cause a brief interruption; this eliminates the *manual* disconnect/reconnect steps, not the underlying restart.

## Safeguards

- Do not use the currently selected editor node in an automatic restart. Use the actual active profile id and executable.
- Do not apply any unapplied Smart or custom routing edits just because Global's current ProxyAll JSON hides them.
- Validate replacement config before stopping a live core. If prevalidation fails, leave the old core running and show an error.
- Create an isolated staging config and remove it afterward.
- Keep current PAC generation, Windows proxy ownership/restore and existing manual Reconnect and apply behavior.

## Verify on Windows

```powershell
.\scripts\verify-windows.ps1 -SkipPublish
```

Manual acceptance tests:

1. Connect in **PAC / Smart**, select **Global**, click Save. Verify V2Fly automatically restarts to ProxyAll on the **same server** and WinINET becomes `ProxyEnable=1` and `AutoConfigURL` is absent.
2. Select **PAC**, click Save. Verify the previous Smart route returns, WinINET becomes `ProxyEnable=0` and `AutoConfigURL` points to `/proxy.pac`.
3. Select **Manual**, click Save. Verify Windows proxy settings are restored and local SOCKS/HTTP remain active.
4. Connect in **PAC / ProxyAll** and switch to **Global**; no V2Fly restart should be needed.
5. While connected, select another node *only for editing*. Switch mode and Save; verify the old active node stays connected across any automatic restart.
6. While Global runs, edit an advanced route or PAC domain list, then select PAC and Save; automatic restart must be blocked pending explicit **Reconnect and apply**.
7. Verify invalid/unsupported replacement configs are rejected before the old V2Fly core is stopped.
8. Verify both V4 and V5, in both languages; check status banner, current node, tray, and system proxy on resume after sleep.

Diagnostic (read-only):

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' |
  Select-Object ProxyEnable, ProxyServer, AutoConfigURL
```

**Validation boundary:** Source-level checks run outside Windows, but the current generation environment does not include the .NET 11 SDK or Windows/WinUI. Windows compilation and actual WinINET behavior must be verified on a Windows machine.
