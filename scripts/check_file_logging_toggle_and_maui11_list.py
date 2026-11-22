#!/usr/bin/env python3
"""Static contract for logging preference and MAUI 11 Windows CollectionView2."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET
R=Path(__file__).resolve().parents[1]
core=(R/'src/VelaRay.Core/Runtime/RuntimeLogStore.cs').read_text()
model=(R/'src/VelaRay.Core/Models/ServerProfile.cs').read_text()
cs=(R/'src/VelaRay.Maui/MainPage.xaml.cs').read_text()
root=ET.parse(R/'src/VelaRay.Maui/MainPage.xaml').getroot()
n={'m':'http://schemas.microsoft.com/dotnet/2021/maui','x':'http://schemas.microsoft.com/winfx/2009/xaml'}
def find_named(name):
    nodes=[v for v in root.iter() if v.get('{'+n['x']+'}Name')==name]
    assert len(nodes)==1,(name,len(nodes))
    return nodes[0]
switch=find_named('FileLoggingSwitch')
assert switch.get('Toggled')=='FileLoggingToggled'
parent_map = {c: p for p in root.iter() for c in p}
grid = parent_map[switch]
assert grid.tag.endswith('Grid') and parent_map[grid].tag.endswith('FlexLayout')
assert len(list(grid)) == 2, 'log toggle matches its peer toggle/label layout'
assert 'HelpEnableFileLogging' not in (R/'src/VelaRay.Maui/MainPage.xaml').read_text()
assert find_named('ExportLogButton').get('Clicked')=='ExportLogClicked'
assert 'public bool EnableFileLogging { get; set; } = true;' in model
assert 'Directory.CreateDirectory(LogsDirectory);' not in (R/'src/VelaRay.Core/Runtime/ClientSettingsStore.cs').read_text()
assert 'new RuntimeLogStore(_store.LogFilePath, enableFileLogging: false)' in cs
assert '_runtimeLogs.SetFileLoggingEnabled(_settings.EnableFileLogging);' in cs
assert '_runtimeLogs.SetFileLoggingEnabled(e.Value);' in cs
assert 'ExportLogButton.IsEnabled = e.Value;' in cs
assert '_settings.EnableFileLogging = FileLoggingSwitch.IsToggled;' in cs
assert 'public bool FileLoggingEnabled' in core
assert 'public void SetFileLoggingEnabled(bool enabled)' in core
assert '_pending.Clear();' in core and 'if (!_fileLoggingEnabled || _fileLoggingFailed) return;' in core
assert 'if (!_fileLoggingEnabled || _fileLoggingFailed || _pending.Count == 0 ||' in core
assert 'if (!_fileLoggingEnabled)' in core and 'logs cannot be exported to disk' in core
assert 'public const int MaxVisibleLines = 1_000;' in core
for language in ('zh-CN','en-US'):
    data=json.loads((R/f'src/VelaRay.Maui/Resources/I18n/{language}.json').read_text())
    assert 'ToggleEnableFileLogging' in data and 'HelpEnableFileLogging' not in data
maui=ET.parse(R/'src/VelaRay.Maui/VelaRay.Maui.csproj')
assert [v.text for v in maui.iter('UseWindowsCollectionView2Handler')]==['true']
card=find_named('ProfileCardBorder')
assert card.find('m:Border.Triggers/m:DataTrigger',n).get('Binding')=='{Binding IsSelected}'
assert find_named('ProfilesList').get('ItemSizingStrategy')=='MeasureAllItems'
assert 'profile.IsSelected = ReferenceEquals(profile, selected)' in cs
print('PASS: persisted default-on log-file toggle, bounded memory-only mode, safe disabled export, MAUI 11 CollectionView2 with model-driven selected border')
