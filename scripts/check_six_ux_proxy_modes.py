#!/usr/bin/env python3
"""Regression contract for six UX improvements; no runtime behavior is modified."""
from pathlib import Path
from lxml import etree
import json

root=Path(__file__).resolve().parents[1]
base=root/'src/VelaRay.Maui'
xaml=(base/'MainPage.xaml').read_text()
code=(base/'MainPage.xaml.cs').read_text()
styles=(base/'Resources/Styles/Theme.xaml').read_text()
tree=etree.fromstring(xaml.encode())
ns={'m':'http://schemas.microsoft.com/dotnet/2021/maui','x':'http://schemas.microsoft.com/winfx/2009/xaml'}

def named(n):
    matches=tree.xpath(f'//*[@x:Name="{n}"]',namespaces=ns)
    assert len(matches)==1,(n,len(matches))
    return matches[0]

# 1. Global settings separated into cards rather than replacing the underlying fields.
for n in ('SettingsSection','SocksPortEntry','HttpPortEntry','PacPortEntry','SmartFallbackPicker',
          'RoutingModePicker','LogLevelPicker','DnsEntry','CorePathEntry','LanSwitch','EnableUdpSwitch',
          'SniffSwitch','StartupSwitch','AutoConnectSwitch','TraySwitch','BalanceSwitch'):
    named(n)
assert xaml.count('Style="{StaticResource SurfaceCardStyle}"')>=4
# 2. One connected-status pill, sourcing actual running state.
assert named('TopConnectionDot').tag.endswith('Border')
assert 'TopConnectionDot.BackgroundColor = Color.FromArgb(connected ?' in code
assert 'ActiveNodeLabel.Text = connected ? RunningServerLabel : "—";' in code
assert 'ConnectionPillStyle' in styles
# 3. Connected marker separate from selected visual state.
assert 'VisualState Name="Selected"' in xaml
assert 'IsVisible="{Binding IsConnected}"' in xaml
assert 'ServerRunningIndicatorShort' in xaml
sel=code.split('private void ProfilesSelectionChanged',1)[1].split('private void RefreshProfiles',1)[0]
assert all(op not in sel for op in ('ConnectAsync(', 'ToggleConnectionAsync(', 'DisconnectAsync('))
# 4. Three large choices map to the existing picker and exact persisted values.
mode_picker=named('ProxyModePicker')
assert mode_picker.get('IsVisible')=='False'
assert mode_picker.get('SelectedIndexChanged')=='ProxyModeChanged'
for n,mode in [('PacProxyModeRadio','PAC'),('GlobalProxyModeRadio','Global'),('ManualProxyModeRadio','Manual')]:
    assert named(n).get('Value')==mode
    assert named(n).get('CheckedChanged')=='ProxyModeCardCheckedChanged'
    assert named(n).get('GroupName')=='SystemProxyMode'
for n in ('PacProxyModeCard','GlobalProxyModeCard','ManualProxyModeCard'):
    named(n)
codeblock=code.split('private void ProxyModeCardCheckedChanged',1)[1].split('private void CustomConfigToggled',1)[0]
assert 'ProxyModePicker.SelectedItem = selection;' in codeblock
assert all(op not in codeblock for op in ('_proxy.EnableGlobal(', '_proxy.EnablePac(', 'ToggleConnectionAsync(', '_core.Start(', 'SaveAllAsync('))
# 5. Existing HTTP/SOCKS input and toggles retained.
for n in ('HttpPortEntry','SocksPortEntry','EnableUdpSwitch','LanSwitch'):
    named(n)
assert 'ProxyInboundHttpTitle' in xaml and 'ProxyInboundSocksTitle' in xaml
# 6. Footer differentiates configured settings and actual running mode.
for n in ('ConfiguredPortsFooterLabel','RunningModeFooterLabel','LogScrollView','LogTailAnchor'):
    named(n)
assert '_runningProxyMode' in code.split('private void UpdateFooterStatus()',1)[1].split('private void CustomConfigToggled',1)[0]
assert 'UpdateFooterStatus();' in code
assert 'VerticalScrollBarVisibility="Always"' in xaml
# Existing essential actions and pending-PAC flow intact.
for n in ('HeaderConnectionButton','ConnectionActionButton','PendingReconnectButton',
          'CheckCoreUpdateButton','InstallCoreUpdateButton','ChineseLanguageButton','EnglishLanguageButton'):
    named(n)
for k in ('HelpProxyModeCards','ProxyModePacTitle','ProxyModeGlobalTitle','ProxyModeManualTitle',
          'FooterConfiguredPorts','FooterRuntimeMode','FooterDisconnected'):
    for lang in ('zh-CN','en-US'):
        data=json.loads((base/f'Resources/I18n/{lang}.json').read_text())
        assert data.get(k),(k,lang)
print('PASS: 6 UX improvements, 3 original PAC/Global/Manual values, no automatic connect/save from mode cards')
print('PASS: existing connection actions, log panel, staged PAC and update controls preserved')
