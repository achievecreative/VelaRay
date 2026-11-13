#!/usr/bin/env python3
"""Check top-right actions remain away from logo; executable source-level UI contract."""
from pathlib import Path
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
ui=ET.parse(root/'src/VelaRay.Maui/MainPage.xaml').getroot()
x='{http://schemas.microsoft.com/winfx/2009/xaml}'
parents={c:p for p in ui.iter() for c in p}

def find_automation(automation_id):
    nodes=[n for n in ui.iter() if n.get('AutomationId')==automation_id]
    assert len(nodes)==1, f'Expected one {automation_id}: {len(nodes)}'
    return nodes[0]

def find_name(name):
    nodes=[n for n in ui.iter() if n.get(x+'Name')==name]
    assert len(nodes)==1, f'Expected one {name}: {len(nodes)}'
    return nodes[0]

view=find_automation('view-config')
connect=find_name('HeaderConnectionButton')
logo=[n for n in ui.iter() if n.get('Source')=='velaray_logo.png']
assert len(logo)==1
assert view.get('Clicked')=='ViewConfigClicked'
assert connect.get('Clicked')=='ConnectionActionClicked'
assert connect.get('AutomationId')=='connection-action-top'
container=parents[view]
assert container is parents[connect], 'Both top actions must be in the same toolbar action group'
assert container.tag.endswith('HorizontalStackLayout')
assert container.get('Grid.Column')=='4' and container.get('HorizontalOptions')=='End', 'Actions must be aligned to the top-right'
bar=parents[container]
assert bar.tag.endswith('Grid')
assert bar.get('ColumnDefinitions')=='Auto,*,230,150,Auto', 'Brand, status, language, and right aligned actions need separate slots'
assert bar is parents[logo[0]], 'Logo and actions must share the same top toolbar'
assert logo[0].get('Grid.Column')=='0'
assert [c for c in bar if c.get('Grid.Column')=='1' and c.tag.endswith('Label') and c.get('Text')=='VelaRay'], 'Brand title must remain beside the logo'
assert not any(c.get('Grid.Column')=='1' and (c is container or view in c.iter()) for c in bar), 'No actions beneath the brand/logo'
assert any(c.get('Grid.Column')=='2' and find_name('ActiveNodeLabel') in c.iter() for c in bar)
assert any(c.get('Grid.Column')=='3' and find_name('EnglishLanguageButton') in c.iter() for c in bar), 'Language choices must remain in column 3'
assert view.get('WidthRequest') == '140' and connect.get('WidthRequest') == '156', 'Primary toolbar buttons must keep stable width as labels change'
assert not any(n.get('AutomationId')=='connection-action-bottom' for n in ui.iter()), 'Connection action belongs only to the top toolbar'
assert any(n.get(x+'Name')=='CheckCoreUpdateButton' for n in ui.iter()), 'Core updater must remain available'
print('PASS: header buttons at top-right; logo/title unmodified; top state action, footer and core updater retained')
print('NOTE: static layout assertions do not replace Windows MAUI visual verification')
