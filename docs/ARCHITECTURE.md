# VelaRay architecture

```
VelaRay.Maui (Windows .NET 10 MAUI/WinUI)
    MainPage, locale resources, PAC/config viewers
    Windows-specific services: proxy, tray, startup, native titlebar and Job Object
         |
VelaRay.Core (.NET 10 cross-platform library)
    models, V4/V5 config builders, portable store, subscription share links,
    PAC templates, Geo data, v2ray-core process runner
         |
    user-managed V2Fly v2ray.exe + Geo data files
```

The `src` and `tests` namespaces/projects have been renamed to **VelaRay**. The original WPF source tree and legacy settings importer have been removed. V4 support is still required because it is a V2Fly configuration format, **not** an old application import feature.

The platform-neutral Core layer and `IPlatformServices` interfaces preserve a path to future macOS support. macOS is not currently built or tested.
