# VelaRay Windows 单文件发布（仅应用）

发布物：`publish/VelaRay-win-x64.exe`，仅包含 VelaRay .NET 10 / MAUI / Windows App SDK 应用及其运行依赖。

**明确不包含：** `v2ray.exe`、`geosite.dat`、`geoip.dat`、`geoip-only-cn-private.dat`。程序不会从 EXE 内自动提取 V2Fly 核心。连接和内核验证需要用户另行安装 V2Fly。

## 发布

在 Windows（.NET 10 SDK + MAUI Windows workload + Visual Studio C++ Build Tools）执行：

```powershell
.\scripts\verify-windows.ps1 -SkipPublish
.\scripts\publish-windows.ps1 -Mode SingleFile
```

发布脚本会检查输出目录是否仍存在运行时 sidecar。如果 MAUI 单文件发布无法满足环境限制，脚本将报告失败，不会假装发布成功。

## 便携目录

```text
D:\Apps\VelaRay\
  VelaRay.exe
  v2ray-core\v2ray.exe                   # 用户另外安装
  v2ray-core\geosite.dat                # 用户另外安装
  v2ray-core\geoip.dat                  # 用户另外安装
  v2ray-core\geoip-only-cn-private.dat  # 用户另外安装
  configs\
  settings\
  pacs\
  logs\
```

也可以在程序中指定一个外部 `v2ray.exe` 的绝对路径。内核配置验证和连接只能在该路径有效时使用。

MAUI 的单文件 EXE 在运行时仍可能将框架依赖解压到临时目录，属于微软官方的 WinUI 3 单文件发布模型；用户可分发的应用文件仍只有一个。普通 `dotnet build` 的输出不等于单文件发布。

参考： https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app
