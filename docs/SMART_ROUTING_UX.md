# VelaRay 路由 UX 与智能分流预设

## 页面调整

- **顶部：路由模式**。可选智能分流（推荐）、全部代理、全部直连、绕过中国大陆、自定义。切换后左侧导航不变；这些是对全部节点生效的全局设置。
- **内置模式：只读规则预览**，展示实际优先级和兜底路径，不显示空白规则列表。`ProxyAll` 无显式路由规则时使用首个出站。
- **自定义模式：规则集 → 规则列表 → 规则详情**。规则集名称与路由策略位于顶部，域名解析策略选择器固定宽度 188 DIP。规则列表按从上到下优先级排列；新增进入草稿，不会保存无条件匹配的规则。操作包括应用、上移、下移、删除和原始 JSON 编辑。
- **字段帮助**：域名/GeoSite 使用主机名或 `geosite.dat` 分类；IP/GeoIP 使用目的地址、CIDR 或 GeoIP 数据库分类。要表达「域名 **或** IP 匹配」，请创建两条规则，因为同一规则的不同字段在 V2Fly 中是组合条件。

## 新建设置的默认策略

`ClientSettings.RoutingMode = "Smart"`。按用户明确要求，**不进行旧设置数据迁移**；此前已保存的 RoutingMode 会保持原样。

智能路由优先级（默认直连）：

1. `PacProxyWebsites`：PAC 中**明确指定代理**的域名及 IP → `proxy`。来源于托管 PAC 编辑器，不解析手写 PAC JavaScript。
2. 私有及本地目的地址：`geoip:private`、`localhost`、`.local` → `direct`。
3. 中国大陆网站 `geosite:cn` / 中国大陆目的 IP `geoip:cn` → `direct`。
4. 其余所有流量 → `direct`（**第一个出站为 direct**，不是过早匹配的无条件规则）。

**显式 PAC 直连例外**仍优先于代理规则；仅当配置了这些例外时，预览才额外展示该优先级。显式直连和代理发生冲突时，直连优先。

V4 的 `routing.domainStrategy` 继续使用 `IPIfNonMatch`，V5 的 `router.domainStrategy` 使用 `IpIfNonMatch`；未命中域名规则时仍能解析目标 IP，执行 GeoIP 判断。V2Fly 官方文档明确：未命中规则时使用 `outbounds[0]`，因此 Smart 模式下将 `direct` 插在 `outbounds[0]`，不需要添加可能阻断 GeoIP 的兜底路由规则。

**全部节点负载均衡**仅对 PAC 显式代理匹配项使用 balancer，不再额外添加 `全部流量 → balancer` 规则；其他请求仍匹配 CN/private 或走默认直连。`ProxyAll`、`Direct`、`BypassCN`、`Custom` 原有模式与选中规则不变。

### PAC 与核心路由差别

PAC 脚本决定系统浏览器/遵守 PAC 的程序是否把请求发给本地 HTTP 代理；V2Ray 内核路由只决定**已进入内核**的数据走 `direct` 或 `proxy`。手动模式或其他独立代理客户端不一定受系统 PAC 影响。

- 手写 `.pac`/`.js` 中的自定义匹配函数**不会被解析为 V2Ray 路由**。智能分流仅同步受控设置中的 `PacProxyWebsites` 和 `PacDirectWebsites`。
- 如修改了网站列表并已连接，必须重新连接，才能让已经运行的 `v2ray-core` 使用重新生成的路由配置；生成新的 PAC 文件只会更新 PAC 服务，不会热重载内核 JSON。
- 内置 GeoIP/GeoSite 必须存在于 `v2ray-core/` 并由内核成功加载。`geoip-only-cn-private.dat` 只能覆盖 `cn`、`private`，不能替代完整国家地区数据库。
- 如果同一域名在 PAC 显式直连和显式代理列表中都出现，**显式直连优先**；模板中的中国大陆网站直连可以被显式代理覆盖。
- 新建的设置默认使用 `DirectByDefault` PAC 模板（仅显式代理域名交给本地 HTTP 代理）；已保存在 `settings.v5.json` 的 PAC 模板不会被迁移或覆盖。用户主动选 `ProxyByDefault` 时，Windows 浏览器仍会把多数网站交给本地 V2Ray，但 Smart 内核路由的未命中出口依然是 `direct`。
- 修改 PAC 网站清单后，重连会更新核心路由；运行中的旧配置不会自动热重载。

## V2Fly 参考

- V4 Routing: https://www.v2fly.org/config/routing.html
- V5 Router: https://www.v2fly.org/v5/config/router.html
- V5 Format: https://www.v2fly.org/v5/config/overview.html

## 验收与限制

执行 `python scripts/static_check.py`、`python scripts/audit_ux_localization.py`、`python scripts/check_pac_js.py`、`python scripts/review_semantic_singleinstance_routing.py` 以及 Windows 上的 `dotnet run --project tests/VelaRay.SmokeTests`。Windows MAUI 编译和真实 V2Fly 验证必须在 Windows 上使用官方内核执行，静态检查**不能**替代编译。测试时覆盖默认智能分流、显式域名覆盖、中国大陆域名/IP、PAC 直连例外、负载均衡、V4/V5 和多语言界面切换。
