# Proxy inbound port layout — VelaRay 2.7 / .NET 11

## Change

In **Proxy Settings → Local Proxy**, the existing HTTP, SOCKS5 and PAC-only HTTP proxy inbound port cards now share the same three-column row. All three ports remain editable, with unchanged `x:Name` and C# bindings:

| Card | Field | Default |
| --- | --- | --- |
| HTTP | `HttpPortEntry` | 1088 |
| SOCKS5 | `SocksPortEntry` | 1080 |
| PAC-only HTTP | `PacProxyPortEntry` | 1089 |

The **PAC script HTTP service** is a separate setting (`PacPortEntry`, default 18888) below those three proxy inbound cards. Its purpose is to serve PAC scripts, not to forward website traffic.

The only changes to existing application files are `src/VelaRay.Maui/MainPage.xaml` and a shorter localized note (`HelpPacProxyPort`) in the two language JSON files. No C# logic has been modified.

## Verification

- Run `python scripts/check_three_proxy_ports_one_row.py`.
- Run the normal project checks and build with .NET 11 MAUI on Windows.
- In the UI, verify all three input cards remain on one row, with readable labels and editable ports, and the PAC script service setting is below.

Static XAML tests pass, but actual Windows MAUI build/render behavior has not been verified here.
