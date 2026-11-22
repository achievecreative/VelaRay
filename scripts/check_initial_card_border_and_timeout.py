#!/usr/bin/env python3
"""Regression contract for first-render card borders and concise localized timeout.

This source check complements (but cannot replace) a Windows MAUI 11 render test.
"""
from pathlib import Path
import json
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
app = root / 'src/VelaRay.Maui'
model = (root / 'src/VelaRay.Core/Models/ServerProfile.cs').read_text(encoding='utf-8')
code = (app / 'MainPage.xaml.cs').read_text(encoding='utf-8')
strings = (app / 'Localization/UiStrings.cs').read_text(encoding='utf-8')
page = ET.parse(app / 'MainPage.xaml').getroot()
maui = '{http://schemas.microsoft.com/dotnet/2021/maui}'
x = '{http://schemas.microsoft.com/winfx/2009/xaml}'

cards = [v for v in page.iter() if v.tag == maui+'Border' and v.get(x+'Name') == 'ProfileCardBorder']
assert len(cards) == 1
card = cards[0]
assert card.get('StrokeThickness') == '1.5'
assert '#C6D1E2' in card.get('Stroke',''), 'Normal card must have a visible stroke on first render'
assert '#FFFFFF' in card.get('BackgroundColor','')
triggers = [v for v in card.find(maui+'Border.Triggers') if v.tag == maui+'DataTrigger']
assert len(triggers) == 1 and triggers[0].get('Binding') == '{Binding IsSelected}' and triggers[0].get('Value') == 'True'
setters = {v.get('Property'): v.get('Value','') for v in triggers[0] if v.tag == maui+'Setter'}
assert 'Stroke' in setters and '#4E68FF' in setters['Stroke']
assert 'BackgroundColor' in setters and '#EDF3FF' in setters['BackgroundColor']
assert 'IsConnected' in ET.tostring(card,encoding='unicode'), 'Running marker stays independent'
assert 'public bool IsSelected' in model and 'nameof(IsSelected)' in model
assert 'private void UpdateSelectedProfileVisuals(ServerProfile? selected)' in code
assert 'profile.IsSelected = ReferenceEquals(profile, selected);' in code
assert 'UpdateSelectedProfileVisuals(selected);' in code and 'UpdateSelectedProfileVisuals(p);' in code
assert code.index('UpdateSelectedProfileVisuals(selected);',code.index('private void RefreshProfiles')) < code.index('ProfilesList.SelectedItem = selected;',code.index('private void RefreshProfiles'))
# Stale XAML native selection-state transition must not override data-bound model state.
assert not any(v.tag == maui+'VisualState' for v in page.iter() if v.get('Name') in {'Normal','Selected'} and v in list(page.find('.//'+maui+'CollectionView'))), 'unexpected root VSM on server collection'

assert 'profile.ProbeStatusText = succeeded ? "" : UiStrings.Instance["节点测速超时"];' in code
assert 'profile.ProbeStatusText = UiStrings.Instance["节点测速超时"];' in code
assert 'profile.IsProbeFailure = !succeeded;' in code
assert 'Binding IsProbeFailure' in ET.tostring(card,encoding='unicode')
assert '#D92D20' in ET.tostring(card,encoding='unicode')
assert 'public string this[string key]' in strings
assert 'if (_language == "en-US") return Translate(message);' in strings  # Preserve separate diagnosis logic
for language, expected in [('zh-CN','超时'),('en-US','Timeout')]:
    data=json.loads((app/f'Resources/I18n/{language}.json').read_text(encoding='utf-8'))
    assert data['节点测速超时']==expected
assert 'L("节点测速超时")' not in code, 'L() returns original Chinese source in Chinese mode'
print('PASS: visible default server card border and initial selected-state binding; timeout/failed probes map to red 超时/Timeout')
