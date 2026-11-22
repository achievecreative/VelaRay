# VelaRay 2.7 (.NET 11) — V4 TCP header JSON correction

## Reported failure

`Test failed: ... infra/conf/v4: Failed to build TCP config. > invalid TCP header config > type not found in JSON context`

A VMess + raw TCP profile can store an empty TCP header object (`{}`) or an empty string. The previous V4 generator always wrapped this in `tcpSettings.header`; the V2Ray V4 core tries to load any explicitly present header and requires its `type` discriminator.

## Corrected behavior

- Blank string, whitespace, `{}`, or `{"type":"none"}`: generate `tcpSettings: {}` (no header field).
- A non-empty header object with a `type` value: preserve as configured (for example, HTTP disguises).
- Non-empty header objects without a non-empty string `type`: raise a descriptive configuration error before invoking the core.
- The V5 raw-TCP builder uses the same normalization; existing custom transport behavior is otherwise unchanged.
- Clearing the TCP header editor now saves its default `type: none` setting, avoiding ambiguous blank data.

## Verification

Run on Windows with .NET 11 SDK, MAUI workload and a compatible V2Ray core:

```powershell
python scripts/check_v4_tcp_header_normalization.py
dotnet run --project tests/VelaRay.SmokeTests -c Release
.\scripts\verify-windows.ps1 -SkipPublish
```

After saving the affected server profile, re-generate `configs/config.v4.json` and run VelaRay's **检查配置**. The expected raw TCP segment is:

```json
"streamSettings": {
  "network": "tcp",
  "security": "none",
  "tcpSettings": {}
}
```

TLS (`security: tls`) is still valid when intentionally configured and supported by the server.

**Important:** Replacing the generated JSON on disk is temporary; VelaRay regenerates it. Fix the server profile/source instead. This source change has not been built with Windows MAUI nor tested against a live V2Ray binary in this environment.
