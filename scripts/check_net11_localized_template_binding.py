#!/usr/bin/env python3
"""Guard against MAUI 11 compiled binding using ServerProfile as a localization indexer key."""
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
page = root / 'src/VelaRay.Maui/MainPage.xaml'
localizer = root / 'src/VelaRay.Maui/Localization/UiStrings.cs'
tree = ET.parse(page)
x = '{http://schemas.microsoft.com/winfx/2009/xaml}'

profiles = [dt for dt in tree.iter() if dt.tag.endswith('DataTemplate') and dt.attrib.get(x + 'DataType') == 'models:ServerProfile']
assert len(profiles) == 1, 'The server profile template must keep its compiled ServerProfile data type'
profile_template = profiles[0]
localized_labels = [el for el in profile_template.iter() if el.tag.endswith('Label')
                    if 'ServerRunningIndicatorShort' in el.attrib.get('Text', '')]
assert len(localized_labels) == 1, 'Expected one localized running indicator in the server card'
label = localized_labels[0]
assert label.attrib.get('BindingContext') == '{x:Static loc:UiStrings.Instance}', 'Localized label must bind to UiStrings rather than ServerProfile'
assert label.attrib.get(x + 'DataType') == 'loc:UiStrings', 'Localized label needs a matching compiled binding type'
assert label.attrib.get('Text') == '{Binding ServerRunningIndicatorShort}', 'Avoid indexer syntax inside typed ServerProfile templates'

for el in profile_template.iter():
    for value in el.attrib.values():
        assert not ('{Binding [' in value and 'UiStrings.Instance' in value), \
            'UiStrings indexer binding inside ServerProfile DataTemplate can trigger MAUI 11 XamlC failure'

profile_bindings = [el.attrib.get('Text', '') for el in profile_template.iter() if el.tag.endswith('Label')]
for prop in ('Name', 'EndpointDisplay', 'ProtocolBadge', 'LatencyDisplay'):
    assert '{Binding ' + prop + '}' in profile_bindings, f'ServerProfile compiled binding for {prop} must remain'

code = localizer.read_text(encoding='utf-8')
assert re.search(r'public\s+string\s+ServerRunningIndicatorShort\s*=>\s*this\["ServerRunningIndicatorShort"\]', code), \
    'UiStrings must expose a typed, localized property'
assert 'new PropertyChangedEventArgs(null)' in code, 'Changing language must notify typed localization binding'
for lang in ('zh-CN', 'en-US'):
    resource = json.loads((root / f'src/VelaRay.Maui/Resources/I18n/{lang}.json').read_text(encoding='utf-8'))
    assert resource.get('ServerRunningIndicatorShort'), f'{lang} missing running indicator translation'

print('PASS: MAUI 11 server-card localization binding has explicit UiStrings type; indexer mismatch removed')
print('NOTE: Static check only. A real MAUI 11 Windows build is still required.')
