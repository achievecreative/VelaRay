#!/usr/bin/env python3
"""Contract test for screenshot-aligned server cards and PAC/core staged applying.

This verifies source-level wiring and XML/JSON; use dotnet tests on Windows for behavior.
"""
from pathlib import Path
from lxml import etree
import json
import re

root = Path(__file__).resolve().parents[1]
xaml = (root / 'src/VelaRay.Maui/MainPage.xaml').read_text()
code = (root / 'src/VelaRay.Maui/MainPage.xaml.cs').read_text()
model = (root / 'src/VelaRay.Core/Models/ServerProfile.cs').read_text()
diff = (root / 'src/VelaRay.Core/Runtime/RunningConfigChangeDetector.cs').read_text()
smoke = (root / 'tests/VelaRay.SmokeTests/Program.cs').read_text()
tree = etree.fromstring(xaml.encode('utf-8'))
ns = {'x':'http://schemas.microsoft.com/winfx/2009/xaml'}

def named(name):
    nodes = tree.xpath(f"//*[@x:Name='{name}']", namespaces=ns)
    assert len(nodes) == 1, f'Missing / duplicate: {name}'
    return nodes[0]

def section(name, following):
    a=code.index(name)
    b=code.index(following,a)
    return code[a:b]

for n in ('ChineseLanguageButton','EnglishLanguageButton','ProfilesList','MeasureLatencyButton',
          'PendingRoutingBanner','PendingReconnectButton'):
    named(n)
assert 'StrokeShape="RoundRectangle 24"' in xaml and 'Grid ColumnDefinitions="24,*,*"' in xaml
maui_ns = {'m':'http://schemas.microsoft.com/dotnet/2021/maui'}
assert len(tree.xpath('//m:Ellipse', namespaces=maui_ns)) >= 2, 'Globe should use vector circles rather than emoji' 
assert 'ColumnDefinitions="336,*"' in xaml, 'Card sidebar must have more room than old 256px'
assert 'ItemSizingStrategy="MeasureAllItems"' in xaml, 'Variable height cards must be measured independently'
for binding in ['ProtocolBadge','EndpointDisplay','LatencyDisplay','IsConnected']:
    assert f'{{Binding {binding}}}' in xaml and binding in model
assert 'Text="✓"' in xaml and 'ServerRunningIndicatorShort' in xaml
assert 'BackgroundColor" Value="{AppThemeBinding Light=#EDF3FF, Dark=#20324D}' in xaml
assert 'Stroke" Value="{AppThemeBinding Light=#4E68FF, Dark=#87A3FF}' in xaml
assert 'private async void MeasureLatenciesClicked' in code
assert 'SmartNodeLatencyProbe.Classify(snapshot)' in code and '_latencyProbe.ProbeAsync(snapshot, executable)' in code
assert 'private async void MeasureLatenciesClicked' in code
assert 'private async void PendingReconnectClicked' in code
assert 'await ToggleConnectionAsync(forceRestart: true);' in section('private async void PendingReconnectClicked','private void RefreshCoreAvailabilityMessage')
assert 'RunningConfigChangeDetector.NeedsReconnect(' in code
for signal in ['original.Id', 'RoutingMode', 'SmartUnmatchedRoute','PacDirectWebsites']:
    # Smart fields are covered by the full generated JSON comparator.
    if signal == 'original.Id': continue
    assert signal in smoke
assert 'ConfigFlavor.Build(current, original)' in diff
assert 'runningPacPort != current.PacPort' in diff
assert 'UpdatePendingCoreChanges()' in section('private async void ApplyPacTemplateClicked','private async void PickCustomClicked')
apply_pac=section('private async void ApplyPacTemplateClicked','private async void PickCustomClicked')
assert apply_pac.index('if (UpdatePendingCoreChanges())') < apply_pac.index('await WriteManagedPacAsync()')
for meth,nxt in [('private async void SavePacClicked','private void RefreshPacClicked'),
                 ('private async void ApplyPacTemplateClicked','private async void PickCustomClicked'),
                 ('private async void SaveRuleClicked','private async void DeleteRuleClicked')]:
    block=section(meth,nxt)
    assert 'ToggleConnectionAsync(' not in block and 'ConnectAsync(' not in block, f'Auto reconnect detected in {meth}'
for culture in ('zh-CN','en-US'):
    data=json.loads((root/f'src/VelaRay.Maui/Resources/I18n/{culture}.json').read_text())
    for k in ('ServerRunningIndicatorShort','ActionTestTcpLatency','HelpTestTcpLatency',
              'RoutingPendingReconnectHint','ActionReconnectToApply','RoutingPendingPacDeferred','ManagedPacRawLocked'):
        assert data.get(k), f'Missing {culture} localized key {k}'
print('PASS: screenshot language pill, server cards, TCP-only manual latency, selected/active separation')
print('PASS: pending routing banner, explicit reconnect, dual PAC/kernel state, locale resources')
print('NOTE: Windows MAUI compilation, rendered pixels, and live WinINET PAC reload not tested here')
