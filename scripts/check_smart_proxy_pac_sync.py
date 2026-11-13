#!/usr/bin/env python3
"""Static integration checks for opt-in Smart Proxy fallback and managed PAC suffix rules.

Source checks are not a replacement for compiling C# or running V2Fly.
"""
from pathlib import Path
from lxml import etree
import json

R = Path(__file__).resolve().parents[1]
read = lambda p: (R / p).read_text(encoding="utf-8")
model = read('src/VelaRay.Core/Models/ServerProfile.cs')
policy = read('src/VelaRay.Core/Configuration/SmartRoutingPolicy.cs')
v4 = read('src/VelaRay.Core/Configuration/V4ConfigBuilder.cs')
v5 = read('src/VelaRay.Core/Configuration/V5ConfigBuilder.cs')
pac = read('src/VelaRay.Core/Pac/PacTemplateGenerator.cs')
code = read('src/VelaRay.Maui/MainPage.xaml.cs')
ui = read('src/VelaRay.Maui/MainPage.xaml')
smoke = read('tests/VelaRay.SmokeTests/Program.cs')
zh = json.loads(read('src/VelaRay.Maui/Resources/I18n/zh-CN.json'))
en = json.loads(read('src/VelaRay.Maui/Resources/I18n/en-US.json'))
etree.fromstring(ui.encode('utf-8'))
required = ('FieldSmartUnmatchedRoute', 'SmartUnmatchedDirect', 'SmartUnmatchedProxy',
            'HelpSmartUnmatchedRoute', 'SmartManagedPacNotice', 'HelpPacSubdomains')
checks = {
    'Existing saved settings opt in only (defaults to Direct)':
        'SmartUnmatchedRoute { get; set; } = "Direct"' in model,
    'Smart Proxy fallback guards on routing mode and explicit value':
        'settings.RoutingMode == "Smart"' in policy and 'settings.SmartUnmatchedRoute, UnmatchedProxy' in policy,
    'PAC selected template is not silently overridden by Smart':
        'EffectiveManagedPacTemplate(_settings)' in code and
        'EffectiveManagedPacTemplate(ClientSettings settings) => settings.PacTemplate;' in policy,
    'Both generators change first outbound without wildcard network routing':
        'SmartRoutingPolicy.ProxyUnmatched(settings)' in v4 and 'SmartRoutingPolicy.ProxyUnmatched(options)' in v5 and
        'if (options.RoutingMode == "Smart")' in v4 and 'if (options.RoutingMode == "Smart")' in v5,
    'Domain-boundary PAC suffix match, case-insensitive':
        "host.substring(host.length - name.length - 1) === '.' + name" in pac and
        "(host || '').toLowerCase()" in pac,
    'Managed PAC generation and startup both use the selected template':
        code.count('SmartRoutingPolicy.EffectiveManagedPacTemplate(_settings)') >= 2 and
        'await WriteManagedPacAsync();' in code,
    'Smart fallback editable only in Proxy settings, read-only in Routing & PAC':
        'x:Name="SmartFallbackPicker"' in ui and 'x:Name="SmartFallbackReadOnlyLabel"' in ui and
        'SmartFallbackChanged' in code and 'RefreshSmartUnmatchedChoices' in code and
        'SmartFallbackRoutingPicker' not in ui,
    'Applying selection does not trigger connection':
        'private async void SmartFallbackChanged' in code and
        'await RunSafelyAsync(() => _store.SaveAsync(_settings)' in code,
    'Existing custom PAC file is not overwritten by Smart toggle':
        'if (_settings.ProxyMode == "PAC" && _settings.SelectedPacFile.Equals("managed.pac"' in code,
    'Localized messages present in both languages': all(k in zh and k in en for k in required),
    'PAC suffix and Smart Proxy regression test cases in smoke suite':
        'Smart Proxy fallback/PAC synchronization regression tests passed.' in smoke and
        'PAC accepts a root domain and wildcard entries' in smoke,
}
for label, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + label)
if not all(checks.values()):
    raise SystemExit(1)
print('Smart Proxy/PAC synchronization static checks passed (not an actual MAUI build).')
