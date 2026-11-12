# MAUI startup: StaticResource not found (HintLabelStyle)

`MainPage.xaml` uses `Style="{StaticResource HintLabelStyle}"`, and the style is
provided by `Resources/Styles/Theme.xaml`, merged from `App.xaml`.

## Root cause

The previous `App(MainPage page, ...)` constructor required the dependency
injection container to create `MainPage` *before* it entered the `App`
constructor. `MainPage.InitializeComponent()` therefore ran while
`Application.Resources` was not initialized, and its `StaticResource`
lookup failed even though the style existed in `Theme.xaml`.

## Fix

`App` now injects `IServiceProvider` instead. It calls `InitializeComponent()`
first and only then resolves `MainPage` via `services.GetRequiredService<MainPage>()`.
The single `Theme.xaml` resource dictionary is shared across all pages; avoid
copying these styles to page-level dictionaries to work around initialization.

## After updating

1. Close Visual Studio.
2. Delete `src/VelaRay.Maui/bin` and `src/VelaRay.Maui/obj` if they exist.
3. Open Visual Studio again, restore NuGet packages, rebuild and run on Windows.
4. Confirm that `MainPage` opens and `PacPreviewPage` can open without missing
   `StaticResource` exceptions.

`python scripts/static_check.py` verifies that all StaticResource references
resolve to defined theme keys and detects the original dependency-injection
initialization ordering regression. This static test is not a MAUI runtime test.
