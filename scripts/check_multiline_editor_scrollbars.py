"""Regression guard: long PAC/routing content scrolls inside bounded Editors."""
from pathlib import Path
import xml.etree.ElementTree as ET

root=Path(__file__).resolve().parent.parent
maui=(root/'src/VelaRay.Maui/MauiProgram.cs').read_text(encoding='utf-8')
for required in (
    '#if WINDOWS',
    'EditorHandler.Mapper.AppendToMapping(',
    'ScrollViewer.SetVerticalScrollBarVisibility(',
    'ScrollBarVisibility.Auto',
    'handler.PlatformView',
):
    assert required in maui, f'Missing native Windows Editor scrollbar support: {required}'

ns='{http://schemas.microsoft.com/dotnet/2021/maui}'
page=ET.parse(root/'src/VelaRay.Maui/MainPage.xaml').getroot()
editors={node.attrib.get('{http://schemas.microsoft.com/winfx/2009/xaml}Name'):node
         for node in page.iter(ns+'Editor')}
for name in (
    'PacDirectSitesEditor', 'PacProxySitesEditor', 'PacScriptEditor',
    'RouteDomainsEditor', 'RouteIpsEditor', 'RoutingJsonEditor',
    'BuiltInRouteJsonPreview', 'WsHeadersEntry', 'TcpHeaderEntry',
):
    assert name in editors, f'Missing multiline editor: {name}'
    node=editors[name]
    assert int(node.attrib.get('HeightRequest','0'))>0, f'Editor must stay bounded: {name}'
    assert node.attrib.get('AutoSize','Disabled')=='Disabled', f'Auto-growing editor defeats scrollbars: {name}'
    assert 'VerticalScrollBarVisibility' not in node.attrib, f'Unsupported MAUI Editor attribute: {name}'

pac=ET.parse(root/'src/VelaRay.Maui/PacPreviewPage.xaml').getroot()
preview=[e for e in pac.iter(ns+'Editor') if e.attrib.get('{http://schemas.microsoft.com/winfx/2009/xaml}Name')=='PacPreviewEditor']
assert len(preview)==1 and preview[0].attrib.get('IsReadOnly')=='True'

# Do not rewrap the editors in child ScrollViews; the WinUI TextBox has a native
# embedded ScrollViewer, while the workspace remains scrollable independently.
assert page.findall('.//'+ns+'Editor/'+ns+'ScrollView')==[]
print('Multiline Editor scrollbar / bounded height checks passed.')
