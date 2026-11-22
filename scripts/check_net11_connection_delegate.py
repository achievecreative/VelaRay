#!/usr/bin/env python3
"""Regression guard for the MAUI 11 connection action delegate and proxy mode events."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
code = (root / 'src/VelaRay.Maui/MainPage.xaml.cs').read_text(encoding='utf-8-sig')
xaml = (root / 'src/VelaRay.Maui/MainPage.xaml').read_text(encoding='utf-8-sig')

start = code.index('private async void ConnectionActionClicked(')
end = code.index('public async Task ToggleConnectionAsync(', start)
handler = code[start:end]
assert 'if (action == ConnectionAction.Disconnect)' in handler
assert 'RunSafelyAsync(() => DisconnectAsync(), "断开失败")' in handler
assert 'RunSafelyAsync(() => ConnectAsync(), "无法切换连接")' in handler
assert 'RunSafelyAsync(action == ConnectionAction.Disconnect ? DisconnectAsync : ConnectAsync' not in handler
assert '? DisconnectAsync' not in handler and '? ConnectAsync' not in handler
assert 'finally' in handler and '_connectionActionBusy = false;' in handler

for name, args in (
    ('ProxyModeCardCheckedChanged', 'CheckedChangedEventArgs'),
    ('PacProxyModeCardTapped', 'TappedEventArgs'),
    ('GlobalProxyModeCardTapped', 'TappedEventArgs'),
    ('ManualProxyModeCardTapped', 'TappedEventArgs'),
):
    assert re.search(rf'\bvoid\s+{name}\s*\(object\?\s+sender,\s*{args}\s+\w+\)', code), name
    assert name in xaml, f'{name} no longer used by XAML'

print('MAUI 11 connection-action delegate and nullable event bindings: OK')
