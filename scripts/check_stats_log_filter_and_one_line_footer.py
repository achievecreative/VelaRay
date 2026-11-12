#!/usr/bin/env python3
"""Guard UI-only one-line footer and internal stats log suppression.

Checks the source contract; a Windows build/render test is still required.
"""
from pathlib import Path
from lxml import etree

root = Path(__file__).resolve().parents[1]
page = root / 'src/VelaRay.Maui/MainPage.xaml.cs'
xaml = root / 'src/VelaRay.Maui/MainPage.xaml'
source = page.read_text(encoding='utf-8')
ns={'x': 'http://schemas.microsoft.com/winfx/2009/xaml',
    'm': 'http://schemas.microsoft.com/dotnet/2021/maui'}
tree=etree.parse(str(xaml))

def named(name):
    result=tree.xpath(f'//*[@x:Name="{name}"]',namespaces=ns)
    assert len(result)==1,(name,len(result))
    return result[0]

center = named('UploadSpeedLabel').getparent().getparent()
assert center.tag.endswith('Grid') and center.attrib.get('Grid.Column')=='1'
assert center.attrib.get('ColumnDefinitions')=='Auto,Auto,*,Auto'
assert not center.attrib.get('RowDefinitions'), 'Stats should be a single row'
assert center.attrib.get('HorizontalOptions')=='Fill'
for name,col in [('UploadSpeedLabel','0'),('DownloadSpeedLabel','1')]:
    control=named(name)
    assert control.attrib.get('MaxLines')=='1' and control.attrib.get('LineBreakMode')=='NoWrap'
    assert control.getparent().attrib.get('Grid.Column')==col
for name,col in [('TrafficTotalsLabel','2'),('TrafficAvailabilityLabel','3')]:
    control=named(name)
    assert control.attrib.get('Grid.Column')==col
    assert control.attrib.get('MaxLines')=='1'
    assert control.attrib.get('LineBreakMode')=='TailTruncation'
for name in ('UploadSpeedLabel','DownloadSpeedLabel','TrafficTotalsLabel','TrafficAvailabilityLabel'):
    assert named(name) in list(center.iter())

append = source.split('private void AppendLog(string line)',1)[1].split('private void StartLogRefreshTimer()',1)[0]
needle='tcp:127.0.0.1:0 [velaray-stats-api]'
assert needle in append
assert 'StringComparison.OrdinalIgnoreCase' in append
assert append.index('line.Contains(') < append.index('_runtimeLogs.Append(line)')
assert '_core.LogReceived += AppendLog;' in source
assert '_runtimeLogs.Append(line);' in append, 'Other log messages must still be retained'
print('PASS: stats API access log suppressed before storage; centered traffic remains one line')
