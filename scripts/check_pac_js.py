"""Check the PAC JavaScript body extracted from the C# raw-string template.

Requires Python 3 and Node.js. This does NOT compile or execute the C# generator;
C# generator behavior is covered separately by VelaRay.SmokeTests.
"""
from pathlib import Path
import json
import subprocess

source = (Path(__file__).resolve().parents[1] / "src/VelaRay.Core/Pac/PacTemplateGenerator.cs").read_text(encoding="utf-8")
script = source.split('return $$"""', 1)[1].split('""";', 1)[0]


def run_case(label, template, direct_sites, proxy_sites, port, expected):
    js = (script.replace("{{directJs}}", json.dumps(direct_sites))
          .replace("{{proxyJs}}", json.dumps(proxy_sites))
          .replace("{{templateDirectJs}}", json.dumps(["baidu.com", "qq.com", "weixin.qq.com", "taobao.com", "tmall.com", "jd.com", "bilibili.com", "zhihu.com", "163.com", "sina.com.cn", "alipay.com", "douyin.com"] if template == "ChinaSitesDirect" else []))
          .replace("{{httpPort}}", str(port))
          .replace("{{defaultRoute}}", "DIRECT" if template == "DirectByDefault" else f"PROXY 127.0.0.1:{port}"))
    bootstrap = r"""
function isPlainHostName(host) { return host.indexOf('.') === -1; }
function shExpMatch(str, glob) {
    var escaped = glob.replace(/[.+^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*').replace(/\?/g, '.');
    return new RegExp('^' + escaped + '$').test(str);
}
"""
    cases = json.dumps(expected)
    program = bootstrap + js + f"\nvar cases = {cases};\n" + """
for (var i = 0; i < cases.length; i++) {
    var host = cases[i][0], want = cases[i][1], got = FindProxyForURL('https://' + host + '/', host);
    if (got !== want) { throw new Error(host + ' expected ' + want + ' got ' + got); }
}
"""
    process = subprocess.run(["node", "-e", program], capture_output=True, text=True, check=False)
    if process.returncode:
        raise RuntimeError(f"{label}: {process.stderr}")
    print(f"PASS PAC JS: {label} ({len(expected)} cases)")


run_case("default proxy with explicit overrides", "ProxyByDefault", ["example.cn", "shared.test"],
         ["example.com", "shared.test", "10.0.0.5"], 1088,
         [["www.example.cn", "DIRECT"], ["www.example.com", "PROXY 127.0.0.1:1088"],
          ["shared.test", "DIRECT"], ["unknown.org", "PROXY 127.0.0.1:1088"],
          ["10.0.0.5", "PROXY 127.0.0.1:1088"], ["localhost", "DIRECT"],
          ["127.0.0.1", "DIRECT"], ["server.local", "DIRECT"],
          ["192.168.4.2", "DIRECT"]])
run_case("default direct with forced proxy", "DirectByDefault", [], ["google.com"], 1088,
         [["google.com", "PROXY 127.0.0.1:1088"], ["mail.google.com", "PROXY 127.0.0.1:1088"],
          ["example.com", "DIRECT"], ["intranet", "DIRECT"]])
run_case("PAC root and all-level subdomain boundary", "DirectByDefault", [], ["google.com"], 1088,
         [["google.com", "PROXY 127.0.0.1:1088"],
          ["www.google.com", "PROXY 127.0.0.1:1088"],
          ["aaa.google.com", "PROXY 127.0.0.1:1088"],
          ["bbb.google.com", "PROXY 127.0.0.1:1088"],
          ["a.b.google.com", "PROXY 127.0.0.1:1088"],
          ["WWW.GOOGLE.COM", "PROXY 127.0.0.1:1088"],
          ["google.com.", "PROXY 127.0.0.1:1088"],
          ["notgoogle.com", "DIRECT"],
          ["google.com.evil.com", "DIRECT"],
          ["evilgoogle.com", "DIRECT"]])
run_case("PAC specific direct overrides proxy root and children", "DirectByDefault",
         ["login.google.com"], ["google.com"], 1088,
         [["google.com", "PROXY 127.0.0.1:1088"],
          ["www.google.com", "PROXY 127.0.0.1:1088"],
          ["login.google.com", "DIRECT"],
          ["sso.login.google.com", "DIRECT"]])
run_case("China-site template base list", "ChinaSitesDirect", ["baidu.com", "qq.com"],
         ["google.com"], 1088,
         [["map.baidu.com", "DIRECT"], ["qq.com", "DIRECT"],
          ["google.com", "PROXY 127.0.0.1:1088"], ["unknown.org", "PROXY 127.0.0.1:1088"]])

run_case("PAC explicit proxy overrides China offline template", "ChinaSitesDirect", [], ["baidu.com"], 1088,
         [["baidu.com", "PROXY 127.0.0.1:1088"], ["qq.com", "DIRECT"], ["unknown.net", "PROXY 127.0.0.1:1088"]])
