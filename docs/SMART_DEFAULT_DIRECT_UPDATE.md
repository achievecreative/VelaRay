# 智能分流：默认直连（VelaRay 2.5 修订）

## 内核路由（仅在 RoutingMode = Smart 时生效）

1. PAC 托管配置中明确标注为 **代理** 的域名/IP → `proxy`；如果同时启用全部节点负载均衡，则这部分请求使用 `balance`。
2. 私有与本地目的地址（`geoip:private`, `localhost`, `.local`）→ `direct`。
3. 中国大陆域名（`geosite:cn`）与目的 IP（`geoip:cn`）→ `direct`。
4. 其余全部 → `direct`。

当用户还配置了 PAC 显式直连例外时，该例外优先于 PAC 强制代理列表。没有显式代理项时，智能分流模式下没有任何流量需要代理出站。

## 实现细节

V2Fly V4 和 V5 的文档都规定：未命中任何路由时默认使用 `outbounds` 中的**第一个**出站。VelaRay 在 Smart 模式把 `direct`（Freedom）放在第一个出站，其次才是节点 `proxy`；不添加兜底全匹配规则。这允许保留 V4 的 `IPIfNonMatch` 和 V5 的 `IpIfNonMatch`，在未命中域名规则时继续检查 GeoIP。

其他内置模式 (`ProxyAll`, `Direct`, `BypassCN`) 与自定义模式的出站顺序及策略不变。在 Smart + BalanceAllNodes 模式下，只将明确匹配 PAC 代理的网站规则指向 balancer；不再有 `所有流量 -> balancer` 的末尾规则。

## PAC 与内核路由的边界

新安装的 `PacTemplate` 默认 `DirectByDefault`，只有明确指定代理的 PAC 域名才会通过系统 PAC 进入本地 V2Ray HTTP 代理。已经保存的用户 PAC 模板不会被覆盖；用户也可以主动选择 `ProxyByDefault`。后者可能把所有符合 PAC 的浏览器请求发送到本地 V2Ray，但在 Smart 模式下，未命中代理例外的请求仍由 Freedom 出站直连，不会发送到远程服务器。

任意用户手写 PAC JavaScript **不会**被自动解析成 V2Ray 核心路由条件，只有托管 PAC 编辑器中的 `PacProxyWebsites` 和 `PacDirectWebsites` 会同步到 Smart 路由。修改规则后需重新连接以加载更新的 `config.active.v4/v5.json`。

## 检查

执行了 `scripts/check_smart_routing_contract.py`、`scripts/ten_round_audit.py`、`scripts/check_pac_js.py` 等源码静态检查和 Node.js PAC JavaScript 测试，通过。新增 C# SmokeTests 检查第一出站、默认直连、PAC 代理例外、空例外、负载均衡与其他模式隔离。

**尚未实际运行 .NET 10 MAUI 编译、C# SmokeTests 或 V2Fly 连接测试**：当前环境没有 dotnet SDK。请在 Windows 上执行 `dotnet run --project tests/VelaRay.SmokeTests`、`dotnet build VelaRay.slnx -c Debug`，然后使用真实 V2Fly 内核分别执行 `v2ray.exe test -format json -c ...` 与 `v2ray.exe test -format jsonv5 -c ...`。

参考：
- https://www.v2fly.org/config/routing.html
- https://www.v2fly.org/v5/config/router.html
