#!/usr/bin/env python3
"""Source-level guarantees. Run C# tests and MAUI builds on Windows separately."""
from pathlib import Path
import json
import xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[1]
xml = (root / 'src/VelaRay.Maui/MainPage.xaml').read_text(encoding='utf-8')
cs = (root / 'src/VelaRay.Maui/MainPage.xaml.cs').read_text(encoding='utf-8')
stat = (root / 'src/VelaRay.Core/Runtime/CoreTrafficStatistics.cs').read_text(encoding='utf-8')
ET.fromstring(xml)
assert 'MinimumHeightRequest="78"' in xml
assert 'RowSpacing="6" Padding="12,11,12,11"' in xml
assert 'LinearItemsLayout Orientation="Vertical" ItemSpacing="2"' in xml
assert 'Grid ColumnDefinitions="*,10" HorizontalOptions="Fill"' in xml
for name in ('UploadSpeedLabel','DownloadSpeedLabel','TrafficTotalsLabel','TrafficAvailabilityLabel'):
    assert f'x:Name="{name}"' in xml and name in cs, name
assert 'CoreTrafficStatistics.Enable(configJson, SelectedConfigVersion, candidate)' in cs
assert 'await File.WriteAllTextAsync(activePath, configJson);' in cs
assert 'if (!launchedExternal)' in cs
assert 'var configJson = await File.ReadAllTextAsync(activePath);' in cs
assert '_runningConfigSnapshot = configJson' in cs
assert 'TimeSpan.FromSeconds(3)' in cs
assert '"127.0.0.1"' in stat
assert 'private CancellationTokenSource? _trafficCancellation;' in cs
assert '"direct" or "block" or ApiTag' in stat
assert '"-json"' in stat and '"-reset"' not in stat
for locale in ('zh-CN','en-US'):
    data = json.loads((root / f'src/VelaRay.Maui/Resources/I18n/{locale}.json').read_text(encoding='utf-8'))
    for key in ('TrafficSessionTotals','TrafficUnavailable','TrafficCollecting'):
        assert key in data
print('PASS: 78dp cards, original actions, read-only traffic UX and local stats guardrails')
