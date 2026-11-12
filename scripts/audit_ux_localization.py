#!/usr/bin/env python3
"""Focused cross-platform source checks for the Windows MAUI UI.
Does not replace building, rendering, or testing the app on Windows.
"""
from pathlib import Path
import xml.etree.ElementTree as ET
import json
import re
import struct
import sys

root=Path(__file__).resolve().parent.parent
app=root/'src/VelaRay.Maui'
errors=[]
xt=ET.parse(app/'MainPage.xaml')
ns='{http://schemas.microsoft.com/winfx/2009/xaml}'
xnodes=list(xt.iter())
name=lambda n: n.attrib.get(ns+'Name','')
byname={name(n):n for n in xnodes if name(n)}
langs={locale:json.loads((app/f'Resources/I18n/{locale}.json').read_text()) for locale in ('zh-CN','en-US')}
print('PASS 1: MainPage XAML parses')
# 2: i18n resource coverage
keys=set(re.findall(r'\[([A-Z][A-Za-z0-9]*)\], Source=\{x:Static loc:UiStrings.Instance\}',(app/'MainPage.xaml').read_text()))
for locale,d in langs.items():
  missing=keys-set(d)
  if missing:errors.append(f'{locale}: missing {sorted(missing)}')
print(f'PASS 2: UI localization bindings checked ({len(keys)} keys)')
# 3: all editable text fields have localizable examples or the two intentional syntax literals
controls=[n for n in xnodes if n.tag.rsplit('}',1)[-1] in ('Entry','Editor') and name(n) != 'LogEditor']
for n in controls:
  if 'Placeholder' not in n.attrib:errors.append(f'Missing placeholder: {name(n)}')
  p=n.attrib.get('Placeholder','')
  if p and p.startswith('{Binding'):
    match=re.search(r'\[([A-Z][A-Za-z0-9]*)\], Source=\{x:Static loc:UiStrings.Instance\}',p)
    if not match or match.group(1) not in langs['en-US'] or match.group(1) not in langs['zh-CN']:
      errors.append(f'Unlocalized placeholder: {name(n)}')
print(f'PASS 3: Editable input hints checked ({len(controls)} inputs)')
# 4: High-DPI, two-state segmented language chooser beside the current node.
parent={child:el for el in xnodes for child in el}
def ancestor(n, named):
  while n in parent:
    n=parent[n]
    if name(n)==named:return True
  return False
node=byname['ActiveNodeLabel']
language_zh=byname.get('ChineseLanguageButton')
language_en=byname.get('EnglishLanguageButton')
if any(old in byname for old in ('LanguagePicker','EnglishLanguageSwitch')):
    errors.append('Remove obsolete language picker/toggle')
if language_zh is None or language_zh.attrib.get('Clicked')!='ChineseLanguageClicked':
    errors.append('Missing explicit Chinese language option')
if language_en is None or language_en.attrib.get('Clicked')!='EnglishLanguageClicked':
    errors.append('Missing explicit English language option')
if language_zh is not None and language_en is not None and parent.get(language_zh) is not parent.get(language_en):
    errors.append('Language options must share the same segmented-control grid')
if not any(el.attrib.get('Grid.Column')=='2' and node in list(el.iter()) for el in xnodes):
    errors.append('Current node must be in fixed column 2, left of language control')
if language_en is not None and not any(el.attrib.get('Grid.Column')=='3' and language_en in list(el.iter()) for el in xnodes):
    errors.append('Language choices must be in fixed-width column 3')
print('PASS 4: Stable segmented language chooser beside current node checked')
# 5: Node JSON picker is inline with Connection heading, not in an info card.
picker=byname['ConfigVersionPicker']
heading_row=parent.get(picker)
if heading_row is None or heading_row.tag.rsplit('}',1)[-1]!='Grid':
    errors.append('Config picker must be in an inline Grid')
else:
    labels=[n for n in heading_row if n.tag.rsplit('}',1)[-1]=='Label']
    if not any('[SectionConnection]' in n.attrib.get('Text','') for n in labels):
        errors.append('JSON format picker must share the Connection heading row')
    if not any('[ConfigJsonFormat]' in n.attrib.get('Text','') for n in labels):
        errors.append('Inline JSON format label missing')
    if picker.attrib.get('Grid.Column')!='2':
        errors.append('Config picker must be at the right end of connection row')
    if 'Title' in picker.attrib:
        errors.append('Config picker must not repeat the single JSON format label in Title')
if not ancestor(picker,'NodeSection'):
    errors.append('Config version must belong to the selected node')
for old in ('CompatibilityNoticeLabel','SettingsFormatHintLabel','DisabledOptionsHintLabel'):
    if old in byname: errors.append('Unwanted format explanation: '+old)
for local in ('NameEntry','UuidEntry','TransportPicker'):
  if not ancestor(byname[local],'NodeSection'):errors.append(f'{local} not node-specific')
for global_ in ('SocksPortEntry','ProxyModePicker'):
  if not ancestor(byname[global_],'SettingsSection'):errors.append(f'{global_} not global')
for global_ in ('PacTemplatePicker','GeoIpFilePicker','RoutingJsonEditor'):
  if not ancestor(byname[global_],'RoutingSection'):errors.append(f'{global_} not global route')
# Global pages remain together at the top; server editing is pinned below the server list.
node_nav=byname['NodeNavButton']
settings_nav=byname['SettingsNavButton']
routing_nav=byname['RoutingNavButton']
if parent.get(settings_nav) is not parent.get(routing_nav):
    errors.append('Global navigation buttons should be grouped together')
if node_nav.attrib.get('Grid.Row') != '5' or byname['ProfilesList'].attrib.get('Grid.Row') != '3':
    errors.append('Server settings navigation must be below the server list, at the bottom')
if not ('NavServerSettings' in node_nav.attrib.get('Text','')):
    errors.append('Server settings navigation must have a semantic localized label')
if not (settings_nav.attrib.get('AutomationId') and node_nav.attrib.get('AutomationId')):
    errors.append('Navigation needs stable accessibility/test identifiers')
if not any('GlobalScopeCardStyle' == el.attrib.get('Style','').replace('{StaticResource ','').replace('}','')
           for el in xnodes if ancestor(el,'SettingsSection')):
    errors.append('Missing right-hand global settings banner')
if not any('GlobalScopeCardStyle' == el.attrib.get('Style','').replace('{StaticResource ','').replace('}','')
           for el in xnodes if ancestor(el,'RoutingSection')):
    errors.append('Missing right-hand global routing banner')
print('PASS 5: Inline per-node JSON format, global banners and navigation checked')
# 6: style resource keys
style=ET.parse(app/'Resources/Styles/Theme.xaml')
stylekeys={el.attrib.get(ns+'Key') for el in style.iter()}
if 'GlobalScopeCardStyle' not in stylekeys:errors.append('Missing global scope border style')
print('PASS 6: Global-scope visual style checked')
# 7: runtime diagnostics in selected locale
ui=(app/'Localization/UiStrings.cs').read_text()
page=(app/'MainPage.xaml.cs').read_text()
for required in ('TranslateDiagnostic', 'V2Fly 配置验证失败', '内核或系统原始信息'):
  if required not in ui:errors.append(f'Localized diagnostic missing: {required}')
if 'UiStrings.Instance.TranslateDiagnostic(ex.Message)' not in page:
  errors.append('Exception alerts/logs are not localized')
print('PASS 7: Localized validation errors and preserved native output checked')
# 8: native titlebar and icon embedding: signed ICO header and WinUI API
shell=(app/'Services/WindowsWindowShell.cs').read_text()
proj=(app/'VelaRay.Maui.csproj').read_text()
ico=app/'Resources/AppIcon/velaray.ico'
raw=ico.read_bytes()
if struct.unpack_from('<HHH',raw)[:2] !=(0,1):errors.append('Invalid ICO header')
if 'AppWindow.SetIcon(iconPath)' not in shell:errors.append('No WinUI AppWindow.SetIcon call')
if 'VelaRay.Assets.velaray.ico' not in proj or 'GetManifestResourceStream("VelaRay.Assets.velaray.ico")' not in shell:
  errors.append('Titlebar icon not embedded consistently')
if 'Source="velaray_logo.png"' not in (app/'MainPage.xaml').read_text():
  errors.append('Logo missing from app header')
print('PASS 8: Windows titlebar native .ico / embedded resource checked')
# 9: Avoid WinUI picker popup size dependency for two locales.
program=(app/'MauiProgram.cs').read_text()
if 'VelaRayLanguagePicker' in program or 'LanguagePicker' in program:
    errors.append('Obsolete global Picker mapping still present')
for method in ('ChineseLanguageClicked','EnglishLanguageClicked','UpdateLanguageButtons','ChangeLanguageAsync'):
    if method not in page: errors.append('Missing language switch handler: '+method)
print('PASS 9: Explicit language selection implementation checked')
# 10: no forbidden placeholders (malformed markup extension) and no stale navigation guidance
for n in xnodes:
 for k,v in n.attrib.items():
  if k.endswith('Placeholder') and v.startswith('{') and not (v.startswith('{}') or v.startswith('{Binding')):
   errors.append(f'Suspicious XAML placeholder: {name(n)}')
if 'V2Fly · .NET 10 · V4 / V5' in (app/'MainPage.xaml').read_text():
  errors.append('Decorative framework/version tagline remains')
if 'Title = "VelaRay ·' in (app/'App.xaml.cs').read_text():
  errors.append('OS window title must contain only VelaRay')
if 'SettingsFormatHintLabel' in page or 'CompatibilityNoticeLabel' in page:
  errors.append('Old format guidance remains in code-behind')
if '使用节点与传输页面顶部的 V4 / V5 切换器。' in page:
  errors.append('Stale config-format location guidance remains')
# New source requirements: single-instance is acquired before services are initialized,
# and visual route controls are both implemented and localized.
instance=(app/'Services/WindowsSingleInstance.cs').read_text()
if 'new Mutex(initiallyOwned: true' not in instance or 'TryAcquireOrActivate' not in program:
    errors.append('Missing single-instance mutex gate')
if program.index('WindowsSingleInstance.TryAcquireOrActivate') > program.index('builder.UseMauiApp'):
    errors.append('Single-instance guard must run before services are created')
if not all(name in byname for name in ('VisualRuleList','RouteTargetPicker','RouteDomainsEditor','RouteIpsEditor','RoutePortsEntry','RouteRawModeSwitch','RouteDetailPanel','CustomRouterPanel')):
    errors.append('Visual routing editor controls missing')
if re.search(r'Ui\d{3}', (app/'MainPage.xaml').read_text() + (app/'Resources/I18n/zh-CN.json').read_text()):
    errors.append('Numeric localization keys remain')
print('PASS 10: XAML literals, visual routing, single-instance and semantic locale keys checked')
if errors:
 for err in errors:print('FAIL:',err)
 sys.exit(1)
print('PASS: all 10 UX localization/titlebar static checks. Windows compilation/rendering NOT tested.')
