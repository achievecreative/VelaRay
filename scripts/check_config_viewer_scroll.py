#!/usr/bin/env python3
"""Static regression checks for VelaRay read-only configuration viewer.

Note: these checks do not run the MAUI/WinUI rendering engine.
"""
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent.parent
viewer_file = root / 'src/VelaRay.Maui/ConfigViewerPage.xaml'
code_file = root / 'src/VelaRay.Maui/ConfigViewerPage.xaml.cs'
ui = ET.parse(viewer_file).getroot()
ns = {'maui': 'http://schemas.microsoft.com/dotnet/2021/maui',
      'x': 'http://schemas.microsoft.com/winfx/2009/xaml'}
main_grid = ui.find('maui:Grid', ns)
assert main_grid is not None and '*' in main_grid.attrib['RowDefinitions']
container = main_grid.find("maui:Border[@Grid.Row='3']", ns)
# ElementTree needs the MAUI namespace for attached properties.
if container is None:
    container = next((x for x in main_grid if x.tag.endswith('Border') and x.attrib.get('Grid.Row') == '3'), None)
assert container is not None, 'Config body must be in the star-sized row'
scroll = container.find('maui:ScrollView', ns)
assert scroll is not None, 'Missing bounded ScrollView'
assert scroll.attrib.get('Orientation') == 'Vertical'
assert scroll.attrib.get('VerticalScrollBarVisibility') == 'Always'
content = scroll.find('maui:Label', ns)
assert content is not None
assert content.attrib.get('LineBreakMode') == 'CharacterWrap', 'Do not use NoWrap: it truncates multiline JSON'
assert content.attrib.get('MaxLines', '-1') == '-1'
assert content.attrib.get('{http://schemas.microsoft.com/winfx/2009/xaml}Name') == 'ConfigTextLabel'
code = code_file.read_text(encoding='utf-8')
assert code.count('ConfigTextLabel.Text') == 3, 'Both raw and formatted JSON must update the label'
assert 'Clipboard.Default.SetTextAsync(_originalJson)' in code, 'Copy should retain exact original JSON'
assert 'new JsonSerializerOptions { WriteIndented = true }' in code, 'Formatted JSON toggle must still work'
assert 'ConfigEditor' not in code
print('PASS: scrollable configuration viewer, full multiline content, raw/pretty toggle, and copy')
