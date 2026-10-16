using System.Text;
using System.Text.Json;
using StatusTracker.Models;

namespace StatusTracker.Services;

/// <summary>
/// Telegram 发送统一入口：官方直连 + Cloudflare Worker 反代，
/// 按「通道模式」降级。Clash 检测不阻塞发送。
/// </summary>
public sealed class TelegramService
{
    public const string BootMessage = "🖥️ YU-PC 开机了，准备登微信";
    public const string ShutdownMessage = "💤 YU-PC 关机了，AI Agent 可以申请登微信了";

    private readonly ConfigService _configService;

    /// <summary>当前官方通道健康。</summary>
    public ChannelHealth OfficialHealth { get; private set; }
        = new(NotifyChannels.Official, ChannelState.Unknown, "未检测", DateTimeOffset.Now);

    /// <summary>当前 CF 通道健康。</summary>
    public ChannelHealth CfHealth { get; private set; }
        = new(NotifyChannels.Cf, ChannelState.Unconfigured, "未配置", DateTimeOffset.Now);

    /// <summary>上次通知描述，含实际通道。</summary>
    public string LastNotifyText { get; private set; } = "暂无记录";

    public event EventHandler? StatusChanged;

    public TelegramService(ConfigService configService) => _configService = configService;

    /// <summary>开机通知：等 Clash 就绪最多 60s，官方失败立即 CF。</summary>
    public async Task<SendResult> SendBootAsync(CancellationToken ct = default)
    {
        var cfg = _configService.Load();
        var wait = TimeSpan.FromSeconds(Math.Max(0, cfg.BootWaitClashSeconds));
        var deadline = DateTimeOffset.Now + wait;

        // 自动/仅官方：先等 Clash；仅CF 不等
        if (cfg.ChannelMode != ChannelMode.CfOnly)
        {
            while (DateTimeOffset.Now < deadline)
            {
                if (ProxyDetector.IsClashRunning()) break;
                Logger.Instance.Info("Boot: waiting for Clash...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        return await SendAsync(cfg, BootMessage, ct);
    }

    /// <summary>关机通知：官方短超时，失败立刻 CF，再败只记录。</summary>
    public Task<SendResult> SendShutdownAsync(CancellationToken ct = default)
        => SendAsync(_configService.Load(), ShutdownMessage, ct, isShutdown: true);

    /// <summary>手动发送（开机文案）。</summary>
    public Task<SendResult> SendManualBootAsync(CancellationToken ct = default)
        => SendAsync(_configService.Load(), BootMessage, ct);

    /// <summary>手动发送（关机文案）。</summary>
    public Task<SendResult> SendManualShutdownAsync(CancellationToken ct = default)
        => SendAsync(_configService.Load(), ShutdownMessage, ct);

    private async Task<SendResult> SendAsync(
        AppConfig cfg, string text, CancellationToken ct, bool isShutdown = false)
    {
        if (string.IsNullOrWhiteSpace(cfg.BotToken) || string.IsNullOrWhiteSpace(cfg.ChatId))
        {
            var missing = new SendResult(false, "未配置 Bot Token 或 Chat ID", DateTimeOffset.Now, "missing config");
            PublishLast(missing);
            Logger.Instance.Error(missing.Message);
            return missing;
        }

        var officialTimeout = isShutdown ? cfg.ShutdownTimeoutSeconds : cfg.OfficialTimeoutSeconds;
        var attempts = Math.Max(1, cfg.ChannelMaxAttempts);
        var mode = cfg.ChannelMode;
        var cfReady = !string.IsNullOrWhiteSpace(cfg.CfBaseUrl);

        if (!cfReady)
        {
            CfHealth = new ChannelHealth(NotifyChannels.Cf, ChannelState.Unconfigured, "未配置", DateTimeOffset.Now);
            StatusChanged?.Invoke(this, EventArgs.Empty);
            if (mode == ChannelMode.CfOnly)
            {
                var fail = new SendResult(false, "CF 通道未配置", DateTimeOffset.Now, "cf unconfigured");
                PublishLast(fail);
                return fail;
            }
            if (mode == ChannelMode.Auto) mode = ChannelMode.OfficialOnly;
        }

        // 1) 官方通道
        if (mode is ChannelMode.Auto or ChannelMode.OfficialOnly)
        {
            var lastOfficial = new SendResult(false, "官方未发送", DateTimeOffset.Now);
            for (var i = 1; i <= attempts; i++)
            {
                lastOfficial = await TrySendViaAsync(
                    cfg, text, isOfficial: true, officialTimeout, ct);
                if (lastOfficial.Success)
                {
                    OfficialHealth = new ChannelHealth(NotifyChannels.Official, ChannelState.Ok, "发送成功", DateTimeOffset.Now);
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                    PublishLast(lastOfficial with { Channel = NotifyChannels.Official });
                    return lastOfficial with { Channel = NotifyChannels.Official };
                }

                if (isShutdown) break; // 关机场景不轮询
            }

            OfficialHealth = new ChannelHealth(
                NotifyChannels.Official, ChannelState.Error, lastOfficial.Error ?? "失败", DateTimeOffset.Now);
            StatusChanged?.Invoke(this, EventArgs.Empty);
            Logger.Instance.Warn($"Official channel failed: {lastOfficial.Error}");

            if (mode == ChannelMode.OfficialOnly)
            {
                var fail = lastOfficial with { Channel = NotifyChannels.Official };
                PublishLast(fail);
                return fail;
            }
        }

        // 2) CF 通道
        if (mode is ChannelMode.Auto or ChannelMode.CfOnly)
        {
            if (!cfReady)
            {
                var fail = new SendResult(false, "CF 未配置且官方失败", DateTimeOffset.Now, "cf unconfigured");
                PublishLast(fail);
                return fail;
            }

            SendResult lastCf = new(false, "CF未发送", DateTimeOffset.Now);
            for (var i = 1; i <= attempts; i++)
            {
                lastCf = await TrySendViaAsync(
                    cfg, text, isOfficial: false, cfg.CfTimeoutSeconds, ct);
                if (lastCf.Success)
                {
                    CfHealth = new ChannelHealth(NotifyChannels.Cf, ChannelState.Ok, "发送成功", DateTimeOffset.Now);
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                    PublishLast(lastCf with { Channel = NotifyChannels.Cf });
                    return lastCf with { Channel = NotifyChannels.Cf };
                }

                if (isShutdown) break;
            }

            CfHealth = new ChannelHealth(
                NotifyChannels.Cf, ChannelState.Error, lastCf.Error ?? "失败", DateTimeOffset.Now);
            StatusChanged?.Invoke(this, EventArgs.Empty);
            var failAll = lastCf with { Channel = NotifyChannels.Cf };
            PublishLast(failAll);
            return failAll;
        }

        var unknown = new SendResult(false, "未知通道模式", DateTimeOffset.Now, "bad mode");
        PublishLast(unknown);
        return unknown;
    }

    private async Task<SendResult> TrySendViaAsync(
        AppConfig cfg, string text, bool isOfficial, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            string url;
            if (isOfficial)
            {
                url = $"https://api.telegram.org/bot{cfg.BotToken}/sendMessage";
            }
            else
            {
                var baseUrl = AppConfig.AppendTrailingSlash(cfg.CfBaseUrl);
                url = $"{baseUrl}bot{cfg.BotToken}/sendMessage";
            }

            using var client = isOfficial
                ? ProxyDetector.CreateProxyClient(cfg, TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)))
                : ProxyDetector.CreateDirectClient(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

            var payload = new { chat_id = cfg.ChatId, text };
            using var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await client.PostAsync(url, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                Logger.Instance.Info($"Telegram send OK via {(isOfficial ? "official" : "cf")}: {text}");
                return new SendResult(true, "发送成功", DateTimeOffset.Now,
                    Channel: isOfficial ? NotifyChannels.Official : NotifyChannels.Cf);
            }

            var snippet = body.Length > 160 ? body[..160] + "…" : body;
            Logger.Instance.Error($"Telegram send failed via {(isOfficial ? "official" : "cf")}: {(int)response.StatusCode} {snippet}");
            return new SendResult(false, $"HTTP {(int)response.StatusCode}", DateTimeOffset.Now, snippet);
        }
        catch (OperationCanceledException)
        {
            Logger.Instance.Error($"Telegram send timeout via {(isOfficial ? "official" : "cf")}");
            return new SendResult(false, "发送超时", DateTimeOffset.Now, "timeout");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Telegram send exception via {(isOfficial ? "official" : "cf")}: {ex.Message}");
            return new SendResult(false, "发送异常", DateTimeOffset.Now, ex.Message);
        }
    }

    private void PublishLast(SendResult result)
    {
        LastNotifyText = $"{result.Timestamp:HH:mm} 经{(string.IsNullOrEmpty(result.Channel) ? "未知" : result.Channel)}{(result.Success ? "发送成功" : "发送失败")}";
        StatusChanged?.Invoke(this, EventArgs.Empty);
        Logger.Instance.Info($"LastNotify: {LastNotifyText}");
    }
}
