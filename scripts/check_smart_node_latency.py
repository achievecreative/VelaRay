"""Source contracts for isolated smart TCP/UDP node probing."""
from pathlib import Path
import json
import re

root=Path(__file__).resolve().parent.parent
probe=(root/'src/VelaRay.Core/Runtime/SmartNodeLatencyProbe.cs').read_text()
main=(root/'src/VelaRay.Maui/MainPage.xaml.cs').read_text()
model=(root/'src/VelaRay.Core/Models/ServerProfile.cs').read_text()
xaml=(root/'src/VelaRay.Maui/MainPage.xaml').read_text()
for fragment in (
    'transport is "mkcp" or "quic" or "hysteria2"',
    'NodeProbeTransport.Tcp', 'NodeProbeTransport.Udp',
    'V4ConfigBuilder.BuildOutbound(profile, "latency-probe-out")',
    'V5ConfigBuilder.BuildOutbound(profile, "latency-probe-out")',
    '["listen"] = "127.0.0.1"',
    '["outbounds"] = new JsonArray(',
    'UseProxy = true', 'HttpStatusCode.NoContent',
    'MaxUdpAttempts = 2', 'MaxConcurrentProbes = 2',
    'process.Kill(entireProcessTree: true)',
    '_processLifetime.Attach(process)', 'File.Delete(configFile)',
    'catch (OperationCanceledException)', 'catch (HttpRequestException)',
    'await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead',
):
    assert fragment in probe, f'Missing smart probing contract: {fragment}'
assert 'new WebProxy(new Uri($"http://127.0.0.1:{port}"))' in probe
assert '["protocol"] = "freedom"' not in probe, 'Direct fallback is forbidden in isolated probe'
assert 'V2RayCoreRunner.BuildConfigArguments("run", configFile, profile.ConfigVersion)' in probe
assert 'NodeProbeTransport.Tcp => "TCP"' in main
assert 'NodeProbeTransport.Udp => "UDP"' in main
assert 'await _latencyProbe.ProbeAsync(snapshot, executable)' in main
assert 'SemaphoreSlim(SmartNodeLatencyProbe.MaxConcurrentProbes)' in main
assert 'ProbeMethodDisplay' in model and 'MeasuredLatencyMs' in model and '[JsonIgnore]' in model
assert 'JsonSerializer.Deserialize<ServerProfile>(JsonSerializer.Serialize(profile))' in main
assert '{Binding ProbeMethodDisplay}' in xaml and '{Binding LatencyDisplay}' in xaml
# All non-success probe outcomes use the localized Timeout label; only this state is red.
assert 'profile.ProbeStatusText = succeeded ? "" : L("节点测速超时");' in main
assert 'profile.IsProbeFailure = !succeeded;' in main
assert 'profile.IsProbeFailure = false;' in main
assert 'profile.ProbeStatusText = L("节点测速超时");' in main
assert 'p.IsProbeFailure = false;' in main
assert 'public bool IsProbeFailure' in model and 'nameof(IsProbeFailure)' in model
assert 'Binding IsProbeFailure' in xaml and 'DataTrigger TargetType="Label"' in xaml
assert 'Light=#D92D20, Dark=#FF7373' in xaml
assert 'Clicked="MeasureLatenciesClicked"' in xaml
for lang in ('zh-CN','en-US'):
    content=json.loads((root/f'src/VelaRay.Maui/Resources/I18n/{lang}.json').read_text())
    assert 'UDP' in content['HelpTestTcpLatency'] and content['ActionTestTcpLatency']
lookup=json.loads((root/'src/VelaRay.Maui/Resources/I18n/lookup.json').read_text())
for key in ('节点测速中','节点测速超时','节点配置无效','节点内核不可用','节点传输不支持','节点测速失败'):
    assert key in lookup
print('Smart TCP/UDP node probe source review passed.')
