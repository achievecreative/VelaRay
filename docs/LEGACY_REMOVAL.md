# Removal of original V2RayW client artifacts

Removed from the VelaRay source tree:

- `legacy/V2RayW-original.zip` and the entire `legacy/` folder.
- `src/V2RayW.Core/Migration/LegacySettingsImporter.cs` (renamed projects now use `VelaRay.*`, with no migration importer).
- The two old `settings.json` import UI actions and their event handler in `MainPage`.
- Obsolete import-specific locale strings in English and Chinese.
- Older settings migration/backup logic that silently modified unsupported Shadowsocks entries on launch.
- Migration-specific tests, historical review documents, and the obsolete core-bundling preparation script.
- Every old `V2RayW.*` project path, solution name, C# namespace and Windows package display name; these are now `VelaRay.*`.

Retained deliberately:

- V2Fly v2ray-core integration, V4 (`json`) and V5 (`jsonv5`) configuration format support.
- Current VelaRay settings persisted under `settings/settings.v5.json`.
- External V4/V5 core JSON selection and share-link/subscription parsing; these are not imports of the former application's `settings.json`.
- Windows system proxy and PAC, Windows Job Object lifetime, language support, and VelaRay logo/icons.

## Tests

- `python scripts/ten_round_audit.py` passed (10 source-level checks + independent PAC/XAML/locale/lexical checks).
- `python scripts/static_check.py` passed.
- `python scripts/check_csharp_structure.py` passed (lexical-only; not a compiler).
- `python scripts/check_pac_js.py` passed.
- `python scripts/audit_ux_localization.py` passed.
- `VelaRay.slnx` and renamed project references were verified by static examination.

**Not tested here:** Windows .NET 10 MAUI compilation, self-contained single-file publishing, or runtime proxy behavior. On Windows run `./scripts/verify-windows.ps1 -SkipPublish` and `./scripts/publish-windows.ps1 -Mode SingleFile`.
