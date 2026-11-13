#!/usr/bin/env python3
"""Contract/regression check for PAC/Global/Manual switching and strict PAC choices.

This checks source wiring only; Windows WinINET behavior still needs a real runtime test.
"""
from pathlib import Path
from lxml import etree
import json

root = Path(__file__).resolve().parents[1]
read=lambda relative: (root/relative).read_text(encoding='utf-8')
xaml=read('src/VelaRay.Maui/MainPage.xaml')
code=read('src/VelaRay.Maui/MainPage.xaml.cs')
policy=read('src/VelaRay.Core/Configuration/SmartRoutingPolicy.cs')
detector=read('src/VelaRay.Core/Runtime/RunningConfigChangeDetector.cs')
system_proxy=read('src/VelaRay.Maui/Services/WindowsSystemProxyService.cs')
pac_server=read('src/VelaRay.Maui/Services/PacHttpServer.cs')
smoke=read('tests/VelaRay.SmokeTests/Program.cs')
xml=etree.fromstring(xaml.encode())
ns={'m':'http://schemas.microsoft.com/dotnet/2021/maui','x':'http://schemas.microsoft.com/winfx/2009/xaml'}
list_ = xml.xpath('//m:CollectionView[@x:Name="ProfilesList"]',namespaces=ns)[0]
layout=list_.xpath('./m:CollectionView.ItemsLayout/m:LinearItemsLayout',namespaces=ns)
assert len(layout)==1 and layout[0].get('ItemSpacing')=='2', '2dp native server card spacing'
root_template=list_.xpath('./m:CollectionView.ItemTemplate/m:DataTemplate/m:Grid',namespaces=ns)[0]
assert root_template.get('ColumnDefinitions')=='*,10', '10dp reserved column; visible clearance depends on scrollbar width'
assert 'EffectiveManagedPacTemplate(ClientSettings settings) => settings.PacTemplate;' in policy
assert 'ProxyUnmatched(settings) && settings.PacTemplate' not in policy
assert 'PAC Direct-by-default selection' in smoke
assert 'PAC Proxy-by-default remains an explicit separate choice' in smoke
assert 'system proxy mode alone does not require restarting the core' in smoke
assert 'string.Equals(runningProxyMode, current.ProxyMode' not in detector
assert 'ConfigFlavor.Build(current, original)' in detector
assert 'runningPacPort != current.PacPort' in detector
start=code.index('private async Task<bool> ApplySavedProxyModeWithoutCoreRestartAsync()')
stop=code.index('private async void ExportClicked',start)
switch=code[start:stop]
for desired in ('_connectionGate.WaitAsync()', 'UpdatePendingCoreChanges()', 'await WriteManagedPacAsync()',
                '_pac.Start(', '_proxy.EnablePac(', '_proxy.EnableGlobal(', '_proxy.Restore()',
                'previousMode', '_runningProxyMode = _settings.ProxyMode;'):
    assert desired in switch,desired
for forbidden in ('_core.StartAsync(', '_core.StopAsync(', 'ConnectAsync(', 'DisconnectAsync(', 'ToggleConnectionAsync('):
    assert forbidden not in switch, forbidden
assert 'await ApplySavedProxyModeWithoutCoreRestartAsync()' in code
assert 'ReferenceEquals(sender, SaveProfileButton)' in code
assert 'if (!File.Exists(pacFile)) throw new FileNotFoundException' in pac_server
for method,nextmethod in (('public void EnablePac(','public void Disable('),
                          ('public void EnableGlobal(','public void EnablePac(')):
    a=system_proxy.split(method,1)[1].split(nextmethod,1)[0]
    if 'EnablePac' in method:
        for text in ('key.SetValue("ProxyEnable", 0', 'key.DeleteValue("ProxyServer", false)',
                     'key.SetValue("AutoConfigURL",', 'Refresh();'):
            assert text in a,text
    else:
        assert 'key.SetValue("ProxyEnable", 1' in a
        assert 'key.DeleteValue("AutoConfigURL", false)' in a
for code_ in ('zh-CN','en-US'):
    resource=json.loads(read(f'src/VelaRay.Maui/Resources/I18n/{code_}.json'))
    assert 'SmartManagedPacNotice' in resource
    assert 'PAC' in resource['SmartManagedPacNotice']
assert '系统代理模式已应用，无需重新连接' in json.loads(read('src/VelaRay.Maui/Resources/I18n/lookup.json'))
print('PASS: 2dp server spacing, 10dp card-to-scrollbar rail, PAC template autonomy')
print('PASS: mode change on Save uses WinINET + PAC only; core/routing pending stays deferred')
print('PASS: Global -> PAC clears manual system proxy; no silent missing-file proxy-all fallback')
