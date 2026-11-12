#!/usr/bin/env python3
"""Ten traceable source checks for this change set; not a runtime compiler."""
from pathlib import Path
import json, re, sys, xml.etree.ElementTree as ET
r=Path(__file__).resolve().parent.parent
app=r/'src/VelaRay.Maui'
core=r/'src/VelaRay.Core'
code=(app/'MainPage.xaml.cs').read_text()
xaml=(app/'MainPage.xaml').read_text()
program=(app/'MauiProgram.cs').read_text()
instance=(app/'Services/WindowsSingleInstance.cs').read_text()
router=(core/'Configuration/VisualRoutingEditor.cs').read_text()
v4=(core/'Configuration/V4ConfigBuilder.cs').read_text()
v5=(core/'Configuration/V5ConfigBuilder.cs').read_text()
zh=json.loads((app/'Resources/I18n/zh-CN.json').read_text())
en=json.loads((app/'Resources/I18n/en-US.json').read_text())
tests=(r/'tests/VelaRay.SmokeTests/Program.cs').read_text()
checks={
    '01 · 语义化语言键':not any(re.fullmatch(r'Ui\d+', k) for k in zh) and len(zh) > 185 and zh.keys()==en.keys(),
    '02 · 即时切换语言': 'ChineseLanguageClicked' in xaml and 'EnglishLanguageClicked' in xaml and 'ChangeLanguageAsync' in code and 'UiStrings.Instance.ChangeLanguage(code)' in code,
    '03 · JSON格式单标签': 'Title="{Binding [ConfigJsonFormat]' not in xaml and xaml.count('[ConfigJsonFormat]')==1 and '[FieldConfigFormat]' not in xaml,
    '04 · 单实例启动互斥': 'new Mutex(initiallyOwned: true' in instance and program.index('WindowsSingleInstance.TryAcquireOrActivate') < program.index('MauiApp.CreateBuilder()'),
    '05 · 再次运行唤醒主窗口': 'signal.Set()' in instance and 'WindowsSingleInstance.ActivationRequested' in (app/'App.xaml.cs').read_text(),
    '06 · 高级路由可视化操作': all(s in xaml for s in ('VisualRuleList','RouteDomainsEditor','RouteIpsEditor','RoutePortsEntry','ActionMoveRuleUp','ActionMoveRuleDown')) and all(s in code for s in ('AddVisualRuleClicked','ApplyVisualRuleClicked','RemoveVisualRuleClicked','MoveSelectedVisualRuleAsync')),
    '07 · V4与V5路由序列化': 'outboundTag' in router and 'geoDomain' in router and 'geoip' in router and 'portList' in router and 'domainStrategy' in router,
    '08 · 保留高级字段和防误改': 'DeepClone()' in router and 'Balancer routing needs' in router and 'is not available. Use the raw JSON editor' in code,
    '09 · 全局路由与节点格式分离': 'RoutingRuleSets' in (core/'Models/ServerProfile.cs').read_text() and 'ConfigVersion { get; set; }' in (core/'Models/ServerProfile.cs').read_text() and 'ConvertRoutingToV5' in v5 and 'ParseCustomRouting' in v4,
    '10 · 单文件+Windows回归门禁': 'PublishSingleFile=true' in (r/'scripts/publish-windows.ps1').read_text() and 'VisualRoutingEditor.Upsert' in tests and 'dotnet build' in (r/'.github/workflows/windows.yml').read_text(),
}
for name, passed in checks.items():print(('PASS' if passed else 'FAIL'),name)
if not all(checks.values()):sys.exit(1)
print('10/10 focused source checks passed. .NET MAUI compilation and Windows GUI runtime are NOT covered.')
