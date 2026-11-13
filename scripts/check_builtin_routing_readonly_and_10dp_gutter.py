#!/usr/bin/env python3
"""Static regression check for built-in routing read-only gallery and 10dp server card rail."""
from pathlib import Path
from lxml import etree
import json

root = Path(__file__).resolve().parents[1]
read = lambda rel: (root/rel).read_text(encoding="utf-8")
ui = read("src/VelaRay.Maui/MainPage.xaml")
code = read("src/VelaRay.Maui/MainPage.xaml.cs")
xml = etree.fromstring(ui.encode())
ns={"m":"http://schemas.microsoft.com/dotnet/2021/maui", "x":"http://schemas.microsoft.com/winfx/2009/xaml"}
def named(name):
    el=xml.xpath(f'//*[@x:Name="{name}"]', namespaces=ns)
    assert len(el)==1, (name,len(el))
    return el[0]

list_el = named('ProfilesList')
root_template = list_el.xpath('./m:CollectionView.ItemTemplate/m:DataTemplate/m:Grid',namespaces=ns)[0]
assert root_template.get('ColumnDefinitions')=='*,10', 'Must reserve exactly 10dp at right'
assert root_template.find('m:Border',ns).get('Grid.Column') == '0'
assert list_el.xpath('./m:CollectionView.ItemsLayout/m:LinearItemsLayout',namespaces=ns)[0].get('ItemSpacing')=='2'
assert root_template.find('m:Border',ns).get('MinimumHeightRequest')=='78'
assert '18dp empty column' not in ui

for name in ('BuiltInSmartRuleSummary','BuiltInProxyAllRuleSummary','BuiltInDirectRuleSummary','BuiltInBypassCnRuleSummary'):
    el=named(name)
    assert el.tag.endswith('Label') and el.get('Text') is None, name
picker=named('BuiltInRouteDetailPicker')
assert picker.get('SelectedIndexChanged')=='BuiltInRouteDetailChanged'
preview=named('BuiltInRouteJsonPreview')
assert preview.get('IsReadOnly')=='True' and preview.get('AutomationId')=='builtin-route-json-readonly'
assert 'RoutingBuiltInJsonPlaceholder' in preview.get('Placeholder','')
assert named('BuiltInRoutePreview').get('IsVisible')=='True'
assert 'RouteModeInRoutingPicker' not in ui
assert 'SmartFallbackRoutingPicker' not in ui
assert 'private void BuiltInRouteDetailChanged' in code
assert 'RefreshBuiltInRouteJsonPreview();' in code
assert 'CreateBuiltInRoutingPreviewSettings' in code
assert 'private async void ApplyActiveRoutingRuleClicked' in code
assert 'CustomRoutingDeployment.Apply(_settings, selected.Id);' in code
assert 'private void ActivateCustomRouting' not in code
assert 'private async void SaveRuleClicked' in code
for loc,expected in [('zh-CN','保存规则'),('en-US','Save rule')]:
    strings=json.loads(read(f'src/VelaRay.Maui/Resources/I18n/{loc}.json'))
    assert strings['ActionApplyVisualRule']==expected
    for k in ('RoutingBuiltInSmartTitle','RoutingBuiltInProxyAllTitle','RoutingBuiltInDirectTitle',
              'RoutingBuiltInBypassCnTitle','RoutingBuiltInJsonPlaceholder','RoutingCustomDraftsTitle'):
        assert strings.get(k), (loc,k)
print('PASS: 10dp independent server-card right rail, native 2dp card gap, unchanged 78dp minimum height')
print('PASS: all four built-in policies visible + read-only JSON; custom edit/save is not an activation action')
