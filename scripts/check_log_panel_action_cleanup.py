#!/usr/bin/env python3
"""VelaRay 2.7: sidebar config validation, log-only bottom area, no duplicate actions."""
from pathlib import Path
from lxml import etree
root = Path(__file__).resolve().parents[1]
ui = etree.parse(str(root / 'src/VelaRay.Maui/MainPage.xaml'))
code = (root / 'src/VelaRay.Maui/MainPage.xaml.cs').read_text(encoding='utf-8')
ns = {'x':'http://schemas.microsoft.com/winfx/2009/xaml'}

def named(name):
    found = ui.xpath(f'//*[@x:Name="{name}"]', namespaces=ns)
    assert len(found) == 1, (name, len(found))
    return found[0]

header = named('HeaderConnectionButton')
assert header.get('Clicked') == 'ConnectionActionClicked'
assert named('LogScrollView').get('Grid.Row') == '0'
assert named('ConfiguredPortsFooterLabel').get('Grid.Column') == '0'
assert ui.xpath('//*[@AutomationId="check-config-server-action"]')[0].get('Clicked') == 'TestClicked'
assert not ui.xpath('//*[@x:Name="StatusLabel"]', namespaces=ns)
assert not ui.xpath('//*[@x:Name="ConnectionActionButton"]', namespaces=ns)
assert not ui.xpath('//*[@AutomationId="connection-action-bottom"]')
assert not ui.xpath('//*[@Clicked="ShareClicked"]')
server_action = ui.xpath('//*[@AutomationId="check-config-server-action"]')[0]
assert server_action.getparent().get('ColumnDefinitions') == '*,1.6*,*', 'Make room for the longer English validation label'
assert server_action.get('ImageSource') == 'action_check_circle_neutral.png' 
assert 'ShareClicked' not in code and not __import__('re').search(r'(?<![\w])StatusLabel\.Text', code)
assert not __import__('re').search(r'\bConnectionActionButton\b', code), 'No code-behind references to deleted bottom action'
assert 'new[] { HeaderConnectionButton }' in code, 'Dynamic actions must update the surviving top button'
assert 'await DisplayAlert(UiStrings.Instance["ActionValidateConfig"], confirmation, L("确定"));' in code
log = named('LogScrollView')
parent = log.getparent()
assert parent.get('RowDefinitions') == '*,Auto', parent.get('RowDefinitions')
assert not parent.xpath('.//Button'), 'The bottom log and traffic region should not contain action buttons'
assert named('LogTailAnchor') is not None
assert named('TrafficTotalsLabel') is not None
assert named('RunningModeFooterLabel') is not None
print('PASS: removed log toolbar/status, sidebar checks config, existing header actions and traffic/log remain')
