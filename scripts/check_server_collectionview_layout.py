#!/usr/bin/env python3
"""MAUI CollectionView native spacing, outside-card scrollbar gutter, and selected-state contract."""
from pathlib import Path
from lxml import etree
root=Path(__file__).resolve().parents[1]
p=root/'src/VelaRay.Maui/MainPage.xaml'
t=etree.parse(str(p))
n={'m':'http://schemas.microsoft.com/dotnet/2021/maui', 'x':'http://schemas.microsoft.com/winfx/2009/xaml'}
c=t.xpath('//m:CollectionView[@x:Name="ProfilesList"]',namespaces=n)
assert len(c)==1
v=c[0]
assert v.get('SelectionMode')=='Single' and v.get('SelectionChanged')=='ProfilesSelectionChanged'
layout=v.xpath('./m:CollectionView.ItemsLayout/m:LinearItemsLayout',namespaces=n)
assert len(layout)==1 and layout[0].get('Orientation')=='Vertical' and layout[0].get('ItemSpacing')=='2'
templates=v.xpath('./m:CollectionView.ItemTemplate/m:DataTemplate/m:Grid',namespaces=n)
assert len(templates)==1
wrap=templates[0]
assert wrap.get('ColumnDefinitions')=='*,10' and wrap.get('HorizontalOptions')=='Fill'
card=wrap.xpath('./m:Border',namespaces=n)
assert len(card)==1
card=card[0]
assert card.get('Grid.Column')=='0' and card.get('MinimumHeightRequest')=='78' and card.get('Margin') is None
assert card.get('{http://schemas.microsoft.com/winfx/2009/xaml}Name')=='ProfileCardBorder'
triggers=card.xpath('./m:Border.Triggers/m:DataTrigger[@Binding="{Binding IsSelected}"][@Value="True"]',namespaces=n)
assert len(triggers)==1, 'Selected border must use model binding on first render'
setters=triggers[0].xpath('./m:Setter',namespaces=n)
assert {s.get('Property') for s in setters}=={'BackgroundColor','Stroke'}
assert card.get('Stroke') and card.get('StrokeThickness')=='1.5', 'Unselected border visible before selection'
for b in ('Name','ProtocolBadge','EndpointDisplay','LatencyDisplay','IsConnected'):
    assert '{Binding %s}'%b in etree.tostring(card,encoding='unicode')
print('PASS: 2dp native vertical spacing; 10dp dedicated right rail; actual clearance varies with scrollbar width; 78dp card; selection and bindings preserved')
