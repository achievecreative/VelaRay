#!/usr/bin/env python3
"""Guard fixed action widths, new language controls and log low-I/O semantics."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
core = (root/'src/VelaRay.Core/Runtime/RuntimeLogStore.cs').read_text()
page = (root/'src/VelaRay.Maui/MainPage.xaml.cs').read_text()
xml = ET.parse(root/'src/VelaRay.Maui/MainPage.xaml').getroot()
x = '{http://schemas.microsoft.com/winfx/2009/xaml}'
name = lambda n: n.get(x+'Name')
named = {name(n): n for n in xml.iter() if name(n)}
assert named['HeaderConnectionButton'].get('WidthRequest') == '156'
assert 'ConnectionActionButton' not in named, 'Bottom connection button has been removed by design'
assert named['LogScrollView'].get('VerticalScrollBarVisibility') == 'Always'
assert named['LogScrollView'].get('Orientation') == 'Vertical'
assert named['ChineseLanguageButton'].get('Clicked') == 'ChineseLanguageClicked'
assert named['EnglishLanguageButton'].get('Clicked') == 'EnglishLanguageClicked'
assert all(s in page for s in ('UpdateLanguageButtons()', 'UpdateLanguageButtons();', 'ScrollLogToBottom', 'ScrollToAsync(LogTailAnchor, ScrollToPosition.End, false)', 'SnapshotVisible()', 'ChangeLanguageAsync'))
assert 'LogTextLabel.Text = string.Join(Environment.NewLine, _runtimeLogs.SnapshotVisible());' in page
assert 'new RuntimeLogStore(_store.LogFilePath)' in page
assert '_runtimeLogs.FlushNow(' not in page, 'Disconnected state must not call removed FlushNow API'
assert 'private async Task StopCoreAndProxyAsync()' in page
assert 'public const int MaxVisibleLines = 1_000;' in core
assert 'public const int DefaultMinimumWriteBatchLines = 1_000;' in core
assert 'public const int DefaultMaxPendingLines = 2_048;' in core
assert 'TimeSpan.FromMinutes(1)' in core
assert '(!includePartialBatch && _pending.Count < _minimumWriteBatchLines)' in core
assert 'FlushPendingLocked(includePartialBatch: false)' in core
assert 'FlushPendingLocked(includePartialBatch: true)' in core
assert 'while (await timer.WaitForNextTickAsync' in core
append = core.split('public void Append(string line)',1)[1].split('public string[] SnapshotVisible()',1)[0]
assert not any(io in append for io in ('WriteLine(', 'FileStream(', 'File.Write', 'FlushPendingLocked(', 'File.AppendAll'))
assert 'if (_pending.Count >= _maxPendingLines)' in core
assert 'if (_visible.Count == MaxVisibleLines) _visible.Dequeue();' in core
assert 'DefaultMaxFileBytes = 10L * 1024 * 1024;' in core
assert 'DefaultArchiveFiles = 5;' in core
buttons=[n for n in xml.iter() if n.get('AutomationId')=='view-config']
assert len(buttons)==1 and buttons[0].get('WidthRequest')=='140'
assert named['LogTextLabel'].get('LineBreakMode')=='CharacterWrap'
assert 'private void StartLogRefreshTimer()' in page
assert 'TimeSpan.FromMilliseconds(500)' in page
assert 'DisposeLogging' in (root/'src/VelaRay.Maui/App.xaml.cs').read_text()
assert 'RuntimeLogStore.DefaultMinimumWriteBatchLines' in (root/'tests/VelaRay.SmokeTests/Program.cs').read_text()
print('PASS: fixed widths, language choices, log scrollbar/auto-tail, 1k UI and pending threshold, 60s disk batching, bounded queue, export override, shutdown policy')
print('NOTE: This is a source-level check; run C# smoke tests and MAUI Windows build for runtime validation.')
