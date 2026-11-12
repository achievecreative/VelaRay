# Portable storage (VelaRay)

Root: the physical directory containing the distributed `VelaRay.exe`, determined from `Environment.ProcessPath`.

```
VelaRay/
├── VelaRay.exe
├── v2ray-core/
│   ├── v2ray.exe
│   ├── geosite.dat
│   └── geoip.dat
├── settings/
│   ├── settings.v5.json
│   └── wininet-proxy-backup.json
├── configs/
│   ├── config.v4.json
│   ├── config.v5.json
│   ├── config.active.v4.json
│   ├── config.active.v5.json
│   └── custom/
├── pacs/
└── logs/
```

VelaRay **does not** read or import another application's `settings.json`. Settings storage is specific to VelaRay. Users can still select external V4/V5 *core configuration files* under the custom config flow; that is not a full application settings importer.

The directory must be writable by the current Windows user, so `Program Files` is not suitable for this portable layout without a separate data-directory strategy.
