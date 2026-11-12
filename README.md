# VelaRay

A Windows-only .NET 10 / .NET MAUI client for the official **V2Fly v2ray-core**. It supports modern VMess, Trojan and Hysteria2 profiles, with V2Fly **JSON V4 (`json`)** and **JSON V5 (`jsonv5`)** outputs. No Xray or sing-box dependency is required.


## Smart 代理回退与 PAC 子域名匹配（2.7）

Smart 默认保持未匹配直连；可在代理设置或路由与 PAC 中选择“未匹配 → 代理”。自动生成的 `managed.pac` 会同步默认行为，手写 PAC 文件保持原样。PAC 列表的 `google.com` 自动包含 `www.google.com` 和任意子域名，但不会误命中 `notgoogle.com`。详见 [Smart/PAC 分流说明](docs/SMART_PROXY_FALLBACK_AND_PAC_SUBDOMAINS.md)。

## VelaRay 2.7 — 服务器卡片间距和滚动条留白修复

服务器列表通过 `CollectionView.ItemsLayout` 的 `LinearItemsLayout.ItemSpacing=12` 控制卡片间距（不再依赖 `Border.Margin`）；数据模板使用独立的 18dp 右侧空白列，为 Windows 覆盖式滚动条及约 10dp 视觉间距预留空间。卡片仍保持 78dp 最小高度；选中高亮和实际运行节点状态仍彼此独立。详见 [布局修复说明](docs/SERVER_COLLECTIONVIEW_GAPS_FIX_2_7.md)。

## Features

- A node-specific editor and separate global proxy, routing and PAC workspaces.
- One context-aware **Connect / Switch / Disconnect** action, mirrored in the top bar and footer. Profile selection alone never changes the running connection.
- Official V2Fly **manual-only v2ray-core version checking and installation** from Proxy settings: stable by default, opt-in prereleases, SHA256 verification, executable version check, rollback backup, no forced disconnect.
- Windows **Global**, **PAC**, and **Manual** system-proxy modes; backs up and restores WinINET proxy settings.
- VMess `alterId` (default 0) and V4 `allowInsecure` opt-in; V5 rejects incompatible settings explicitly.
- Bilingual Chinese/English UI with immediate language switching.
- V4/V5 config viewing, generation, external custom JSON selection, validation and execution.
- Local PAC templates with custom direct/proxy domain lists; GeoSite and GeoIP selection.
- Windows Job Object-based `v2ray.exe` child-process lifetime management.
- One-file frontend publishing (the V2Fly core and Geo data remain separately managed).
- Single-instance Windows startup: launching VelaRay again activates the existing window, not a second proxy client.
- Visual advanced routing editor: add/edit/remove/reorder V4/V5 rules by domain/GeoSite, IP/GeoIP, ports, network and outbound. Raw JSON remains available for unsupported fields.

**Removed:** original WPF project resources, legacy `settings.json` importing/merging, migration-only services and UI. Node share-link export and external V4/V5 custom JSON selection remain available. Subscription URL management remains removed; **official v2ray-core updates have been restored**. Existing VelaRay settings continue to load from `settings/settings.v5.json`; there is no automatic scanning for other clients' settings.

## Build / test (Windows)

```powershell
dotnet workload install maui-windows
.\scripts\verify-windows.ps1 -SkipPublish
.\scripts\publish-windows.ps1 -Mode SingleFile
```

Direct commands:

```powershell
dotnet run --project tests/VelaRay.SmokeTests -c Release
dotnet build src/VelaRay.Maui/VelaRay.Maui.csproj -c Release -f net10.0-windows10.0.19041.0
```

## Portable storage

All VelaRay-managed files are stored next to the physical `VelaRay.exe` in `settings/`, `configs/`, `pacs/` and `logs/`. Place the EXE in a **user-writable** location. The external core should be placed under `v2ray-core/v2ray.exe`, with applicable `geosite.dat`, `geoip.dat` and/or `geoip-only-cn-private.dat` files.

**Publishing:** `publish/VelaRay-win-x64.exe` is intended to be a standalone MAUI frontend; `v2ray.exe` and Geo data are not bundled. Actual Windows publish and runtime compatibility must be verified on Windows.

## Verification note

The Python static checks validate source structure, XAML/XML/JSON resources and integration references. They **cannot** catch all C# compiler errors. A passing Windows MAUI Debug/Release build, core config validation (`v2ray.exe test -format json[v5] ...`) and UI/runtime testing are required for release acceptance.

## Node-specific JSON formats and fixed language control

Each server now chooses its own V2Fly V4 or V5 config format in the **Servers & transport** editor. Global proxy and PAC options remain shared. Language is switched directly with the explicit two-choice **中文 / EN** control beside the current server, without restarting. Both translation JSON files now use semantic keys such as `FieldServerAddress` and `ActionValidateConfig`. Details and Windows acceptance criteria: `docs/FORMAT_PER_NODE_AND_LANGUAGE.md`.

## Visual routing / one-instance verification

The **Routing & PAC → Advanced routing rules** section now includes a structured editor. Rules are evaluated top to bottom. The route set is global; each selected server still controls its own V4/V5 loader. The editor saves changes to the portable settings file and preserves unrelated advanced JSON keys. Unsupported balancer rules can be edited in **Show advanced JSON** mode. A custom rule set initially uses V4 routing JSON so both config builders can consume it.

For a real Windows build and local process test:

```powershell
dotnet run --project tests/VelaRay.SmokeTests -c Release
.\scripts\verify-windows.ps1 -SkipPublish
.\scripts\publish-windows.ps1 -Mode SingleFile
.\scripts\test-single-instance.ps1
```

The last test requires an **interactive Windows session**. It starts two copies of the published app, checks that only one stays running, and closes its test-owned first process afterward. Visual confirmation that the first window is restored from the tray is manual. No `v2ray.exe` is embedded in the single-file VelaRay frontend.

## VelaRay 2.5 — 路由与 PAC 优化

路由页面现在使用 **规则集 → 规则顺序 → 规则详情** 工作流；内置策略只读预览，自定义规则可编辑。新建设置默认 **智能分流（推荐）**：PAC 显式直连例外、PAC 显式强制代理、私有/本地地址直连、中国大陆网站和 IP 直连，**其余全部直连**。新建设置的 PAC 模板也改为默认直连，不强制修改已有设置。V4 与 V5 分别生成对应的原生路由结构。

详细设计、优先级、两层 PAC/内核路由区别及验收注意事项见 `docs/SMART_ROUTING_UX.md`。此版本**没有旧设置迁移功能**。

## VelaRay 2.6 — 连接状态与编辑分离

左侧节点列表仅用于**选择和编辑**，不会自动启动、切换或重连代理。顶部显示实际运行的连接名称及状态；绿色标识只标记已加载到当前内核配置的节点。运行中的节点（包括负载均衡涉及的节点）不可修改或删除，其他节点可随时选择并编辑；手动点击「连接」才会切换连接。代理设置和路由/PAC 仍是全局设置。

2.6 当时移除了订阅 URL 和内核维护模块；**本次改动已恢复官方 V2Fly 内核的版本检查与更新，但订阅 URL 管理仍不恢复**。「路由与 PAC」中的 Geo 数据检查/更新继续保留。Hysteria2 的 V5 出站结构参照 V2Fly 官方示例，并避免将 `http/1.1` 等 TCP ALPN 覆盖带入 QUIC。参见 `docs/CONNECTION_SELECTION_AND_HY2_V5.md`。

## VelaRay 2.7 — 合并连接按钮与恢复内核更新

- 顶部工具栏和底部状态区共用同一个操作：**未连接 → 连接；选中当前运行节点 → 断开；选中其他节点 → 切换**。负载均衡中的运行节点也可正确识别。更换列表选中项不会自动连接、断开或切换。
- 在**代理设置 → V2Ray 内核更新**查看本地与最新版本。不会在启动或切换更新通道时自动检查；只有手动点击「检查内核更新」才会检查本地及官方版本。默认采用稳定版，预发布版本需要显式开启。
- 仅更新应用目录 `v2ray-core/v2ray.exe`；外部指定的自定义内核路径只检查版本、不修改文件。运行中的内核必须由用户先断开；更新仅替换可执行文件，其他 Geo/配置文件保留。
- 从 [V2Fly 官方 Releases](https://github.com/v2fly/v2ray-core/releases) 获取发行资产，验证 SHA256 和下载文件版本，原内核保留为 `v2ray.exe.backup-...`。失败时尽可能恢复旧文件。

详见 [`docs/CORE_UPDATES_AND_CONNECTION_ACTION.md`](docs/CORE_UPDATES_AND_CONNECTION_ACTION.md)。

## Workspace UX and future desktop platforms

The sidebar places **Proxy settings** and **Routing & PAC** (global) above the server list, and **Server settings** (per selected node) at the bottom. Selecting a node never changes the active connection. The blue selected row is distinct from the green running marker; the top toolbar reports the real running connection. Action buttons wrap on narrower windows.

VelaRay remains **Windows-only**. macOS can be targeted later through MAUI Mac Catalyst, but needs native adapters; Linux MAUI desktop is not officially supported. Reusable settings, PAC, config builders, cross-platform storage/core path helpers and platform-service contracts are in `VelaRay.Core`. See [`docs/WORKSPACE_PLATFORM_UX_REVIEW.md`](docs/WORKSPACE_PLATFORM_UX_REVIEW.md).

### 2.7 visual refresh and live PAC/core state

The reference-image language pill and server cards, explicit on-demand TCP latency test,
and pending routing configuration banner are documented in
[`docs/REFERENCE_CARD_UI_AND_STAGED_PAC_2_7.md`](docs/REFERENCE_CARD_UI_AND_STAGED_PAC_2_7.md).
The selector does not change the running connection; Smart PAC domains are staged
with the core and require an explicit reconnect, while independently edited custom
PAC files may hot reload.


## UX update: PAC / Global / Manual mode cards

VelaRay 2.7 uses three visual system-proxy mode cards and six compact UX improvements without changing proxy behavior or settings storage. See [`docs/SIX_UX_PROXY_MODES_2_7.md`](docs/SIX_UX_PROXY_MODES_2_7.md).
