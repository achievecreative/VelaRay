#!/usr/bin/env python3
"""Static migration gate. Does not replace a .NET 11 build or WinUI UI test."""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
projects = {
    'src/VelaRay.Core/VelaRay.Core.csproj': 'net11.0',
    'src/VelaRay.Maui/VelaRay.Maui.csproj': 'net11.0-windows10.0.19041.0',
    'tests/VelaRay.SmokeTests/VelaRay.SmokeTests.csproj': 'net11.0',
    'tests/VelaRay.WindowsLifetimeTests/VelaRay.WindowsLifetimeTests.csproj': 'net11.0-windows',
}
for path, tfm in projects.items():
    p = root / path
    tree = ET.parse(p)
    values = [(x.text or '').strip() for x in tree.iter() if x.tag in {'TargetFramework', 'TargetFrameworks'}]
    assert values == [tfm], f'{path}: expected one target {tfm}, got {values}'
    assert not re.search(r'net10\.0(?:-windows)?', p.read_text()), f'{path}: old framework remains'
    print(f'OK: {path} -> {tfm}')

maui = ET.parse(root / 'src/VelaRay.Maui/VelaRay.Maui.csproj')
packages = {p.attrib['Include']: p.attrib['Version'] for p in maui.iter('PackageReference')}
assert packages['Microsoft.Maui.Controls'] == '11.0.10', packages
assert packages['Microsoft.Extensions.Logging.Debug'] == '11.0.0', packages
assert all('Compatibility' not in p for p in packages)
assert [p.text for p in maui.iter('UseMaui')] == ['true']
assert [p.text for p in maui.iter('UseWindowsCollectionView2Handler')] == ['true']
assert [p.text for p in maui.iter('WindowsPackageType')] == ['None']
assert [p.text for p in maui.iter('WindowsAppSDKSelfContained')] == ['true']
print('OK: stable MAUI 11.0.10, logging 11.0.0, Windows CollectionView2 enabled')

sdk = json.loads((root/'global.json').read_text())['sdk']
assert sdk['version'] == '11.0.100'
assert sdk['rollForward'] == 'latestFeature'
assert sdk['allowPrerelease'] is False
print('OK: .NET 11 SDK selection with feature roll-forward')

for file in ('scripts/verify-windows.ps1','scripts/publish-windows.ps1','.github/workflows/windows.yml'):
    t = (root/file).read_text()
    assert 'net11.0-windows10.0.19041.0' in t, file
    assert 'net10.0-windows10.0.19041.0' not in t, file
assert "dotnet-version: '11.0.x'" in (root/'.github/workflows/windows.yml').read_text()
assert 'dotnet workload install maui-windows' in (root/'.github/workflows/windows.yml').read_text()
assert "dotnet run --project tests/VelaRay.SmokeTests" in (root/'scripts/verify-windows.ps1').read_text()
assert 'PublishSingleFile=true' in (root/'scripts/publish-windows.ps1').read_text()
assert 'UseWindowsCollectionView2Handler' in (root/'docs/NET11_MAUI11_MIGRATION.md').read_text()

# MAUI's XAML and config files are structurally valid, without changing UI behavior.
for p in (root/'src/VelaRay.Maui').rglob('*.xaml'):
    ET.parse(p)
for p in (root/'src/VelaRay.Maui/Resources/I18n').glob('*.json'):
    json.loads(p.read_text())
print('OK: CI/build/publish target alignment; all XAML/i18n parse; UI and business behavior must be reviewed separately')
print('NOTE: A Windows MAUI build and rendered behavior still require .NET 11 SDK + MAUI Windows workload on Windows.')
