#!/usr/bin/env python3
"""Source contract for three configurable, separate proxy listeners."""
from pathlib import Path
import json
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
def source(path): return (root / path).read_text(encoding='utf-8-sig')
model=source('src/VelaRay.Core/Models/ServerProfile.cs')
policy=source('src/VelaRay.Core/Configuration/ExplicitProxyPolicy.cs')
v4=source('src/VelaRay.Core/Configuration/V4ConfigBuilder.cs')
v5=source('src/VelaRay.Core/Configuration/V5ConfigBuilder.cs')
xaml=source('src/VelaRay.Maui/MainPage.xaml')
code=source('src/VelaRay.Maui/MainPage.xaml.cs')
store=source('src/VelaRay.Core/Runtime/ClientSettingsStore.cs')
ET.fromstring(xaml)
assert 'public int SocksPort { get; set; } = 1080;' in model
assert 'public int HttpPort { get; set; } = 1088;' in model
assert 'public int PacProxyPort { get; set; } = 1089;' in model
assert 'ForceExplicitProxy' not in model
assert '"http-pac-in"' in policy
assert 'new("socks-in", "http-in")' in policy
assert 'rules.Insert(0, ExplicitProxyPolicy.V4Rule' in v4
assert 'rules.Insert(0, ExplicitProxyPolicy.V5Rule' in v5
assert '"127.0.0.1"' in v4 and '"127.0.0.1"' in v5
assert 'ExplicitProxyPolicy.PacHttpTag' in v4 and 'ExplicitProxyPolicy.PacHttpTag' in v5
assert 'localPorts.Distinct().Count()' in v5
assert 'PacProxyPortEntry' in xaml and 'ForceExplicitProxySwitch' not in xaml
assert '_settings.PacProxyPort = Port(PacProxyPortEntry.Text' in code
assert 'PacTemplateGenerator.Generate' in code and '_settings.PacProxyPort' in code
assert '_runningPacProxyPort' in code and 'context.PacProxyPort' in code
assert 'props.TryGetProperty("ExplicitHttpPort"' in store
assert 'CoreTrafficStatistics.FindLoopbackPort' in code
for lang in ['zh-CN','en-US']:
    data=json.loads(source(f'src/VelaRay.Maui/Resources/I18n/{lang}.json'))
    for key in ['FieldPacProxyPort','PlaceholderPacProxyPort','HelpPacProxyPort']:
        assert data[key]
    assert not any(k in data for k in ['ToggleForceExplicitProxy','FieldExplicitHttpPort'])
print('PASS: 1080 SOCKS5 / 1088 manual HTTP / 1089 PAC HTTP, all editable with separate script port')
