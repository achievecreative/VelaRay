#!/usr/bin/env python3
"""Source contract: routing editor never selects/promotes the core routing policy.
These source checks are not a Roslyn or running-core test.
"""
from pathlib import Path
import json
from lxml import etree
root = Path(__file__).resolve().parents[1]
read = lambda path: (root/path).read_text(encoding='utf-8')
code = read('src/VelaRay.Maui/MainPage.xaml.cs')
ui = read('src/VelaRay.Maui/MainPage.xaml')
core = read('src/VelaRay.Core/Configuration/CustomRoutingDeployment.cs')
store = read('src/VelaRay.Core/Runtime/ClientSettingsStore.cs')
tree = etree.fromstring(ui.encode('utf-8'))
ns={'x':'http://schemas.microsoft.com/winfx/2009/xaml'}
assert len(tree.xpath('//*[@x:Name="RoutingModePicker"]', namespaces=ns)) == 1
assert len(tree.xpath('//*[@x:Name="SmartFallbackPicker"]', namespaces=ns)) == 1
assert len(tree.xpath('//*[@x:Name="ActiveRoutingRulePicker"]', namespaces=ns)) == 1
assert len(tree.xpath('//*[@x:Name="RouteModeReadOnlyLabel"]', namespaces=ns)) == 1
assert len(tree.xpath('//*[@x:Name="SmartFallbackReadOnlyLabel"]', namespaces=ns)) == 1
assert 'RouteModeInRoutingPicker' not in ui and 'SmartFallbackRoutingPicker' not in ui
assert 'RouteModeInRoutingPicker' not in code and 'SmartFallbackRoutingPicker' not in code
assert 'CustomRouterPanel.IsVisible = true;' in code
assert 'ActiveRoutingRuleSettingsPanel.IsVisible = CurrentRouteMode == "Custom"' in code
assert 'private async void ApplyActiveRoutingRuleClicked' in code
assert 'CustomRoutingDeployment.Apply(_settings, selected.Id);' in code
assert 'private void ActivateCustomRouting()' not in code
assert 'CustomRoutingDeployment.MigrateLegacy(settings);' in store
for path in ('V4ConfigBuilder','V5ConfigBuilder','ConfigFlavor'):
    assert 'CustomRoutingDeployment.ResolveJson' in read('src/VelaRay.Core/Configuration/'+path+'.cs')
assert 'public static void Apply(ClientSettings settings, string ruleSetId)' in core
assert 'settings.AppliedRoutingRuleJson = rule.Json;' in core
assert 'settings.SelectedRoutingRuleId = rule.Id;' in core
for start,end in [('private async void RouteStrategyChanged','private void RouteRawModeToggled'),
                  ('private async Task ApplyVisualEditAsync','private void AddVisualRuleClicked'),
                  ('private async void RulePickerChanged','private async void AddRuleClicked'),
                  ('private async void AddRuleClicked','private async void SaveRuleClicked'),
                  ('private async void SaveRuleClicked','private async void DeleteRuleClicked'),
                  ('private async void DeleteRuleClicked','private string SelectedPacPath')]:
    segment=code[code.index(start):code.index(end)]
    assert '_settings.RoutingMode =' not in segment, start
    assert 'SelectedRoutingRuleId =' not in segment, start
    assert 'CustomRoutingDeployment.Apply' not in segment, start
    assert 'ToggleConnectionAsync' not in segment, start
assert 'private async void ApplyActiveRoutingRuleClicked' in code
for locale in ('zh-CN','en-US'):
    strings=json.loads(read(f'src/VelaRay.Maui/Resources/I18n/{locale}.json'))
    assert all(strings.get(k) for k in ('FieldAppliedRoutingRuleSet','ActionApplyRoutingRuleSet',
                  'HelpApplyRoutingRuleSet','HelpRoutingEditorNotApplied','RoutingRuleSnapshotSaved'))
smoke=read('tests/VelaRay.SmokeTests/Program.cs')
assert 'editing a selected rule does not change V4/V5 core JSON even after ordinary reconnect' in smoke
assert 'explicit promotion flags a running core for manual reconnect' in smoke
print('PASS: read-only route policy on rule editor page; explicit snapshot application only from Proxy settings')
print('PASS: V4/V5 immutable applied route snapshot; backward-compatible legacy migration; bilingual labels')
