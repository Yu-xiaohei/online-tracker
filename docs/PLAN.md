# 于の电脑 状态追踪器 — 开发计划备份

> 版本：计划 v1.0（2026-10-16）  
> 状态：**已与用户确认，待开工**  
> 仓库：<https://github.com/Yu-xiaohei/online-tracker>

---

## 1. 产品信息

| 项 | 值 |
|---|---|
| 应用名称 | 于の电脑 状态追踪器 |
| 目标平台 | Windows 10/11 |
| 交付形态 | **MSIX 安装包**（GitHub Releases） |
| 开发语言 | C# / .NET 9 |
| UI 框架 | WinUI 3（Windows App SDK 1.6）+ Fluent Design |
| 架构 | MVVM（CommunityToolkit.Mvvm） |
| 托盘 | H.NotifyIcon（WinUI）+ MenuFlyout |

---

## 2. 技术选型

| 维度 | 选择 | 说明 |
|---|---|---|
| UI | WinUI 3 + ThemeResource | 原生 Fluent，自动适配明/暗，圆角 8/4，Segoe UI Variable |
| MVVM | CommunityToolkit.Mvvm | 源生成器 RelayCommand / ObservableProperty |
| 托盘 | H.NotifyIcon.WinUI | 左键开窗、右键 MenuFlyout |
| 网络 | HttpClient + WebProxy | 官方通道走 `http://127.0.0.1:7890` |
| 配置 | System.Text.Json + **DPAPI** | Bot Token / CF SECRET 加密落盘 |
| 日志 | 自研 Logger | `%LocalAppData%\PcStatusNotifier\logs\app-yyyyMMdd.log` |
| 系统通知 | AppNotification / Toast | 开机发送成败横幅 |
| 打包 | 单项目 MSIX | `WindowsPackageType=MSIX` |

---

## 3. 功能清单

### 3.1 托盘

- 静默启动，不弹主窗口，只显示托盘图标
- 左键单击 → 打开状态窗口
- 右键菜单：打开应用窗口 / 开机自启动 / 关机前登录提醒 / 退出
- 图标双态：
  - `tray-normal`：四项检测**全部通过**（Clash 运行中 + VPN 连接正常 + Telegram 可用 + Google 可用）
  - `tray-error`：任一项异常（**含延迟较高**）
- 「关闭窗口后在托盘运行」默认 **开**；关闭后点 X 直接退出程序

### 3.2 状态窗口（5 卡片）

| 卡片 | 内容 |
|---|---|
| 网络与服务状态 | Clash：`运行中 / 未运行`；VPN：`连接正常 / 延迟较高 / 连接异常`；Telegram：`可用 / 不可用`；Google：`可用 / 不可用` |
| Bot 消息发送状态 | 上次开机提醒 / 上次关机提醒：成败图标 + 时间戳 |
| 通知通道（新增） | 当前模式；官方通道状态；CF 通道状态；上次通知（写明实际通道） |
| 手动发送状态（Expander） | 开机提醒 → 发送消息；关机提醒 → 发送消息 |
| 配置（Expander） | 见下表 |
| 检查更新 | 浏览器打开 GitHub 仓库 |

底部：`Version: 1.0.0 v` / `© 2024-2026 YUNO BASE All rights reserved.`

### 3.3 配置项

| 配置项 | 控件 | 默认 | 说明 |
|---|---|---|---|
| Chat ID | TextBox | — | |
| Bot Token | TextBox + 眼睛 | — | **DPAPI 加密落盘**，眼睛可看明文，日志只写掩码 |
| Clash 连接端口 | TextBox | 7890 | |
| CF 转发器地址 | TextBox | 空 | `https://xxx.workers.dev/<SECRET>/`，自动补末尾 `/` |
| 通道模式 | ComboBox | 自动 | 自动 / 仅官方直连 / 仅CF转发 |
| 主题 | ComboBox | 跟随系统 | 跟随系统 / 黑暗主题 / 明亮主题 |
| 开机自启动 | ToggleSwitch | 读注册表 | HKCU\...\Run |
| 关闭窗口后在托盘运行 | ToggleSwitch | 开 | |
| 关机前登录提醒 | ToggleSwitch | 开 | 副标题：关机前向您发送 微信登录 的提醒弹窗 |
| — | 保存 / 重置 | | 重置只清配置区，不清日志 |

配置变更**即时生效**，无需重启。  
配置文件：`%AppData%\PcStatusNotifier\config.json`

### 3.4 状态指示图标（原生 Segoe Fluent Icons + SystemFillColor）

| 图标 | 系统色 | 用于 |
|---|---|---|
| 绿勾 | Success | 运行中 / 连接正常 / 可用 / 发送成功 |
| 黄叹号 | Caution | 延迟较高 / 发送失败 |
| 红/异常 | Critical | 未运行 / 连接异常 / 不可用 |

禁止自绘圆点，禁止硬编码色值。

---

## 4. 通知通道（方案 v2）

### 4.1 通道

| 层级 | 通道 | 基地址 | 前提 |
|---|---|---|---|
| 主 | 官方 Telegram Bot API | `https://api.telegram.org/` | Clash/代理可用 |
| 次级 | Cloudflare Worker 反代 | `https://xxx.workers.dev/<SECRET>/` | 无需梯子 |

### 4.2 发送逻辑 `SendTelegram(method, params)`

```
通道模式
├─ 自动（默认）：官方（15s × 2）→ 失败立即 CF（15s × 2）→ 全败报错 + 托盘异常
├─ 仅官方直连：只走官方
└─ 仅CF转发：只走 CF
```

| 场景 | 策略 |
|---|---|
| 开机通知 | 等 Clash 每 15s 一次，**上限 60s** → 未就绪或官方失败 → 立即 CF |
| 关机/重启 | 官方 **10s** 短超时 → 秒级转 CF → 再失败只记录，不阻塞关机 |
| 手动按钮 | 与自动同一套降级 |

- CF 地址为空 → 次级显示「未配置」，自动模式退化为仅官方
- 消息体：开机 `🖥️ YU-PC 开机了，准备登微信`；关机 `💤 YU-PC 关机了，AI Agent 可以申请登微信了`
- 发送成败均弹 **Windows 系统通知**

### 4.3 与旧规则的覆盖

| 旧 | 新 |
|---|---|
| 发送前必须先确认代理 | Clash/代理检测**只做展示**，不阻塞发送 |
| 开机 15s × 20（5 分钟） | Clash 等待上限 **60s**，到点走 CF |
| 关机 5s 不重试 | 官方 10s → CF → 放弃记录 |
| 单通道 | 双通道三模式 |

---

## 5. 弹窗文案（锁定）

| 触发 | 标题 | 正文 | 按钮 |
|---|---|---|---|
| 关机拦截 | 请在手机上确认微信登录 | 设备即将关机，稍后 AI Agent 将尝试登录您的微信电脑版。请注意在手机 微信 中点击 确认登录 ，以便允许 Agent 登录您的微信电脑版。 | 确定（X/Esc = **取消关机**） |
| 手动发关机后延迟 2s | 请在手机上确认微信登录 | AI Agent 已收到您的关机提醒消息，请注意在手机 微信 中点击 确认登录 ，以便允许 Agent 登录您的微信电脑版。 | 确定 |
| 手动发开机 | 手动发送开机提醒 | 您确定要手动发送 开机提醒 的消息给 AI Agent 吗?这可能会使 Agent 主动退出微信及相关应用，且容易造成 AI 判断出现失误，您确定吗? | 确定 / 取消 |
| 手动发关机 | 手动发送关机提醒 | 您确定要手动发送 关机提醒 的消息给 AI Agent 吗?这可能会使 Agent 主动尝试登录微信及相关应用，且容易造成 AI 判断出现失误，您确定吗? | 确定 / 取消 |
| 重置配置 | 重置您的配置 | 您确定要重置全部配置信息吗?这会导致您的 Bot Token、Chat ID 等信息全部丢失，您需要重新填写后才可恢复软件使用。此操作不可逆，您确定吗? | **取消（主按钮）** / 确定 |

---

## 6. 关键流程

### 6.1 开机

```
系统启动后 N 分钟内拉起（默认 N=3，可配置）判定为「开机启动」
  → 检测 Clash 进程 → 代理通信 → Telegram + Google
  → 全部通过 → 走 SendTelegram 发开机提醒
  → 60s 内 Clash 未就绪 / 官方失败 → 立即 CF
  → 成功/失败弹系统通知 + 写状态窗口 + 写日志
```

### 6.2 关机（「关机前登录提醒」= 开）

```
WM_QUERYENDSESSION
  → 先发关机提醒（官方 10s → CF）
  → 弹窗「设备即将关机…」（ShutdownBlockReasonCreate，返回 FALSE）
  → 点「确定」→ ShutdownBlockReasonDestroy + 放行关机
  → 点 X / Esc → 取消本次关机（不放行）
```

开关 **关**：不拦截、不弹窗，**消息仍发**。

### 6.3 手动发送

```
点「发送消息」→ 确认弹窗 → 确定后单次发送（同通道降级）
  → 成败弹系统通知
  → 仅「关机」手动发送后延迟 2s 再弹「AI Agent 已收到…」
```

---

## 7. 工程结构

```
src/StatusTracker/
  App.xaml(.cs)              // 托盘、DI、静默启动、主题
  MainWindow.xaml(.cs)       // 5+ 卡片
  Views/Dialogs/             // 5 个 ContentDialog
  ViewModels/StatusViewModel.cs
  Models/AppConfig.cs, SendResult.cs, ProxyCheckResult.cs, ChannelStatus.cs
  Services/
    ConfigService.cs         // JSON + DPAPI
    Logger.cs
    AutostartManager.cs
    ProxyDetector.cs         // 仅展示
    TelegramService.cs       // SendTelegram 双通道
    ChannelClient.cs         // 官方 / CF 基地址切换
    ShutdownMonitor.cs
    ToastNotifier.cs
    UpdateService.cs         // 打开 GitHub
  Helpers/NativeMethods.cs   // WM_QUERYENDSESSION, ShutdownBlockReason
  Assets/                    // picture/*.svg → ico/png
picture/
  app-icon.svg, tray-normal.svg, tray-error.svg
tools/
  telegram-forwarder-worker.js
docs/
  PLAN.md                    // 本文件
  pc-notifier-channels-spec.md
```

---

## 8. 详细 TODO

> 顺序执行；完成后勾选。

### P0 工程骨架
- [ ] 创建 WinUI 3 解决方案 + MSIX 单项目工程（.NET 9）
- [ ] 引入 CommunityToolkit.Mvvm / H.NotifyIcon.WinUI / WindowsAppSDK
- [ ] 收录 `picture/` 三个 SVG，生成 ICO/PNG 应用与托盘资源
- [ ] 配置 appxmanifest（产品名「于の电脑 状态追踪器」）、app.manifest（PerMonitorV2）
- [ ] 明暗主题资源就绪（全部 ThemeResource，禁止硬编码色）

### P0 服务层
- [ ] `Logger` 按天滚动，Token/SECRET 只写掩码
- [ ] `ConfigService`：config.json 读写 + DPAPI 加解密 Bot Token / CF SECRET
- [ ] `AutostartManager`：HKCU Run 写删，开窗时读真实状态
- [ ] `ProxyDetector`：Clash 进程 / 端口 / 延迟三态文案 + Telegram + Google 双通
- [ ] `ToastNotifier`：开机发送成败系统横幅

### P0 通知通道
- [ ] `ChannelClient`：官方基地址 / CF 基地址（末尾 `/` 自动补全）
- [ ] `SendTelegram`：按「通道模式」降级；官方 15s×2 → CF 15s×2
- [ ] 开机路径：Clash 等待 15s×4（上限 60s）后转 CF
- [ ] 关机路径：官方 10s → 秒级 CF → 放弃只记录
- [ ] 「上次通知」记录实际通道名

### P0 关机拦截
- [ ] `ShutdownMonitor`：WM_QUERYENDSESSION / WM_ENDSESSION
- [ ] ShutdownBlockReasonCreate/Destroy
- [ ] 弹窗「设备即将关机…」；确定放行；X/Esc 取消关机
- [ ] 「关机前登录提醒」关闭时：不拦截不弹窗，仍发消息

### P1 主窗口 UI
- [ ] 卡片 1 网络与服务状态（四行 + 图标）
- [ ] 卡片 2 Bot 消息发送状态（时间戳 + 成败）
- [ ] 卡片 3 通知通道分组（模式/官方/CF/上次通知）
- [ ] 卡片 4 手动发送 Expander（两按钮）
- [ ] 卡片 5 配置 Expander（Token 眼睛、CF 地址、通道模式、主题、三个 Toggle、保存/重置）
- [ ] 卡片 6 检查更新（打开 GitHub）
- [ ] 页脚版本与版权
- [ ] 5 个 ContentDialog 按文案表实现（重置弹窗取消为主按钮）

### P1 托盘与生命周期
- [ ] 双态托盘图标（全绿正常，否则 Error）
- [ ] 菜单：打开 / 开机自启动 / 关机前登录提醒 / 退出
- [ ] 静默启动、关窗进托盘（默认开）
- [ ] 主题三选（跟随系统 / 黑暗 / 明亮）即时生效

### P1 流程串联
- [ ] 开机检测窗口 N=3 分钟判定 + 自动发送 + 系统通知
- [ ] 关机发送 + 拦截弹窗
- [ ] 手动发送确认弹窗 → 单次发送 →（关机）2s 后「已收到」弹窗

### P2 打包与验收
- [ ] `dotnet build` 无警告通过
- [ ] MSIX 打包（x64）
- [ ] 对照验收清单（见 §9）逐条过
- [ ] 日志/配置路径确认

### P2 发布
- [ ] README（含 CF Worker 部署 2 分钟步骤）
- [ ] 收录 `tools/telegram-forwarder-worker.js`
- [ ] 推送 GitHub，Release 挂 MSIX

---

## 9. 验收清单

- [ ] 开 Clash → 手动「发送开机提醒」→ 经官方直连成功
- [ ] 关 Clash → 手动发送 → 自动降级 CF 成功
- [ ] CF 地址填错且 Clash 关闭 → 发送失败，状态报错，托盘切 Error
- [ ] 模式「仅CF」→ 不开 Clash 也能发送
- [ ] 开机：Clash 未启动时，60s 内经 CF 发出开机通知
- [ ] 关机/重启：官方失败后秒级转 CF
- [ ] CF 地址为空：自动退化仅官方，CF 显示「未配置」
- [ ] 关机拦截弹窗：确定放行；X/Esc 取消关机
- [ ] 「关机前登录提醒」关：不拦截不弹窗，消息仍发
- [ ] Token 落盘加密，日志无明文
- [ ] 明/暗主题跟随系统；手动三选生效
- [ ] 高延迟黄叹号，托盘切 Error
- [ ] 保存/重置/检查更新/自启动注册表行为正确

---

## 10. 用户手动事项

### 10.1 Cloudflare Worker（约 2 分钟，**必须你做**）

1. 打开 <https://dash.cloudflare.com/> 登录  
2. 左侧 **Workers & Pages → Create → Create Worker**  
3. 随便起名（如 `yuno-tg`），点 **Deploy** 先建空的，再 **Edit code**  
4. 把 `tools/telegram-forwarder-worker.js` 全文粘贴进去  
5. **改 `SECRET`** 为 32 位以上随机字符串（只给自己和软件用）  
6. **Deploy**  
7. 复制 Worker 地址，拼成：  
   `https://<worker名>.<子域>.workers.dev/<SECRET>/`  
   （末尾斜杠必须有）  
8. 填进软件配置「CF 转发器地址」→ 保存  

可选加固：给 Worker 绑自己的域名（国内更稳）。

### 10.2 图标

- 稍后自行设计最终图标替换 `picture/` 内 SVG（路径已预留）

### 10.3 GitHub Token

- Token 仅用于推送/Release，**不进仓库、不进配置**
- 建议用完后去 GitHub 吊销并重新生成（它已在聊天中出现过）

---

## 11. 默认值速查

| 项 | 值 |
|---|---|
| 开机判定窗口 N | 3 分钟 |
| Clash 就绪等待上限 | 60 秒（每 15s 一次） |
| 官方通道超时/次数 | 15s × 2（关机场景 10s，不轮询） |
| CF 通道超时/次数 | 15s × 2 |
| 延迟正常 / 偏高 / 异常 | ≤1500ms / 1500–2500ms / >2500ms 或失败 |
| 默认代理端口 | 7890 |
| 主题 | 跟随系统 |
| 通道模式 | 自动 |
| 关窗进托盘 | 开 |
| 配置路径 | `%AppData%\PcStatusNotifier\config.json` |
| 日志路径 | `%LocalAppData%\PcStatusNotifier\logs\` |
