using System.Runtime.InteropServices;
using StatusTracker.Models;
using StatusTracker.Services;

namespace StatusTracker.Services;

/// <summary>
/// 开机启动判定与开机通知流程。
/// 系统启动后 N 分钟内启动本程序 → 判定为开机启动 → 等 Clash（上限 60s）→ 发送 → 系统通知。
/// </summary>
public sealed class BootNotificationService
{
    private readonly ConfigService _configService;
    private readonly TelegramService _telegram;

    public event EventHandler<string>? Progress;

    public BootNotificationService(ConfigService configService, TelegramService telegram)
    {
        _configService = configService;
        _telegram = telegram;
    }

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    /// <summary>系统已运行时长。</summary>
    public static TimeSpan SystemUptime => TimeSpan.FromMilliseconds(GetTickCount64());

    /// <summary>是否处于开机启动窗口内。</summary>
    public bool IsBootWindow()
    {
        var cfg = _configService.Load();
        var uptime = SystemUptime;
        var window = TimeSpan.FromMinutes(Math.Max(1, cfg.BootDetectWindowMinutes));
        var isBoot = uptime <= window;
        Logger.Instance.Info($"Boot detect: uptime={uptime}, window={window}, isBoot={isBoot}");
        return isBoot;
    }

    /// <summary>若为开机启动则发送开机提醒；否则跳过。返回是否发送。</summary>
    public async Task<bool> RunIfBootAsync(CancellationToken ct = default)
    {
        if (!IsBootWindow())
        {
            Logger.Instance.Info("Skip boot notify: not in boot window");
            return false;
        }

        Progress?.Invoke(this, "开机提醒发送中…");
        var result = await _telegram.SendBootAsync(ct);
        ToastNotifier.ShowBootResult(result.Success, result.DisplayText);
        Progress?.Invoke(this, result.Success
            ? $"开机提醒发送成功（{result.Channel}）"
            : $"开机提醒发送失败：{result.Error ?? result.Message}");
        Logger.Instance.Info($"Boot notify result: success={result.Success} channel={result.Channel}");
        return true;
    }
}
