#!/usr/bin/env python3
"""Static contract only: use verify-windows.ps1 for real build and UI/runtime testing."""
from pathlib import Path
import json,re
from lxml import etree
root=Path(__file__).resolve().parent.parent
xaml=(root/'src/VelaRay.Maui/MainPage.xaml').read_text()
code=(root/'src/VelaRay.Maui/MainPage.xaml.cs').read_text()
v5=(root/'src/VelaRay.Core/Configuration/V5ConfigBuilder.cs').read_text()
tree=etree.fromstring(xaml.encode())
ns={'m':'http://schemas.microsoft.com/dotnet/2021/maui','x':'http://schemas.microsoft.com/winfx/2009/xaml'}
for name in ['NodeEditFields','RunningNodeLockNotice','SaveProfileButton','DeleteProfileButton','ActiveNodeLabel']:
 assert tree.xpath(f"//*[@x:Name='{name}']",namespaces=ns),f'Missing {name}'
selection=re.search(r'private void ProfilesSelectionChanged\(.*?\n    }',code,re.S)
assert selection
for call in ['ConnectAsync(', 'ToggleConnectionAsync(', 'StopCoreAndProxyAsync(', 'DisconnectAsync(']:
 assert call not in selection.group(),f'Selection unexpectedly starts or switches connection: {call}'
assert 'IsActiveProfile(_selected)' in code
assert 'NodeEditFields.IsEnabled = !locked' in code
assert 'ActiveNodeLabel.Text = connected ? RunningServerLabel' in code
assert 'if (IsActiveProfile(p)) return;' in code
assert 'ReferenceEquals(sender, SaveProfileButton)' in code
assert '_runningProxyMode = _settings.ProxyMode;' in code
assert 'SetRunningConnection(true, launchedProfiles, launchedExternal);' in code
assert 'security[transport == "hysteria2" ? "server_name" : "serverName"]' in v5
assert 'if (transport != "hysteria2")' in v5
for bad in ['SubscriptionUrlsEditor','DownloadCoreClicked','SubscriptionService','CoreInstaller','CoreLocationLabel']:
 for text in [xaml,code]: assert bad not in text,f'Legacy subscription/maintenance reference: {bad}'
zh=json.loads((root/'src/VelaRay.Maui/Resources/I18n/zh-CN.json').read_text())
en=json.loads((root/'src/VelaRay.Maui/Resources/I18n/en-US.json').read_text())
for key in ['RunningNodeEditLocked','ActionEditNotAllowed','DisconnectBeforeEdit','HeaderCurrentServer']:
 assert zh[key] and en[key]
assert code.count('ConnectionActionResolver.Resolve(') == 1
assert 'private async void ConnectionActionClicked' in code
assert 'private async void DisconnectClicked' not in code
assert 'private async void ConnectClicked' not in code
assert 'CoreReleaseUpdater' in code and 'CoreReleaseUpdater' in (root/'src/VelaRay.Maui/MauiProgram.cs').read_text()
print('PASS: selection only edits; running nodes locked; unified connection action; core updater restored; subscriptions removed')
print('NOTE: static source contract, not C# compilation or GUI behavior testing')
