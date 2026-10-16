using System.Text.Json.Serialization;

namespace StatusTracker.Models;

/// <summary>应用配置（config.json）。敏感字段 DPAPI 加密，日志只写掩码。</summary>
public sealed class AppConfig
{
    /// <summary>Telegram Bot Token。</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>频道/群组 Chat ID。</summary>
    public string ChatId { get; set; } = string.Empty;

    /// <summary>Clash HTTP 代理端口。</summary>
    public int ProxyPort { get; set; } = 7890;

    /// <summary>Clash 外部控制 API 端口。</summary>
    public int ClashApiPort { get; set; } = 9090;

    /// <summary>CF 转发器基地址，如 https://xxx.workers.dev/&lt;SECRET&gt;/</summary>
    public string CfBaseUrl { get; set; } = string.Empty;

    /// <summary>通道模式。</summary>
    public ChannelMode ChannelMode { get; set; } = ChannelMode.Auto;

    /// <summary>界面主题。</summary>
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    public bool AutostartEnabled { get; set; }

    /// <summary>关闭窗口后驻留托盘。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>关机前微信登录拦截弹窗。</summary>
    public bool ShutdownReminderEnabled { get; set; } = true;

    public string TrayIconPath { get; set; } = string.Empty;

    /// <summary>延迟较好阈值（毫秒）。</summary>
    public int LatencyGoodMs { get; set; } = 1500;

    /// <summary>延迟异常阈值（毫秒）。</summary>
    public int LatencyBadMs { get; set; } = 2500;

    /// <summary>开机判定窗口（分钟）：系统启动后 N 分钟内视为开机启动。</summary>
    public int BootDetectWindowMinutes { get; set; } = 3;

    /// <summary>官方通道单次超时（秒）。</summary>
    public int OfficialTimeoutSeconds { get; set; } = 15;

    /// <summary>关机场景官方通道超时（秒）。</summary>
    public int ShutdownTimeoutSeconds { get; set; } = 10;

    /// <summary>CF 通道单次超时（秒）。</summary>
    public int CfTimeoutSeconds { get; set; } = 15;

    /// <summary>每通道最大尝试次数。</summary>
    public int ChannelMaxAttempts { get; set; } = 2;

    /// <summary>开机等待 Clash 就绪的总窗口（秒）。</summary>
    public int BootWaitClashSeconds { get; set; } = 60;

    [JsonIgnore]
    public string BotTokenMasked => MaskSecret(BotToken);

    [JsonIgnore]
    public string CfSecretMasked => MaskSecret(ExtractCfSecret(CfBaseUrl));

    public static string MaskSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (value.Length <= 8) return new string('*', value.Length);
        return value[..4] + new string('*', value.Length - 8) + value[^4..];
    }

    public static string ExtractCfSecret(string? cfBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(cfBaseUrl)) return string.Empty;
        try
        {
            var uri = new Uri(AppendTrailingSlash(cfBaseUrl));
            var seg = uri.AbsolutePath.Trim('/');
            return seg;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string AppendTrailingSlash(string url)
        => string.IsNullOrWhiteSpace(url) ? url.Trim() : url.Trim().EndsWith('/') ? url.Trim() : url.Trim() + "/";
}

public enum ChannelMode
{
    Auto,
    OfficialOnly,
    CfOnly,
}

public enum ThemeMode
{
    System,
    Dark,
    Light,
}
