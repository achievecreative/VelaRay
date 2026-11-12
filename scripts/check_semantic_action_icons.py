#!/usr/bin/env python3
"""Static UX-only regression checks for semantic button icons (requires no .NET SDK)."""
from pathlib import Path
from lxml import etree
import re
root=Path(__file__).resolve().parents[1]
maui=root/'src/VelaRay.Maui'
old_icon_refs=[]
num=0
for name in ('MainPage.xaml','ConfigViewerPage.xaml','PacPreviewPage.xaml'):
    x=(maui/name).read_text()
    tree=etree.fromstring(x.encode())
    ns={'m':'http://schemas.microsoft.com/dotnet/2021/maui'}
    for b in tree.xpath('//m:Button',namespaces=ns):
        if b.attrib.get('Clicked') in ('ChineseLanguageClicked','EnglishLanguageClicked'):
            continue # segmented control must remain text-only
        assert b.attrib.get('ImageSource'), (name,b.attrib.get('Clicked'),'missing icon')
        assert b.attrib.get('ContentLayout')=='Left,6', (name,b.attrib.get('Clicked'),'missing spacing')
        img=b.attrib['ImageSource']
        assert (maui/'Resources/Images/ActionIcons'/img.replace('.png','.svg')).exists(),img
        num+=1
        old_icon_refs.append(b.attrib.get('Clicked'))
assert num>=40,num
source=(maui/'MainPage.xaml.cs').read_text()
assert 'ConnectionAction.Disconnect => Application.Current?.RequestedTheme == AppTheme.Dark' in source
assert (maui/"Resources/Images/ActionIcons/action_power_danger_dark.svg").exists()
assert 'ConnectionAction.Switch => "action_switch_primary.png"' in source
assert '_ => "action_plug_primary.png"' in source
assert 'ContentLayout="Left,6"' in (maui/'MainPage.xaml').read_text()
assert 'MauiImage Include="Resources/Images/ActionIcons/*.svg"' in (maui/'VelaRay.Maui.csproj').read_text()
for n in ('power','plug','switch'):
    assert (maui/f'Resources/Images/ActionIcons/action_{n}_primary.svg').exists()
print('PASS:',num,'visual icon assignments across three screens')
print('PASS: connection action dynamic icons, vector assets, no font dependency')
