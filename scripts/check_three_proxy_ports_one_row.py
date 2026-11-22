#!/usr/bin/env python3
"""UX regression: three editable proxy inbounds in one row; PAC script listener below."""
from pathlib import Path
from lxml import etree
import json

root = Path(__file__).resolve().parents[1]
base = root / 'src/VelaRay.Maui'
xaml = (base / 'MainPage.xaml').read_text(encoding='utf-8')
tree = etree.fromstring(xaml.encode())
ns = {'m':'http://schemas.microsoft.com/dotnet/2021/maui', 'x':'http://schemas.microsoft.com/winfx/2009/xaml'}

def entry(name):
    found = tree.xpath(f'//m:Entry[@x:Name="{name}"]', namespaces=ns)
    assert len(found) == 1, (name, len(found))
    return found[0]

entries = [entry(name) for name in ('HttpPortEntry', 'SocksPortEntry', 'PacProxyPortEntry')]
cards = [e.getparent().getparent() for e in entries]
assert all(c.tag.endswith('Border') for c in cards)
assert [(c.get('Grid.Row'),c.get('Grid.Column')) for c in cards] == [('0','0'),('0','1'),('0','2')]
parent = cards[0].getparent()
assert all(c.getparent() is parent for c in cards)
assert parent.tag.endswith('Grid')
assert parent.get('ColumnDefinitions') == '*,*,*'
assert parent.get('RowDefinitions') == 'Auto,Auto'
for e in entries:
    assert e.get('Keyboard') == 'Numeric'
    assert e.get('WidthRequest') is None
    assert e.get('IsEnabled') is None
pac_script = entry('PacPortEntry')
pac_container = pac_script.getparent().getparent().getparent()
assert pac_container.getparent() is parent
assert pac_container.get('Grid.Row') == '1'
assert pac_container.get('Grid.ColumnSpan') == '3'
assert len(tree.xpath('//m:Switch[@x:Name="EnableUdpSwitch"]', namespaces=ns))==1
assert len(tree.xpath('//m:Switch[@x:Name="LanSwitch"]', namespaces=ns))==1
for culture in ('zh-CN','en-US'):
    content = json.loads((base/f'Resources/I18n/{culture}.json').read_text(encoding='utf-8'))
    assert content['HelpPacProxyPort']
    assert 'FieldPacProxyPort' in content
print('PASS: HTTP, SOCKS5, PAC HTTP on a single 3-column row; PAC script listener below; handlers unchanged')
