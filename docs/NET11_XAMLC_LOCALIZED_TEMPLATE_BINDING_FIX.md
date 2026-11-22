# .NET MAUI 11 XamlC — server card localized binding fix

## Compiler error

`Binding: Unsupported indexer index type: "global::VelaRay.Core.Models.ServerProfile".`

## Root cause

`MainPage.xaml` has a `CollectionView` `DataTemplate x:DataType="models:ServerProfile"`.
The **running** status label used `{Binding [ServerRunningIndicatorShort], Source={x:Static loc:UiStrings.Instance}}`.
The typed template's `ServerProfile` context conflicts with the `UiStrings` indexer under the .NET MAUI 11 XAML compiler.

## Fix

- Give the one status label its own `BindingContext="{x:Static loc:UiStrings.Instance}"` and `x:DataType="loc:UiStrings"`.
- Replace indexer syntax with a normal `{Binding ServerRunningIndicatorShort}` binding.
- Expose `UiStrings.ServerRunningIndicatorShort` as a typed property backed by the existing localized resource key.
- Keep the parent `DataTemplate x:DataType="models:ServerProfile"` so server name, address, protocol and latency remain compiled bindings.
- Reuse existing `UiStrings.ChangeLanguage` `PropertyChanged(null)` notification so language switching still updates the status text.

No proxy, server selection, routing, PAC or traffic logic changed.

## Verification

Run static check:

```powershell
python scripts/check_net11_localized_template_binding.py
```

Then run a real Windows build (requires .NET 11 SDK + MAUI Windows workload):

```powershell
dotnet clean src/VelaRay.Maui/VelaRay.Maui.csproj -c Debug -f net11.0-windows10.0.19041.0
dotnet build src/VelaRay.Maui/VelaRay.Maui.csproj -c Debug -f net11.0-windows10.0.19041.0
```

Test on screen: select a server, connect it, verify "使用中"/"In use" renders; switch language while connected; select a different server and confirm the active marker still points to the actual running server.

No Windows MAUI build was performed in the source preparation environment.
