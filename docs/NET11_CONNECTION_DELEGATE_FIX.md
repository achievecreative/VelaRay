# VelaRay 2.7 — .NET 11 delegate and event-nullability fix

## Fixes

- `MainPage.xaml.cs` `ConnectionActionClicked`: replace the conditional expression between method groups with two explicit branches, each passing a parameterless `() => Task` lambda to `RunSafelyAsync(Func<Task>, string)`. `ConnectAsync` takes optional parameters, but its method group is not convertible to `Func<Task>`. The selected action is captured once and the original error messages / busy-state handling are unchanged.
- `ProxyModeCardCheckedChanged`, `PacProxyModeCardTapped`, `GlobalProxyModeCardTapped`, `ManualProxyModeCardTapped`: declare `object? sender` to match MAUI 11's nullable event delegate contract, removing the reported CS8622 warnings.

## Source review

Run `python scripts/check_net11_connection_delegate.py` or include it in `scripts/verify-windows.ps1`.

## Real build verification (Windows)

```powershell
dotnet build src/VelaRay.Maui/VelaRay.Maui.csproj -c Release -f net11.0-windows10.0.19041.0
```

No changes were made to network, proxy, PAC, node selection, or routing behavior.

## 2026-11-15 follow-up: robust explicit-branch fix

The connection-action event now calls `RunSafelyAsync(() => DisconnectAsync(), ...)` or
`RunSafelyAsync(() => ConnectAsync(), ...)` in separate `if`/`else` blocks. This ensures
there is no target-typed conditional expression involving method groups in this event,
and the existing `finally` continues to restore the UI busy state.

Confirm the local working tree contains the fix (not an older source copy):

```powershell
Select-String -Path src/VelaRay.Maui/MainPage.xaml.cs -Pattern 'RunSafelyAsync\(\(\) => (ConnectAsync|DisconnectAsync)\(\)'
```

If Windows still reports the previous error on line 1529, verify which working tree
is being compiled, remove the MAUI project's `bin`/`obj` directories and rebuild.
