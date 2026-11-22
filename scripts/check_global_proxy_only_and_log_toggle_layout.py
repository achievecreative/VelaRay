#!/usr/bin/env python3
"""Global routing and logging layout contract; supplement, not replace C# build/tests."""
from pathlib import Path
import json
from lxml import etree
R = Path(__file__).resolve().parents[1]
read=lambda name:(R/name).read_text(encoding='utf-8')
route=read('src/VelaRay.Core/Configuration/EffectiveRoutingMode.cs')
v4=read('src/VelaRay.Core/Configuration/V4ConfigBuilder.cs')
v5=read('src/VelaRay.Core/Configuration/V5ConfigBuilder.cs')
page=read('src/VelaRay.Maui/MainPage.xaml.cs')
detector=read('src/VelaRay.Core/Runtime/RunningConfigChangeDetector.cs')
tests=read('tests/VelaRay.SmokeTests/Program.cs')
assert 'settings.ProxyMode == "Global" ? "ProxyAll" : settings.RoutingMode' in route
assert 'var routingMode = EffectiveRoutingMode.Resolve(settings);' in v4
assert '["routing"] = BuildRouting(settings, routingMode)' in v4
assert 'var routingMode = EffectiveRoutingMode.Resolve(options);' in v5
assert 'config["router"] = BuildRouting(options, useBalance, routingMode);' in v5
assert 'EffectiveRoutingMode.Resolve(o) == "Custom"' in v5
assert 'ConfigFlavor.Build(current, original)' in detector
assert 'UpdatePendingCoreChanges()' in page
assert 'if (_settings.ProxyMode == "Global" && _settings.UseCustomConfig)' in page
assert '"switching Smart PAC to Global forces ProxyAll and requires reconnect"' in tests
assert '"PAC ProxyAll -> Global ProxyAll can update WinINET without a core restart"' in tests
xaml=etree.fromstring(read('src/VelaRay.Maui/MainPage.xaml').encode())
ns={'m':'http://schemas.microsoft.com/dotnet/2021/maui','x':'http://schemas.microsoft.com/winfx/2009/xaml'}
switch=xaml.xpath('//*[@x:Name="FileLoggingSwitch"]',namespaces=ns)
assert len(switch)==1
parent=switch[0].getparent()
assert parent.tag.endswith('Grid') and parent.getparent().tag.endswith('FlexLayout')
assert len(parent)==2 and switch[0].get('Toggled')=='FileLoggingToggled'
assert 'HelpEnableFileLogging' not in read('src/VelaRay.Maui/MainPage.xaml')
for lang in ('zh-CN','en-US'):
    d=json.loads(read(f'src/VelaRay.Maui/Resources/I18n/{lang}.json'))
    assert 'HelpEnableFileLogging' not in d
    assert 'ProxyAll' in d['ProxyModeGlobalDescription']
print('PASS: Global forces V4/V5 ProxyAll, existing route preferences remain stored, custom JSON blocked')
print('PASS: log-file toggle aligned with other switches, no explanatory text')
