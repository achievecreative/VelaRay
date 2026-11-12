#!/usr/bin/env python3
"""Ten source-level regression gates; Windows CI runs actual C# builds separately."""
from pathlib import Path
import subprocess,sys
R=Path(__file__).resolve().parent.parent
def src(path):return (R/path).read_text(encoding='utf8')
def check(label,value):
    if not value:raise AssertionError(label)
    print('PASS',label)
core=src('src/VelaRay.Core/Configuration/ConfigFlavor.cs')
page=src('src/VelaRay.Maui/MainPage.xaml.cs')
xaml=src('src/VelaRay.Maui/MainPage.xaml')
store=src('src/VelaRay.Core/Runtime/ClientSettingsStore.cs')
v4=src('src/VelaRay.Core/Configuration/V4ConfigBuilder.cs')
v5=src('src/VelaRay.Core/Configuration/V5ConfigBuilder.cs')
runner=src('src/VelaRay.Core/Runtime/V2RayCoreRunner.cs')
workflow=src('.github/workflows/windows.yml')
check('01 no original WPF source archive',not (R/'legacy').exists())
check('02 old settings importer removed',not (R/'src/VelaRay.Core/Migration').exists() and 'ImportClicked' not in page and 'ImportClicked' not in xaml)
check('03 application namespaces and project files renamed', 'namespace VelaRay.Maui;' in page and 'VelaRay.Core.Models' in src('src/VelaRay.Core/Models/ServerProfile.cs') and (R/'VelaRay.slnx').exists())
check('04 V4/V5 loaders retained','"jsonv5"' in core and '"json"' in core and 'BuildConfigArguments(command, configPath, format)' in runner)
check('05 VMess and V4 TLS features retained','AllowInsecureSwitch' in xaml and 'VmessAlterIdEntry' in xaml and 'tls["allowInsecure"] = true' in v4 and 'security["allowInsecure"]' not in v5)
check('06 GeoIP routing retained','"router"' in v5 and '"routing"' in v4 and 'GeoIpFilePicker' in xaml)
check('07 local PAC and three proxy modes retained','PacTemplateGenerator.Generate' in page and 'EnableGlobal' in page and 'EnablePac' in page and '"Manual"' in page)
check('08 portable configs and settings retained',all('"'+x+'"' in store for x in ('configs','settings','pacs','logs')) and 'Environment.ProcessPath' in src('src/VelaRay.Maui/MauiProgram.cs'))
check('09 Windows core lifetime and settings restore retained','AssignProcessToJobObject' in src('src/VelaRay.Maui/Services/WindowsJobObjectLifetime.cs') and 'SameProxySettings' in src('src/VelaRay.Maui/Services/WindowsSystemProxyService.cs'))
check('10 Windows build and standalone app publish enforced','dotnet build' in workflow and 'PublishSingleFile=true' in src('scripts/publish-windows.ps1') and 'VelaRay.SmokeTests' in workflow)
for name in ('static_check.py','check_csharp_structure.py','check_pac_js.py','audit_ux_localization.py'):
    result=subprocess.run([sys.executable,str(R/'scripts'/name)],check=False)
    check(name,result.returncode==0)
print('10 static review gates passed; .NET/MAUI compilation not executed by this script.')
