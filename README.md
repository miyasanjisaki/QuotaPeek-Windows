> 本项目基于 macOS 版 [majianyu2007/QuotaPeek](https://github.com/majianyu2007/QuotaPeek) 开发，是它的 Windows 移植版。

# QuotaPeek for Windows

QuotaPeek 的 Windows 托盘版。它常驻任务栏通知区域，调用 VM2API / Claude 兼容服务的 `GET /v1/usage` 接口，显示 5 小时、7 天、Sonnet 7 天和 Opus 7 天额度窗口。使用 C# WinForms（.NET Framework 4.8）编写，无第三方依赖。

## 运行

`dist\` 是构建输出目录，不随仓库提交。克隆仓库后，先按下方“从源码构建”生成 `dist\QuotaPeek.exe`，再双击运行。首次启动会自动打开设置：

1. 填写槽位名称、API 地址和 API Key。
2. 点击“验证并保存”。只有实际请求成功后才会保存。
3. 之后左键点击任务栏通知区域中的数字图标，可打开或收起额度面板；右键可切换槽位、刷新、打开设置或退出。

API 地址可以是服务根地址、以 `/v1` 结尾的地址，或完整的 `/v1/usage`。远程服务只允许 HTTPS；`localhost`、`127.0.0.1` 和 `::1` 可使用 HTTP。

> 这个工具读取的是兼容的 `/v1/usage` 接口，不会从网页、浏览器 Cookie 或本机聊天记录推算额度。若你的服务不提供该接口，仅填写普通 OpenAI/Anthropic API 地址无法得到额度数据。

额度面板是一个贴近任务栏的浮动卡片：顶部标签可在“全部槽位”和单个槽位之间切换，右上角可刷新（F5）、打开设置或收起；点击面板外部或按 Esc 会自动收起。外观会跟随“通用 → 外观”中的浅色/深色设置。

程序已在运行时再次双击 `QuotaPeek.exe`（或其快捷方式），不会启动第二份，而是直接弹出额度面板。

关闭额度面板或设置窗口后，程序仍在托盘运行；完全退出请使用托盘右键菜单中的“退出 QuotaPeek”。若看不到托盘图标，请展开 Windows 通知区域的隐藏图标。

## 与 macOS 版的对应关系

- macOS 菜单栏 → Windows 任务栏通知区域图标
- macOS 钥匙串 → Windows 凭据管理器
- macOS 通知 → Windows 托盘提醒
- macOS 登录项 → 当前用户注册表启动项
- 配置文件 → 程序旁的 `data\preferences.json`（即 `dist\data\preferences.json`）

配置文件不包含 API Key。Key 以 `QuotaPeek/<连接 ID>` 的目标名保存在 Windows 凭据管理器中。网络请求拒绝重定向，成功响应最多读取 1 MiB，并只展示经过筛选的服务端错误码。

程序和普通配置保存在程序所在目录，不另写入 C 盘的应用数据目录；凭据由 Windows 管理。请把整个 `dist` 文件夹放在可写位置，并保留其中的 `data` 文件夹。跨电脑复制时，需要在新电脑重新填写 Key。登录启动默认关闭；开启后若移动程序，需要重新保存该设置。

## 从源码构建

需要 Windows 10/11、.NET Framework 4.8，以及 Visual Studio 2022 的 C# 编译器。在仓库根目录使用 PowerShell 7：

```powershell
pwsh -File .\build.ps1 -Test
```

构建结果为 `dist\QuotaPeek.exe`；`-Test` 还会运行额度解析、地址规范化和安全边界的核心测试。

可用 `pwsh -File .\build.ps1 -Test -IntegrationTest` 运行本机集成验证。测试只监听 `127.0.0.1`，使用虚构 Key 和独立配置目录；临时凭据及配置会清理，窗体截图保存在 `dist\validation-*`。验证涵盖真实 HTTP 路径与认证头、重定向拒绝、响应大小、超时/取消、凭据存取、保存失败回滚及主要窗口交互；不代表已通过真实服务账号验证。

## 致谢

协议解析、安全约束和交互设计均来自 [majianyu2007/QuotaPeek](https://github.com/majianyu2007/QuotaPeek)，感谢原作者。
