#!/usr/bin/env python3
"""Executable source contracts for VelaRay UX and platform seams (not a MAUI compiler)."""
from pathlib import Path
import json, xml.etree.ElementTree as ET
ROOT = Path(__file__).resolve().parents[1]
APP = ROOT/'src/VelaRay.Maui'
CORE = ROOT/'src/VelaRay.Core'
ns = {'m':'http://schemas.microsoft.com/dotnet/2021/maui', 'x':'http://schemas.microsoft.com/winfx/2009/xaml'}
xaml = ET.parse(APP/'MainPage.xaml').getroot()
code = (APP/'MainPage.xaml.cs').read_text()
program = (APP/'MauiProgram.cs').read_text()
application = (APP/'App.xaml.cs').read_text()
platform = (CORE/'Platform/IPlatformServices.cs').read_text()
checks=[]
def find(name):
    matches=xaml.findall(f".//*[@{{{ns['x']}}}Name='{name}']")
    assert len(matches)==1, f"Missing or duplicate control: {name}"
    return matches[0]
def check(label,condition):
    assert condition,label
    checks.append(label)
# 1: Sidebar semantics + order
zh=json.loads((APP/'Resources/I18n/zh-CN.json').read_text())
en=json.loads((APP/'Resources/I18n/en-US.json').read_text())
check('01 semantic bilingual Server settings label', zh.get('NavServerSettings')=='服务器设置' and en.get('NavServerSettings')=='Server settings')
parents={child:parent for parent in xaml.iter() for child in parent}
global_navigation=parents[find('SettingsNavButton')]
check('02 global navigation remains above the server list',
      parents[find('RoutingNavButton')] is global_navigation and
      global_navigation.get('Grid.Row')=='1' and find('ProfilesList').get('Grid.Row')=='3')
check('03 server settings button stays below server actions', find('NodeNavButton').get('Grid.Row')=='5' and find('DeleteProfileButton').get('Grid.Column')=='2')
# 2: selection state is not connection state
check('04 selection visually distinct from running green badge', xaml.find('.//m:VisualState[@Name="Selected"]',ns) is not None and find('ProfilesList').get('SelectionMode')=='Single' and 'IsConnected' in (APP/'MainPage.xaml').read_text())
sel=code.split('private void ProfilesSelectionChanged',1)[1].split('private void RefreshProfiles',1)[0]
check('05 selecting a node opens editor but never connects', 'ShowOnly(NodeSection)' in sel and all(op not in sel for op in ('ConnectAsync(', 'ToggleConnectionAsync(', 'DisconnectAsync(')))
# 3: responsive action placement and top status
check('06 wrapping action controls', sum(el.tag.endswith('FlexLayout') and el.get('Wrap')=='Wrap' for el in xaml.iter())>=5)
check('07 separate top running connection status', find('TopConnectionStateLabel').get('AutomationId')=='connection-status-top' and 'TopConnectionStateLabel.Text = connected' in code)
# 4: platform neutral boundaries
check('08 UI does not reference native Windows proxy/window implementations', all(v not in code for v in ('is WindowsSystemProxyService','is WindowsWindowShell')) and 'IWindowHostService shell' in application)
check('09 native system proxy recovery and tray behavior abstracted', 'bool RecoverAbandonedBackup();' in platform and 'bool MinimizeToTray { get; set; }' in platform)
check('10 OS-aware core binary locator, Windows native adapters remain conditional', 'PortableCoreLocator' in program and '#if WINDOWS' in program and 'IWindowHostService' in program and (CORE/'Platform/PortableCoreLocator.cs').exists() and
      'DesktopDataDirectories.DefaultForCurrentPlatform' in program and
      (CORE/'Platform/DesktopDataDirectories.cs').exists())
print('\n'.join('PASS '+line for line in checks))
print('10/10 source contracts passed; compilation and rendered UI NOT verified.')
