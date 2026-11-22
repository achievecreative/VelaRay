#!/usr/bin/env python3
"""Source-level checks for safe auto-apply of PAC/Global/Manual after Save.
Run the C# smoke tests and WinUI builds separately on Windows.
"""
from pathlib import Path
root = Path(__file__).resolve().parents[1]
main = (root/'src/VelaRay.Maui/MainPage.xaml.cs').read_text(encoding='utf-8')
detector = (root/'src/VelaRay.Core/Runtime/RunningConfigChangeDetector.cs').read_text(encoding='utf-8')
smoke = (root/'tests/VelaRay.SmokeTests/Program.cs').read_text(encoding='utf-8')
save = main.split('private async void SaveClicked(', 1)[1].split('private async Task<bool> ApplySavedProxyModeWithoutCoreRestartAsync(', 1)[0]
connect = main.split('private async Task ConnectAsync(',1)[1].split('private async Task WaitForLocalHttpAsync(',1)[0]
for marker in ('await SaveAllAsync()', 'await ApplySavedProxyModeWithoutCoreRestartAsync()',
               'RunningConfigChangeDetector.IsModeOnlyChange(',
               'await ConnectAsync(launchedId, modeOnlyRestart: true,',
               '_runningConfigProfileId', '_core.RunningExecutablePath', '_connectionActionBusy = true'):
    assert marker in save, marker
for marker in ('ClientSettings runningSettings', 'SameModeIndependentConfiguration(current, runningSettings)',
               'previous.ProxyMode = runningProxyMode', 'a.SmartUnmatchedRoute == b.SmartUnmatchedRoute',
               'a.AppliedRoutingRuleJson == b.AppliedRoutingRuleJson',
               'a.PacDirectWebsites == b.PacDirectWebsites'):
    assert marker in detector, marker
for marker in ('modeOnlyRestart', 'launchingProfile', 'launchedProfileId', 'launchingFormat',
               'await _core.TestConfigAsync(executable, stagedConfigPath, launchingFormat)',
               'await StopCoreAndProxyAsync()', '_runningConfigProfileId = launchingProfile?.Id',
               '_runningSettingsAtLaunch = JsonSerializer.Deserialize<ClientSettings>'):
    assert marker in connect, marker
assert connect.index('await _core.TestConfigAsync(executable, stagedConfigPath, launchingFormat)') < connect.index('await StopCoreAndProxyAsync()')
assert 'if (!modeOnlyRestart || oldCoreWasStopped)' in connect
for phrase in ('pending Smart rule edits prevent automatic mode switch restart',
               'Global routing must not hide staged PAC domain edits during auto restart',
               'external JSON can never be auto-restarted'):
    assert phrase in smoke, phrase
assert '_selected = launchingProfile' not in main
print('PASS: save applies Windows-only mode changes live and auto-restarts safe core-only mode changes')
print('PASS: running node is preserved, unrelated routing drafts are protected, prevalidation runs before stop')
