#!/usr/bin/env python3
"""Source regression: plain VMess/TCP must never produce V4 header:{}.
Run C# smoke tests and V2Ray's native config test on Windows for full validation.
"""
from pathlib import Path
r = Path(__file__).resolve().parents[1]
v4 = (r/'src/VelaRay.Core/Configuration/V4ConfigBuilder.cs').read_text(encoding='utf-8')
v5 = (r/'src/VelaRay.Core/Configuration/V5ConfigBuilder.cs').read_text(encoding='utf-8')
ui = (r/'src/VelaRay.Maui/MainPage.xaml.cs').read_text(encoding='utf-8')
tests = (r/'tests/VelaRay.SmokeTests/Program.cs').read_text(encoding='utf-8')
assert '"tcp" => BuildTcpSettings(p.TcpHeaderJson)' in v4
assert 'var header = V5ConfigBuilder.ParseTcpHeader(json);' in v4
assert 'header.Count == 0' in v4
assert 'public static JsonObject ParseTcpHeader(string? json)' in v5
assert 'if (string.IsNullOrWhiteSpace(json)) return new JsonObject();' in v5
assert 'TCP header JSON must include a non-empty' in v5
assert 'if (p.Transport == "tcp") ParseTcpHeader(p.TcpHeaderJson);' in v5
assert 'string.IsNullOrWhiteSpace(TcpHeaderEntry.Text)' in ui
assert 'V4 plain TCP does not emit an empty header object' in tests
assert 'V5 plain TCP does not emit an empty header object' in tests
assert 'V4 preserves explicit HTTP TCP disguise header' in tests
assert 'Invalid V4 TCP header produces an actionable error' in tests
print('PASS: V4/V5 TCP header empty/none normalization and input validation')
