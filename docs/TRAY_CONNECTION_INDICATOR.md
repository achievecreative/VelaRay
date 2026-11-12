# Tray status and connected-server indicators

- Right-click the Windows tray icon to see a disabled status row with the **actual active connection** name(s), followed by Open, Connect/Disconnect and Exit. The status row is localized to English or Simplified Chinese.
- In the server list, a green check icon is visible only for the profile(s) loaded into the running V2Fly configuration, **not** just the selected editor row. It clears on normal disconnect and unexpected core exit.
- With `BalanceAllNodes`, all included profiles are marked; this means *included in the running outbound configuration*, not individually verified network reachability.
- When loading external V4/V5 JSON, the tray displays **Custom configuration** and no server row is marked, because the active upstream cannot be inferred from the selected editor profile.
- Runtime `ServerProfile.IsConnected` is excluded from JSON settings persistence (`[JsonIgnore]`).
- Deleting a profile used by the running configuration is blocked until disconnected.

## Windows regression checklist

1. Connect server A: A shows a green check and tray shows A.
2. Select server B without connecting: A stays green and tray still shows A.
3. Connect B: A indicator clears, B becomes green, tray shows B.
4. Switch UI language while connected: tray menu and tooltip show localized status.
5. Disconnect: no green badges, tray menu shows disconnected.
6. Kill v2ray.exe: no green badges after unexpected exit, tray disconnected.
7. If balancing multiple servers: all configured nodes show green indicators and names in tray menu.
8. If custom JSON is selected: tray shows Custom configuration, no node badge.
9. Attempt to delete a running server: UI prevents deletion until disconnect.

> Current non-Windows environment cannot run .NET MAUI / WinUI. The C# smoke test and Windows build must run on a Windows developer machine.
