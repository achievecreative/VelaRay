# VelaRay 2.7 — Smart 默认路由与 PAC 子域名匹配

## 使用方法

1. 在 **代理设置** 或 **路由与 PAC** 中选择 **Smart 智能分流**。
2. 设置 **Smart 未匹配流量**：
   - **未匹配 → 直连**（默认，兼容旧配置）
   - **未匹配 → 代理**（新选项，主动开启）
3. 系统代理选 **PAC** 且使用 `managed.pac`，应用会在连接时自动协调 PAC 默认出站，确保未匹配流量可以进入 V2Ray 内核。
4. 点击连接/切换按钮重新启动代理以应用修改；仅保存选项不强制切换正在运行的连接。

配置字段：`ClientSettings.SmartUnmatchedRoute`，取值 `Direct` 或 `Proxy`；旧 JSON 缺少此字段时反序列化默认 `Direct`。

## 路由优先级

仅在 `RoutingMode == Smart` 时按以下顺序生效：

1. 用户的 **PAC 明确直连网站** → direct（最高优先级）
2. 用户的 **PAC 明确代理网站** → proxy / balance（当负载均衡开启）
3. 私有网络、本地地址 → direct
4. 中国大陆域名（GeoSite）和 IP（GeoIP）→ direct
5. 其余未匹配流量 → `SmartUnmatchedRoute` 指定的 direct 或 proxy

V4/V5 分别保留 `IPIfNonMatch` / `IpIfNonMatch`。V2Fly 采用**第一个 outbound**作为路由规则未命中时的出站，因此新选项为 `Proxy` 时改变 outbound 顺序，而**不追加无条件的全匹配规则**，防止 GeoIP 规则被抢先命中。

**负载均衡注意**：匹配显式 PAC 代理列表的流量仍可走 balancer；未匹配且回退代理的流量会走第一个节点，而非随机均衡到所有节点。这是为了保留 GeoIP 规则的正确优先级。现有 `Direct` 策略的行为不变。

## PAC 同步

系统代理模式与 V2Ray 内核路由是两个独立层级：

- 使用 `Global`：遵循系统代理的应用都进入 V2Ray，然后按 Smart 策略分流。
- 使用 `Manual`：不设置系统代理，仅主动连接到本地 HTTP / SOCKS5 的应用进入 V2Ray。
- 使用 `PAC` 和自动生成的 `managed.pac`：启用 Smart Proxy 回退时，若用户选择了 `DirectByDefault` 模板，实际生成时自动采用 `ProxyByDefault`，使未匹配的网站也进入本地代理；同时保留用户显式 PAC 直连和代理列表的优先级。界面显示专门的提示。
- `ChinaSitesDirect` 模板继续保留其内置的国内网站直连名单，再由 V2Ray GeoSite/GeoIP 执行完整的 CN 分流。该模板仅含有限的离线示例，不等同于完整的中国网站列表。
- **手写的自定义 PAC 文件**不会自动重写或解析。它如返回 `DIRECT`，请求仍然绕过 V2Ray，因此无法由新 Smart 默认策略接管。这是有意保留的兼容性边界。

保留在设置 JSON 中的 `PacTemplate` 不会被覆写。例如，用户历史上选择 `DirectByDefault`，后来把 Smart fallback 设置为 `Proxy` 时，只在 `managed.pac` **生成阶段**有效模板变成 `ProxyByDefault`；回退为 `Direct` 后自动回到用户原先的模板选项。

在代理运行期间修改 Smart 策略不会自动断开、切换或重启。重新连接时生成新的运行时配置，重新生成 `managed.pac` 并设置 Windows PAC URL。

## 根域名和子域名

在 PAC **直连网站** 或 **代理网站** 中填写：

```text
google.com
```

等价于匹配：`google.com`、`www.google.com`、`aaa.google.com`、`bbb.google.com`、`a.b.google.com` 等所有层级的子域名，无需重复写 `*.google.com`。显式的 `*.google.com` 也被正规化为 `google.com`。

不会误命中：`notgoogle.com`、`google.com.evil.com`、`evilgoogle.com`。匹配大小写不敏感，能处理末尾 `.`。PAC 采用 `host === rule || host.endsWith('.' + rule)` 的域名边界约束，V4 与 V5 对应使用 `domain:` 和 `RootDomain` 匹配规则。

例如，PAC **代理网站**中写 `google.com`、**直连网站**中写 `login.google.com`，则 `www.google.com` 走代理，而 `login.google.com`、`sso.login.google.com` 按更高优先级直连。

## 测试

当前环境无 .NET 10 Windows MAUI SDK，因此已运行的是 Python 代码约束检查及 Node.js PAC JavaScript 行为测试；C# SmokeTests 测试源代码已经添加，需在 Windows CI/机器上执行：

```powershell
.\scripts\verify-windows.ps1 -SkipPublish
```

建议的人工测试：使用真实 V2Fly V4/V5 配置各连接一次，分别验证 `www.google.com`、`stitch.withgoogle.com`、中国大陆网站、显式 PAC 直连例外，检查断开及重连前后是否符合预期。使用自定义 PAC 文件时，预期不会发生自动同步。
