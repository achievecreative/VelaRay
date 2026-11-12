#!/usr/bin/env python3
"""Traceable source-contract checks, NOT a C# compiler or actual V2Fly integration test."""
from pathlib import Path
import json,re,sys
R=Path(__file__).resolve().parents[1]
read=lambda p:(R/p).read_text(encoding='utf-8')
cs=read('src/VelaRay.Core/Configuration/SmartRoutingPolicy.cs')
v4=read('src/VelaRay.Core/Configuration/V4ConfigBuilder.cs')
v5=read('src/VelaRay.Core/Configuration/V5ConfigBuilder.cs')
models=read('src/VelaRay.Core/Models/ServerProfile.cs')
ui=read('src/VelaRay.Maui/MainPage.xaml')
code=read('src/VelaRay.Maui/MainPage.xaml.cs')
pac=read('src/VelaRay.Core/Pac/PacTemplateGenerator.cs')
tests=read('tests/VelaRay.SmokeTests/Program.cs')
zh=json.loads(read('src/VelaRay.Maui/Resources/I18n/zh-CN.json'))
en=json.loads(read('src/VelaRay.Maui/Resources/I18n/en-US.json'))
checks={
 'New settings default Smart with Direct PAC, no migration': 'RoutingMode { get; set; } = "Smart"' in models and 'PacTemplate { get; set; } = "DirectByDefault"' in models and 'RoutingMode { get; set; } = "Smart"' not in read('src/VelaRay.Core/Runtime/ClientSettingsStore.cs'),
 'Built-in + custom modes available': all(x in code for x in ('"Smart"','"ProxyAll"','"Direct"','"BypassCN"','"Custom"')),
 'Rule-set/name at top and DNS Picker fixed 188': ui.index('x:Name="RulePicker"') < ui.index('x:Name="VisualRuleList"') and 'x:Name="RouteStrategyPicker" WidthRequest="188"' in ui,
 'Read-only built-in preview and clear custom empty states': all(x in ui for x in ('BuiltInRoutePreview','CustomRouterPanel','RouteEmptyStateLabel','RouteListEmptyLabel','RouteDraftHelpLabel')),
 'Custom rule priority list and explicit Apply': all(x in ui for x in ('x:Name="VisualRuleList"','ActionAddVisualRule','ActionApplyVisualRule','ActionMoveRuleUp','ActionMoveRuleDown')) and '_creatingVisualRule' in code,
 'Domain & GeoSite / IP & GeoIP help bilingual': all(x in zh and x in en for x in ('HelpRoutingDomains','HelpRoutingIps','HelpRoutingAndConditions','RoutingManagedPacOnly')),
 'V4 and V5 Smart outputs distinct router names': 'SmartRoutingPolicy.BuildV4' in v4 and 'SmartRoutingPolicy.BuildV5' in v5 and '["rules"]' in cs and '["rule"]' in cs,
 'Explicit PAC overrides ahead of private/CN rules': cs.index('var (domains, ips) = SplitPacWebsites(settings);') < cs.index('var file = GeoDataAssets.NormalizeGeoIpFile(settings.GeoIpFile);'),
 'Default Direct and optional Proxy fallback preserve IPIfNonMatch in both formats': all(k in cs for k in ('IPIfNonMatch','IpIfNonMatch','Everything else → Direct','Everything else → Proxy','其余流量 → 直连','其余流量 → 代理')) and 'SmartRoutingPolicy.ProxyUnmatched(settings)' in v4 and 'SmartRoutingPolicy.ProxyUnmatched(options)' in v5 and 'SmartUnmatchedRoute { get; set; } = "Direct"' in models,
 'Smart balancing only on explicit PAC proxy rules': 'if ((string?)rule["outboundTag"] != "proxy") continue;' in v4 and 'if ((string?)rule["tag"] != "proxy") continue;' in v5 and 'if (options.RoutingMode == "Smart") routing["domainStrategy"]' not in v4 and 'if (options.RoutingMode == "Smart") router["domainStrategy"]' not in v5,
 'PAC browser and core priority aligned with actual PAC JS tests': 'EffectiveProxyWebsites' in pac and 'velaRayTemplateDirectSites' in pac and 'if (velarayMatches(host, velaRayProxySites))' in pac,
 'Added executable C# regression coverage (Windows run required)': all(x in tests for x in ('SmartRoutingPolicy.BuildV4','SmartRoutingPolicy.BuildV5','Smart V4 includes China domains','Smart V5 GeoSite category preserved','Smart V5 puts direct first for unmatched-traffic fallback','Balancing does not add a catch-all proxy/balancer rule')),
}
for name,passed in checks.items():print(('PASS' if passed else 'FAIL')+': '+name)
if not all(checks.values()):sys.exit(1)
print('Source contracts validated. The C# regression tests are NOT executed by this Python check.')
