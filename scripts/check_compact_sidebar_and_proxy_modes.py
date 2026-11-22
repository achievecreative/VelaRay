#!/usr/bin/env python3
"""UX contract: left-aligned navigation, 78dp comfortable server rows, responsive proxy cards."""
from pathlib import Path
from lxml import etree
import re

root = Path(__file__).resolve().parents[1]
xaml = (root/'src/VelaRay.Maui/MainPage.xaml').read_text(encoding='utf-8')
code = (root/'src/VelaRay.Maui/MainPage.xaml.cs').read_text(encoding='utf-8')
tree = etree.fromstring(xaml.encode('utf-8'))
ns={'x':'http://schemas.microsoft.com/winfx/2009/xaml'}

def named(name):
    found=tree.xpath('//*[@x:Name="%s"]'%name,namespaces=ns)
    assert len(found)==1,(name,len(found))
    return found[0]

# Preserve existing controls and exact click handlers; the native alignment is UI-only.
for name, handler in [('SettingsNavButton','ShowSettingsClicked'),
                      ('RoutingNavButton','ShowRoutingClicked'),
                      ('NodeNavButton','ShowNodeClicked')]:
    node=named(name)
    assert node.get('Clicked')==handler
    assert node.get('HandlerChanged')=='NavigationButtonHandlerChanged'
    assert node.get('ContentLayout')=='Left,6'
assert 'nativeButton.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;' in code
nav=code.split('private static void NavigationButtonHandlerChanged',1)[1].split('private void ProxyModeCardsGridSizeChanged',1)[0]
assert '#if WINDOWS' in nav and 'ToggleConnectionAsync' not in nav

# Server card: comfortable 78dp minimum, only name bold, 10dp scrollbar clearance.
items=xaml.split('<!-- Selected card uses blue outline;',1)[1].split('<Grid Grid.Row="4" ColumnDefinitions=',1)[0]
assert 'MinimumHeightRequest="92"' not in items
assert 'MinimumHeightRequest="78"' in items
assert 'LinearItemsLayout Orientation="Vertical" ItemSpacing="2"' in items
assert 'Grid ColumnDefinitions="*,10" HorizontalOptions="Fill"' in items
assert 'Margin="0,0,10,12"' not in items
assert 'Padding="12,11,12,11"' in items
assert 'RowSpacing="6"' in items
assert items.count('FontAttributes="Bold"')==1
for binding in ('Name','ProtocolBadge','EndpointDisplay','LatencyDisplay','IsConnected'):
    assert ('{Binding %s}' % binding) in items
assert 'DataTrigger TargetType="Border" Binding="{Binding IsSelected}"' in items

# Three modes: all contain wrapped descriptions and titles. Responsive columns never overflow.
modegrid=named('ProxyModeCardsGrid')
assert modegrid.tag.endswith('Grid')
assert modegrid.get('SizeChanged')=='ProxyModeCardsGridSizeChanged'
assert modegrid.get('ColumnDefinitions')=='*,*,*'
for name,mode,handler,title,description in [
    ('PacProxyModeCard','PAC','PacProxyModeCardTapped','ProxyModePacTitle','ProxyModePacDescription'),
    ('GlobalProxyModeCard','Global','GlobalProxyModeCardTapped','ProxyModeGlobalTitle','ProxyModeGlobalDescription'),
    ('ManualProxyModeCard','Manual','ManualProxyModeCardTapped','ProxyModeManualTitle','ProxyModeManualDescription'),
]:
    card=named(name)
    assert card.get('Style')=='{StaticResource ProxyModeChoiceCardStyle}'
    assert len(card.xpath('.//m:TapGestureRecognizer[@Tapped="%s"]'%handler,
                          namespaces={'m':'http://schemas.microsoft.com/dotnet/2021/maui'}))==1
    radio=named(name.replace('Card','Radio'))
    assert radio.get('Value')==mode
    assert radio.get('CheckedChanged')=='ProxyModeCardCheckedChanged'
    assert radio.get('GroupName')=='SystemProxyMode'
    assert title in etree.tostring(card,encoding='unicode') and description in etree.tostring(card,encoding='unicode')
    labels=card.xpath('.//m:Label',namespaces={'m':'http://schemas.microsoft.com/dotnet/2021/maui'})
    assert all(label.get('LineBreakMode')=='WordWrap' for label in labels)
resize=code.split('private void ProxyModeCardsGridSizeChanged',1)[1].split('private void ShowNodeClicked',1)[0]
assert 'grid.Width >= 830 ? 3 : grid.Width >= 540 ? 2 : 1' in resize
assert 'Grid.SetColumn(cards[i], i % columns)' in resize
assert 'Grid.SetRow(cards[i], i / columns)' in resize
for width, expected in ((400,1),(539,1),(540,2),(700,2),(829,2),(830,3),(1200,3)):
    result=3 if width>=830 else 2 if width>=540 else 1
    assert result==expected
for forbidden in ('ConnectAsync(', 'ToggleConnectionAsync(', '_proxy.Enable', 'SaveAllAsync(', '_core.Start('):
    assert forbidden not in resize
assert named('ProxyModePicker').get('IsVisible')=='False'
assert named('ProxyModePicker').get('SelectedIndexChanged')=='ProxyModeChanged'
print('PASS: 3 workspace tabs have native left-aligned contents without changing Clicked handlers')
print('PASS: 78dp server card, ItemSpacing 2dp, 10dp right reservation (actual clearance varies by scrollbar)')
print('PASS: PAC/Global/Manual cards reflow into 1, 2, or 3 columns, wrapped content, original mode events preserved')
