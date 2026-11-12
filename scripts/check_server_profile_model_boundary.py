#!/usr/bin/env python3
"""Prevent UI-only ServerProfile card members leaking into RoutingRuleSet.

This would otherwise compile as CS0103 (unresolved PropertyChanged/Protocol/Address/Port).
The test intentionally complements, not replaces, a real dotnet build.
"""
from pathlib import Path

source=(Path(__file__).resolve().parents[1]/'src/VelaRay.Core/Models/ServerProfile.cs').read_text(encoding='utf-8')
assert source.count('public sealed class ServerProfile : INotifyPropertyChanged') == 1
assert source.count('public sealed class RoutingRuleSet') == 1
assert source.count('public sealed class ClientSettings') == 1
server=source.split('public sealed class ServerProfile : INotifyPropertyChanged',1)[1].split('public sealed class RoutingRuleSet',1)[0]
routing=source.split('public sealed class RoutingRuleSet',1)[1].split('public sealed class ClientSettings',1)[0]
for member in ('TcpLatencyMs','LatencyDisplay','ProtocolBadge','EndpointDisplay','NotifyCardChanged'):
    assert member in server, f'Missing server card property: {member}'
    assert member not in routing, f'Server UI presentation member leaked into routing model: {member}'
for wrong in ('PropertyChanged','Address','Protocol','Port','_tcpLatencyMs','IsConnected'):
    assert wrong not in routing, f'Unknown server member in RoutingRuleSet: {wrong}'
for required in ('public string Id','public string Name','public string Json'):
    assert required in routing, f'RoutingRuleSet lost its persisted field: {required}'
assert source.count('public string ProtocolBadge') == 1
assert source.count('public void NotifyCardChanged()') == 1
print('PASS: ServerProfile card presentation members belong only to ServerProfile; RoutingRuleSet retains Id/Name/Json')
