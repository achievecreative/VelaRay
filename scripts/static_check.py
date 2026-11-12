#!/usr/bin/env python3
"""VelaRay source integrity gates (NOT a substitute for dotnet build)."""
import json, re, sys
from pathlib import Path
import xml.etree.ElementTree as ET
R=Path(__file__).resolve().parent.parent
errors=[]
app=R/'src/VelaRay.Maui'
core=R/'src/VelaRay.Core'
main=(app/'MainPage.xaml').read_text(encoding='utf8')
code=(app/'MainPage.xaml.cs').read_text(encoding='utf8')
for path in R.rglob('*.xaml'):
    try: ET.parse(path)
    except ET.ParseError as e: errors.append(f'{path}: {e}')
for path in R.rglob('*.csproj'):
    try: ET.parse(path)
    except ET.ParseError as e: errors.append(f'{path}: {e}')
for path in R.rglob('*.json'):
    try: json.loads(path.read_text(encoding='utf8'))
    except json.JSONDecodeError as e: errors.append(f'{path}: {e}')
# Do not reintroduce the WPF application or old settings import code, even as dead UI.
for unwanted in ('LegacySettingsImporter','ImportClicked','Ui013','Ui068','V2RayW','V2RayM'):
    for path in list((R/'src').rglob('*.cs'))+list((R/'src').rglob('*.xaml'))+list((R/'src').rglob('*.csproj'))+list((R/'tests').rglob('*.cs')):
        if unwanted in path.read_text(encoding='utf8'):
            errors.append(f'{path}: forbidden obsolete code {unwanted}')
if (R/'legacy').exists(): errors.append('Original WPF archive must not be shipped')
if (core/'Migration').exists(): errors.append('Legacy settings importer folder still exists')
if any('ImportClicked' == item for item in re.findall(r'Clicked="([A-Za-z_]\w*)"',main)):
    errors.append('Legacy settings button is still present')
# Every named XAML event must have a code-behind implementation.
for page in app.glob('*.xaml'):
    codefile=Path(str(page)+'.cs')
    if not codefile.exists(): continue
    xaml=page.read_text(encoding='utf8')
    methods=codefile.read_text(encoding='utf8')
    for event in re.findall(r'\b(?:Clicked|Toggled|SelectedIndexChanged|SelectionChanged|TextChanged)="([A-Za-z_]\w*)"',xaml):
        if not re.search(r'\b(?:void|Task)\s+'+re.escape(event)+r'\s*\(',methods):
            errors.append(f'{page.name}: no event handler {event}')
# Ensure XAML resources exist in the app-level theme.
styles=(app/'Resources/Styles/Theme.xaml').read_text(encoding='utf8')
keys=set(re.findall(r'x:Key="([^"]+)"',styles))
for page in app.glob('*.xaml'):
    for key in re.findall(r'StaticResource\s+([^\s}"]+)',page.read_text(encoding='utf8')):
        if key not in keys: errors.append(f'{page.name}: missing style {key}')
locales=app/'Resources/I18n'
zh=json.loads((locales/'zh-CN.json').read_text(encoding='utf8'))
en=json.loads((locales/'en-US.json').read_text(encoding='utf8'))
if zh.keys()!=en.keys(): errors.append('Language keys differ between zh-CN and en-US')
if any(re.fullmatch(r'Ui\d+', k) for k in zh): errors.append('Numeric localization keys must be replaced with semantic names')
for page in app.glob('*.xaml'):
    for key in re.findall(r'\[([A-Z][A-Za-z0-9]*)\], Source=\{x:Static loc:UiStrings.Instance\}',page.read_text(encoding='utf8')):
        if key not in zh: errors.append(f'{page.name}: undefined localized key {key}')
if not (R/'VelaRay.slnx').exists(): errors.append('VelaRay solution missing')
if not all((R/item).exists() for item in ('src/VelaRay.Core/VelaRay.Core.csproj','src/VelaRay.Maui/VelaRay.Maui.csproj')):
    errors.append('Renamed VelaRay project missing')
if errors:
    print('\n'.join('FAIL '+e for e in errors));sys.exit(1)
print(f'PASS: VelaRay XML/JSON, UI handlers, locale keys, resources, and legacy-code removal checks')
print('NOTE: This is not a .NET 10 or Windows MAUI compiler.')
