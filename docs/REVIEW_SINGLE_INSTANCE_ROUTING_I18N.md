# VelaRay – 单实例、语义化多语言及高级路由 UX 审查

## 本次功能范围

- Windows 单实例：在 `MauiProgram.CreateMauiApp()` 创建 MAUI 容器前获取命名 Mutex。第二次运行发出命名事件，通知原应用显示窗口，第二个进程立即退出；不会创建第二套系统代理服务或 `v2ray.exe` 进程。进程异常终止后，Mutex 由 Windows 释放。
- 多语言：`Resources/I18n/zh-CN.json` 与 `en-US.json` 的 184 个旧数字键已改为有语义的标识符，新增路由编辑器文案也使用语义化键。语言实时切换。
- JSON 格式：删除 `ConfigVersionPicker.Title` 的重复文字，并移除仍残留在全局代理设置中的“配置格式”Label。只在连接标题右侧保留一个“JSON配置格式”Label；每个节点独立保存 V4/V5。
- 路由：原始 JSON 之外，提供规则列表、域名/GeoSite、IP/GeoIP/CIDR、端口、TCP/UDP、目标出站、DNS 策略、添加/修改/删除/上下移动。
- 规则集为全局设置，内核规则只控制已进入 v2ray-core 的请求；不混同于浏览器 PAC。规则按顺序匹配，第一条命中生效。

## 十轮源码审查

| # | 主题 | 修复与检查 | 验证方式 |
|---|---|---|---|
| 1 | 文案资源 | 替换编号键；检查中英文键集合一致 | `static_check.py`, `audit_ux_localization.py` |
| 2 | 语言切换 | 保留 Switch，切换时不重建路由输入表单 | UI 绑定/事件检查 |
| 3 | 重复 Label | 清除 JSON Picker Title，仅保留一个 Label | XAML 结构断言 |
| 4 | Windows 单实例 | Mutex 在 MAUI 服务创建前执行 | `review_semantic_singleinstance_routing.py` |
| 5 | 重新唤醒 | 第二进程通过命名事件要求原窗口显示 | 静态引用检查；Windows 交互测试待运行 |
| 6 | 路由 UI | 增加规则和策略编辑、操作按钮可换行 | XAML 解析、事件绑定检查 |
| 7 | V4/V5 分离 | V4 `routing.rules` 与 V5 `router.rule` 各自序列化 | C# SmokeTests 用例已添加，待 Windows 执行 |
| 8 | 路由安全 | 无效出站不默认映射 `proxy`；balancer 规则需用高级 JSON 编辑 | 代码检查/C# SmokeTests 用例 |
| 9 | Geo/IP/排序 | 校验端口范围、CIDR，保留未知高级字段 | C# SmokeTests 用例已添加 |
| 10 | 集成与发布 | 无旧设置导入、无内嵌 v2ray.exe、Windows 编译和单 EXE 验证入口存在 | 静态审查、脚本、CI |

## 实际执行和验收边界

本环境已执行并通过：XML/XAML/JSON 解析；全部具名事件处理器关联；多语言键完整性；PAC JavaScript 行为测试；C# 词法结构检查；新增 10 项重点源码检查。**这些不是 C# 或 Windows MAUI 编译。**

**尚未执行：** `dotnet build`、C# SmokeTests、Windows 原生 UI/Job Object 行为测试、实际 v2ray-core `test` 命令以及单文件 EXE 发布/双启动测试。实际交付前应在 Windows 11 + .NET 10 MAUI SDK 环境执行：

```powershell
.\scripts\verify-windows.ps1 -SkipPublish
.\scripts\publish-windows.ps1 -Mode SingleFile
.\scripts\test-single-instance.ps1
```

对路由分别以 V4 和 V5 节点生成配置并验证：

```powershell
.\v2ray-core\v2ray.exe test -format json -c .\configs\config.v4.json
.\v2ray-core\v2ray.exe test -format jsonv5 -c .\configs\config.v5.json
```

### 单实例限制

同一 Windows 登录会话内只允许一个主程序实例。该约束不会杀死用户单独手动启动的 `v2ray.exe`。强制结束 VelaRay 时，原有 Windows Job Object 会负责结束由 VelaRay 管理的子进程，但异常退出时系统代理还原需要在下一次启动时复原（Windows 强制结束进程时无法保证执行托管清理代码）。

### 路由原始 JSON 边界

高级表单面向常见规则，未知 V5 特性/负载均衡目标保留在原始 JSON 编辑器中。编辑时严格拒绝不能安全表达的目标或条件；不会静默丢弃规则字段。运行中的连接只会在重新连接后加载新的内核路由配置。
