#!/usr/bin/env python3
"""Source contract: V2Fly version checking is strictly user-initiated.

This test does not replace a Windows build or network instrumentation.
"""
from pathlib import Path
import json
import re

root = Path(__file__).resolve().parents[1]
app = root / 'src/VelaRay.Maui'
source = (app / 'MainPage.xaml.cs').read_text(encoding='utf-8')

# Only the explicit Check button may invoke the version-check orchestration.
manual_calls = re.findall(r'(?<!\w)CheckCoreUpdatesAsync\s*\(', source)
assert len(manual_calls) == 2, f'Expected exactly one caller and one declaration, found {len(manual_calls)}'
assert re.search(r'CheckCoreUpdatesClicked\([^)]*\)\s*=>\s*await CheckCoreUpdatesAsync\(\)', source)
assert 'private async Task CheckCoreUpdatesAsync()' in source
assert 'CheckCoreUpdatesAsync(silent:' not in source
assert '_ = CheckCoreUpdatesAsync' not in source

startup = source.split('protected override async void OnAppearing()', 1)[1].split('private static string L(', 1)[0]
assert 'CheckCoreUpdatesAsync' not in startup
assert 'GetLatestAsync(' not in startup
assert 'GetInstalledVersionAsync(' not in startup

preference = source.split('private async void CorePrereleaseToggled(', 1)[1].split('private async void CheckCoreUpdatesClicked(', 1)[0]
assert 'CheckCoreUpdatesAsync' not in preference
assert 'GetLatestAsync(' not in preference
assert '_latestCoreRelease = null;' in preference
assert 'CoreManualCheckPrompt' in preference

check_method = source.split('private async Task CheckCoreUpdatesAsync()', 1)[1].split('private async void InstallCoreUpdateClicked(', 1)[0]
assert '_coreUpdater.GetLatestAsync(includePrerelease)' in check_method
assert 'CoreReleaseUpdater.GetInstalledVersionAsync(selectedCorePath)' in check_method
assert 'await AlertAsync(UiStrings.Instance["CoreCheckFailed"], ex);' in check_method
assert len(re.findall(r'\bGetLatestAsync\s*\(', source)) == 1, 'Found unexpected release API calls'

xaml = (app / 'MainPage.xaml').read_text(encoding='utf-8')
assert 'Clicked="CheckCoreUpdatesClicked"' in xaml
assert '当前内核：检查中…' not in xaml
for lang in ('zh-CN', 'en-US'):
    texts = json.loads((app / f'Resources/I18n/{lang}.json').read_text(encoding='utf-8'))
    assert texts['CoreManualCheckPrompt']
    assert texts['CoreUpdateDescription']

print('PASS: V2Fly version checks are manual-only; startup and prerelease-toggle do not check; install remains separate')
print('NOTE: This is a source-level policy check, not a runtime/network test')
