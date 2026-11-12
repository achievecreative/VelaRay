#!/usr/bin/env python3
"""Keep server name and endpoint on the same left baseline (connected or not)."""
from pathlib import Path
from lxml import etree

root = Path(__file__).resolve().parents[1]
tree = etree.parse(str(root / 'src/VelaRay.Maui/MainPage.xaml'))
ns = {
    'm': 'http://schemas.microsoft.com/dotnet/2021/maui',
    'x': 'http://schemas.microsoft.com/winfx/2009/xaml',
}
item = tree.xpath('//m:CollectionView[@x:Name="ProfilesList"]/m:CollectionView.ItemTemplate/m:DataTemplate/m:Grid', namespaces=ns)
assert len(item) == 1
card = item[0].xpath('./m:Border[@x:Name="ProfileCardBorder"]', namespaces=ns)
assert len(card) == 1
content = card[0].xpath('./m:Grid/m:Grid[@Grid.Column="1"]', namespaces=ns)
assert len(content) == 1
body = content[0]
name_grid = body.xpath('./m:Grid[@Grid.Row="0"][@Grid.Column="0"]', namespaces=ns)
assert len(name_grid) == 1
heading = name_grid[0]
assert heading.get('ColumnDefinitions') == '*,Auto,Auto'
name = heading.xpath('./m:Label[@Text="{Binding Name}"]', namespaces=ns)
endpoint = body.xpath('./m:Label[@Text="{Binding EndpointDisplay}"]', namespaces=ns)
assert len(name) == len(endpoint) == 1
assert name[0].get('Grid.Column') == endpoint[0].get('Grid.Column') == '0'
assert endpoint[0].get('Grid.Row') == '1'
for label in (name[0], endpoint[0]):
    assert label.get('HorizontalOptions') == 'Fill'
    assert label.get('HorizontalTextAlignment') == 'Start'
assert len(heading.xpath('./m:Border[@Grid.Column="1"][@IsVisible="{Binding IsConnected}"]', namespaces=ns)) == 1
assert len(heading.xpath('./m:Border[@Grid.Column="2"]/m:Label[@Text="{Binding ProtocolBadge}"]', namespaces=ns)) == 1
assert len(body.xpath('./m:VerticalStackLayout[@Grid.Column="1"][@Grid.RowSpan="2"]', namespaces=ns)) == 1
print('PASS: server name and endpoint share one left baseline; status dot, protocol and right-side stats remain present')
