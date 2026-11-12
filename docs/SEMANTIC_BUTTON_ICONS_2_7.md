# VelaRay 2.7 — semantic button icons (visual-only update)

- Every existing operation button on MainPage, ConfigViewerPage and PacPreviewPage has a consistent 20dp vector icon to the left of its existing localized text.
- Icons use SVG inputs compiled by MAUI to PNG for app resources. No additional font files, platform-specific glyphs, libraries, network calls, settings fields or new commands.
- Connect / Switch / Disconnect always select a different icon by `ConnectionActionResolver` output: plug, arrows, power. The button text, action event, runtime logic and fixed width remain unchanged.
- Navigation and CRUD buttons get sliders, route, settings, plus, copy, share, trash, save, validation, refresh, search, download and file icons, as appropriate.
- The language segmented control keeps text-only buttons; its existing globe icon remains present, without duplicating pictograms on the two language choices.
- User-facing design changes only: the underlying Clicked event names and code remain the same.

## Verification

Run `python scripts/check_semantic_action_icons.py` and the preexisting source-level regression scripts. Build with Windows .NET MAUI SDK before distributing binaries. Resource conversion and final native rendering must be validated in a Windows build.
