#!/usr/bin/env python3
"""Verify server card gutters and centered live traffic while preserving existing bindings."""
from pathlib import Path
from lxml import etree
root=Path(__file__).resolve().parents[1]
p=root/'src/VelaRay.Maui/MainPage.xaml'
tree=etree.parse(str(p))
ns={'x':'http://schemas.microsoft.com/winfx/2009/xaml', 'm':'http://schemas.microsoft.com/dotnet/2021/maui'}
def one(name):
    nodes=tree.xpath(f'//*[@x:Name="{name}"]', namespaces=ns)
    assert len(nodes)==1,(name,len(nodes))
    return nodes[0]
profile_list=one('ProfilesList')
items_layout=profile_list.find('m:CollectionView.ItemsLayout/m:LinearItemsLayout', namespaces=ns)
assert items_layout is not None and items_layout.get('Orientation')=='Vertical'
assert items_layout.get('ItemSpacing')=='2', 'Use native CollectionView item spacing, not a template margin'
template_root=profile_list.find('m:CollectionView.ItemTemplate/m:DataTemplate/m:Grid',namespaces=ns)
assert template_root is not None and template_root.get('ColumnDefinitions')=='*,10'
assert template_root.get('HorizontalOptions')=='Fill', 'Template root must occupy the full item width'
card=template_root.find('m:Border',namespaces=ns)
assert card is not None and card.get('Grid.Column')=='0'
assert card.get('MinimumHeightRequest')=='78'
assert card.get('Margin') is None, 'Card margin must not substitute native item spacing / real gutter'
assert '12,11,12,11' in etree.tostring(card,encoding='unicode')
# Selected styling must be data-bound at initial render (WinUI selection state
# can be deferred until the first click).
triggers=card.findall('m:Border.Triggers/m:DataTrigger',namespaces=ns)
assert len(triggers)==1 and triggers[0].get('Binding')=='{Binding IsSelected}'
assert triggers[0].get('Value')=='True'
setters=triggers[0].findall('m:Setter',namespaces=ns)
assert {s.get('Property') for s in setters}=={'BackgroundColor','Stroke'}
assert card.get('Stroke') and card.get('StrokeThickness')=='1.5'
assert card.get('{http://schemas.microsoft.com/winfx/2009/xaml}Name')=='ProfileCardBorder'
footer=one('ConfiguredPortsFooterLabel').getparent()
assert footer.tag.endswith('Grid')
assert footer.get('ColumnDefinitions')=='*,2*,*'
assert footer.get('Grid.Row')=='1', 'Log action toolbar removed; traffic footer now occupies row 1'
assert one('ConfiguredPortsFooterLabel').get('Grid.Column')=='0'
assert one('RunningModeFooterLabel').get('Grid.Column')=='2'
center=one('UploadSpeedLabel').getparent().getparent()
assert center.tag.endswith('Grid') and center.get('Grid.Column')=='1'
assert center.get('HorizontalOptions')=='Fill'
assert center.get('ColumnDefinitions')=='Auto,Auto,*,Auto'
assert center.get('RowDefinitions') is None, 'Traffic display must remain one row'
assert one('UploadSpeedLabel').getparent().get('Grid.Column')=='0'
assert one('DownloadSpeedLabel').getparent().get('Grid.Column')=='1'
assert one('TrafficTotalsLabel').get('Grid.Column')=='2'
assert one('TrafficAvailabilityLabel').get('Grid.Column')=='3'
for name in ('DownloadSpeedLabel','TrafficTotalsLabel','TrafficAvailabilityLabel'):
    assert one(name) in list(center.iter()),name
    assert one(name).get('MaxLines')=='1'
code=(root/'src/VelaRay.Maui/MainPage.xaml.cs').read_text(encoding='utf-8')
for name in ('UploadSpeedLabel','DownloadSpeedLabel','TrafficTotalsLabel','TrafficAvailabilityLabel', 'ConfiguredPortsFooterLabel','RunningModeFooterLabel'):
    assert name in code,name
print('PASS: native CollectionView 2dp spacing, actual card gutter outside border, selected state, centered one-line traffic')
