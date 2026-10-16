# 于の电脑 状态追踪器

Windows 托盘常驻小工具：开关机状态 Telegram 通知、Clash/代理状态展示、双通道（官方 API + Cloudflare Worker）发送。

## 功能

- 系统托盘常驻，静默启动；左键打开状态窗口，右键菜单
- 开机自动发送提醒（系统启动 3 分钟内判定为开机）
- 关机拦截弹窗，提醒手机微信确认登录（确定放行 / X 取消关机）
- 手动发送开机 / 关机提醒（发送前二次确认）
- 双通道发送：官方 `api.telegram.org` → 失败自动降级 Cloudflare Worker
- 状态卡片：Clash / VPN 延迟 / Telegram / Google、Bot 发送记录、通知通道健康
- Bot Token 与 CF SECRET 使用 Windows DPAPI 加密落盘
- 主题：跟随系统 / 黑暗 / 明亮
- 开机自启动（注册表 `HKCU\...\Run`）
- 检查更新（打开本仓库）

## 运行要求

- Windows 10 1809+ / Windows 11
- 可选：Clash（用于官方通道与延迟检测）

## 从源码构建

```powershell
# 推荐（会补齐缺失的 PROGRAMFILES 环境变量）
powershell -ExecutionPolicy Bypass -File scripts\build.ps1

# 或手动
dotnet restore OnlineTracker.sln
dotnet build src\StatusTracker\StatusTracker.csproj -c Release -p:Platform=x64
```

生成 MSIX：

```powershell
dotnet build src\StatusTracker\StatusTracker.csproj -c Release -p:Platform=x64 `
  -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never
```

产物路径：

```text
src\StatusTracker\bin\x64\Release\net9.0-windows10.0.19041.0\AppPackages\StatusTracker_1.0.0.0_x64_Test\StatusTracker_1.0.0.0_x64.msix
```

双击安装（需开启旁加载或使用签名包）。

## 配置

首次运行会生成 `%AppData%\PcStatusNotifier\config.json`：

| 字段 | 说明 |
|---|---|
| botTokenEnc | Telegram Bot Token（DPAPI 加密） |
| chatId | 频道 / 群组 ID |
| cfBaseUrlEnc | CF 转发器地址（含 SECRET，DPAPI 加密） |
| channelMode | Auto / OfficialOnly / CfOnly |
| themeMode | System / Dark / Light |
| proxyPort | Clash HTTP 代理端口，默认 7890 |

日志：`%LocalAppData%\PcStatusNotifier\logs\app-yyyyMMdd.log`

## Cloudflare Worker 部署（约 2 分钟）

国内网络走官方 API 可能失败，可用免费 Worker 反代兜底。

1. 登录 [dash.cloudflare.com](https://dash.cloudflare.com/) → **计算 / Workers** → **创建**
2. 选「从 Hello World! 开始」→ 进入编辑器
3. 把 [`tools/telegram-forwarder-worker.js`](tools/telegram-forwarder-worker.js) 全文粘贴进去
4. 把 `SECRET = "..."` 改成 32 位以上随机字符串（勿泄露）
5. **部署**，记下地址，拼成（**末尾斜杠必须有**）：

```text
https://<worker名>.<子域>.workers.dev/<SECRET>/
```

6. 填入软件「配置 → CF 转发器地址」→ 保存

验证：

```text
https://<worker>.workers.dev/<SECRET>/bot<TOKEN>/getMe
```

返回 `"ok": true` 即成功。

> 不要启用 Cloudflare Access，否则程序无法调用。

## 通道说明

| 模式 | 行为 |
|---|---|
| 自动（默认） | 官方 15s×2 → 失败立即 CF 15s×2 |
| 仅官方直连 | 只走 `api.telegram.org`（走本地代理） |
| 仅CF转发 | 只走 Worker（不依赖梯子） |

- 开机通知：等 Clash 最多 60 秒，未就绪则直接 CF
- 关机通知：官方 10 秒短超时，失败秒转 CF，不阻塞关机

## 消息文案

- 开机：`🖥️ YU-PC 开机了，准备登微信`
- 关机：`💤 YU-PC 关机了，AI Agent 可以申请登微信了`

## 安全

- Token / SECRET 只以 DPAPI 密文落盘，日志仅掩码
- 请勿把 Token 发到聊天、Issue 或提交进仓库
- 建议定期 `/revoke` 换新 Token

## 目录

```text
src/StatusTracker/     WinUI 3 应用
picture/               图标源 SVG
tools/                 Cloudflare Worker
docs/PLAN.md           开发计划
scripts/build.ps1      构建脚本
```

## 版本

Version 1.0.0 · © 2024-2026 YUNO BASE
