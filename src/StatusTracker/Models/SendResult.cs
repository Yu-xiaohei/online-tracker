namespace StatusTracker.Models;

/// <summary>发送结果。</summary>
public sealed record SendResult(bool Success, string Message, DateTimeOffset Timestamp, string? Error = null, string Channel = "")
{
    public string StatusText => Success ? "成功" : "失败";
    public string DisplayText =>
        $"{Timestamp:yyyy-MM-dd HH:mm:ss}  {StatusText}" +
        (string.IsNullOrEmpty(Channel) ? string.Empty : $"  经{Channel}") +
        (string.IsNullOrEmpty(Error) ? string.Empty : $"  {Error}");
}

/// <summary>通知通道标识。</summary>
public static class NotifyChannels
{
    public const string Official = "官方直连";
    public const string Cf = "CF转发";
    public const string Unconfigured = "未配置";
}

/// <summary>代理/连通性总体状态。</summary>
public enum OverallLinkState
{
    Checking,
    Normal,
    HighLatency,
    Faulted,
}

/// <summary>单次检测结果。</summary>
public sealed record ProxyCheckResult(
    bool ClashRunning,
    bool ProxyPortReachable,
    bool ClashApiReachable,
    bool TelegramReachable,
    bool GoogleReachable,
    long? LatencyMs,
    OverallLinkState State,
    string Detail)
{
    public string ClashText => ClashRunning ? "运行中" : "未运行";
    public string VpnText => State switch
    {
        OverallLinkState.Normal => "连接正常",
        OverallLinkState.HighLatency => "延迟较高",
        OverallLinkState.Faulted => "连接异常",
        _ => "检测中",
    };
    public string TelegramText => TelegramReachable ? "可用" : "不可用";
    public string GoogleText => GoogleReachable ? "可用" : "不可用";
}

/// <summary>通道健康状态。</summary>
public sealed record ChannelHealth(string Name, ChannelState State, string Detail, DateTimeOffset Timestamp)
{
    public string StateText => State switch
    {
        ChannelState.Ok => "正常",
        ChannelState.Unconfigured => "未配置",
        ChannelState.Unknown => "未检测",
        _ => "异常",
    };
}

public enum ChannelState
{
    Unknown,
    Ok,
    Unconfigured,
    Error,
}
