#!/usr/bin/env python3
"""Source-level regression checks; use dotnet smoke tests and Windows build for executable validation."""
from pathlib import Path
import json
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
app=root/'src/VelaRay.Maui'
core=root/'src/VelaRay.Core'
ui=ET.parse(app/'MainPage.xaml').getroot()
code=(app/'MainPage.xaml.cs').read_text()
updater=(core/'Runtime/CoreReleaseUpdater.cs').read_text()
resolver=(core/'Runtime/ConnectionActionResolver.cs').read_text()
ns={'x':'http://schemas.microsoft.com/winfx/2009/xaml'}

def named(name):
    values=[e for e in ui.iter() if e.get('{'+ns['x']+'}Name')==name]
    assert len(values)==1, f'expected one {name}'
    return values[0]
for name in ('HeaderConnectionButton',):
    assert named(name).get('Clicked')=='ConnectionActionClicked'
assert not any(e.get('Clicked')=='DisconnectClicked' for e in ui.iter())
assert not any(e.get('AutomationId')=='connection-action-bottom' for e in ui.iter())
assert 'ConnectionActionResolver.Resolve(' in code
assert all(action in resolver for action in ('Connect, Switch, Disconnect','runningProfileIds.Contains(selectedProfileId)'))
assert 'selected node' in resolver.lower() or 'editor selection' in resolver.lower()
for name in ('IncludePrereleaseCoreSwitch','CurrentCoreVersionLabel','LatestCoreVersionLabel',
             'CoreUpdateStatusLabel','CheckCoreUpdateButton','InstallCoreUpdateButton'):
    named(name)
for safety in ('SHA256.HashDataAsync','MaxArchiveBytes','MaxExecutableBytes','ZipFile.OpenRead',
               'OfficialAssetUrl','GetInstalledVersionAsync(candidatePath','File.Move(exe, backup)',
               'File.Move(backup, exe)','!OperatingSystem.IsWindows()'):
    assert safety in updater, f'missing protection: {safety}'
for safety in ('_connectionGate.WaitAsync()', 'if (_core.IsRunning || !IsManagedCorePath)',
               '_installingCoreUpdate','CoreDisconnectToInstall'):
    assert safety in code, f'missing UI guard: {safety}'
zh=json.loads((app/'Resources/I18n/zh-CN.json').read_text())
en=json.loads((app/'Resources/I18n/en-US.json').read_text())
for key in ('SectionCoreUpdates','ActionCheckCoreUpdates','ActionInstallCoreUpdate','CoreDisconnectToInstall',
            'CoreInstallConfirmation','CoreInstallSuccess','ToggleCorePrerelease'):
    assert zh.get(key) and en.get(key)
print('PASS: one action per toolbar; explicit node-state semantics; official V2Fly update controls, rollback and verification source checks')
print('NOTE: static checks cannot verify Windows GUI, HTTP downloads or C# compilation')
