#!/usr/bin/env python3
"""Source-level regression for VMess + plain TCP defaults and V4/V5 output.

This is not a substitute for Windows .NET compilation or an actual VMess connection.
"""
from pathlib import Path
r=Path(__file__).resolve().parents[1]
ui=(r/'src/VelaRay.Maui/MainPage.xaml.cs').read_text(encoding='utf-8')
flavor=(r/'src/VelaRay.Core/Configuration/ConfigFlavor.cs').read_text(encoding='utf-8')
v4=(r/'src/VelaRay.Core/Configuration/V4ConfigBuilder.cs').read_text(encoding='utf-8')
v5=(r/'src/VelaRay.Core/Configuration/V5ConfigBuilder.cs').read_text(encoding='utf-8')
tests=(r/'tests/VelaRay.SmokeTests/Program.cs').read_text(encoding='utf-8')
assert 'ProfileCapabilities.ProtocolSwitchDefaults(' in ui
assert 'TransportPicker.SelectedItem = transport;' in ui
assert 'TlsSwitch.IsToggled = tlsEnabled;' in ui
assert 'previousProtocol == "hysteria2"' in flavor
assert 'return ("tcp", false)' in flavor
assert 'return (previousTransport, tlsEnabled);' in flavor
assert '"tcp" => "tcpSettings"' in v4
assert '"tcp" => BuildTcpTransportSettings(p)' in v5
assert 'return new JsonObject();' in v5
assert 'V4 VMess/TCP emits raw TCP, no TLS and the correct UUID' in tests
assert 'V5 VMess/TCP emits raw TCP transport, no TLS and the correct UUID' in tests
print('VMess/TCP protocol-switch and configuration source contracts: OK')
