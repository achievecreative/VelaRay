# Windows release acceptance

1. `python scripts/ten_round_audit.py` and `python scripts/static_check.py` succeed.
2. `dotnet run --project tests/VelaRay.SmokeTests -c Release` succeeds.
3. `dotnet run --project tests/VelaRay.WindowsLifetimeTests -c Release` succeeds.
4. Debug and Release `dotnet build src/VelaRay.Maui/VelaRay.Maui.csproj -f net10.0-windows10.0.19041.0` succeed.
5. `scripts/publish-windows.ps1 -Mode SingleFile` produces a standalone **frontend** EXE without bundling the V2Fly core.
6. Manually run on Windows: launch, switch Chinese/English, edit VMess/Trojan/Hysteria2 nodes, test V4/V5 generated configs using a real v2ray.exe, switch Global/PAC/Manual, then exit or kill the app and verify the child process is not left running.
7. Verify that neither the original WPF source files nor the retired old `settings.json` importer, UI button or translations are present.

Python static checks are *not* substitutes for C# compilation. Runtime, system proxy, and real connection verification are Windows-only acceptance items.
