using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using StatusTracker.Models;
using StatusTracker.Services;

namespace StatusTracker.ViewModels;

/// <summary>状态窗口主 ViewModel。</summary>
public sealed partial class StatusViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly ProxyDetector _proxyDetector;
    private readonly TelegramService _telegram;
    private readonly AutostartManager _autostart;

    public StatusViewModel(
        ConfigService configService,
        ProxyDetector proxyDetector,
        TelegramService telegram,
        AutostartManager autostart)
    {
        _configService = configService;
        _proxyDetector = proxyDetector;
        _telegram = telegram;
        _autostart = autostart;
        LoadFromConfig();
        _telegram.StatusChanged += (_, _) => RefreshChannelStatus();
    }

    [ObservableProperty] private string clashText = "检测中";
    [ObservableProperty] private string vpnText = "检测中";
    [ObservableProperty] private string telegramText = "检测中";
    [ObservableProperty] private string googleText = "检测中";
    [ObservableProperty] private string clashGlyph = "•";
    [ObservableProperty] private string vpnGlyph = "•";
    [ObservableProperty] private string telegramGlyph = "•";
    [ObservableProperty] private string googleGlyph = "•";

    [ObservableProperty] private string lastBootText = "暂无记录";
    [ObservableProperty] private string lastBootState = "—";
    [ObservableProperty] private string lastBootGlyph = "•";
    [ObservableProperty] private string lastShutdownText = "暂无记录";
    [ObservableProperty] private string lastShutdownState = "—";
    [ObservableProperty] private string lastShutdownGlyph = "•";

    [ObservableProperty] private string channelModeText = "自动";
    [ObservableProperty] private string officialChannelText = "未检测";
    [ObservableProperty] private string officialChannelGlyph = "•";
    [ObservableProperty] private string cfChannelText = "未配置";
    [ObservableProperty] private string cfChannelGlyph = "•";
    [ObservableProperty] private string lastNotifyText = "暂无记录";

    [ObservableProperty] private string botToken = string.Empty;
    [ObservableProperty] private string chatId = string.Empty;
    [ObservableProperty] private string proxyPortText = "7890";
    [ObservableProperty] private string cfBaseUrl = string.Empty;
    [ObservableProperty] private int channelModeIndex = 0;
    [ObservableProperty] private int themeModeIndex = 0;
    [ObservableProperty] private bool autostartEnabled;
    [ObservableProperty] private bool minimizeToTray = true;
    [ObservableProperty] private bool shutdownReminderEnabled = true;
    [ObservableProperty] private string statusMessage = string.Empty;

    /// <summary>主窗口主题变化请求。</summary>
    public event EventHandler<ElementTheme>? ThemeRequested;

    /// <summary>健康状态变化（true=全绿）。</summary>
    public event EventHandler<bool>? HealthChanged;

    /// <summary>手动发送前需确认（返回 true 表示用户确认）。</summary>
    public Func<string, Task<bool>>? ConfirmManualSendAsync { get; set; }

    /// <summary>手动发送关机后延迟弹窗。</summary>
    public Func<Task>? ShowAfterShutdownManualAsync { get; set; }

    /// <summary>重置配置需确认。</summary>
    public Func<Task<bool>>? ConfirmResetAsync { get; set; }

    private void LoadFromConfig()
    {
        var cfg = _configService.Load();
        BotToken = cfg.BotToken;
        ChatId = cfg.ChatId;
        ProxyPortText = cfg.ProxyPort.ToString();
        CfBaseUrl = cfg.CfBaseUrl;
        ChannelModeIndex = cfg.ChannelMode switch
        {
            ChannelMode.Auto => 0,
            ChannelMode.OfficialOnly => 1,
            ChannelMode.CfOnly => 2,
            _ => 0,
        };
        ThemeModeIndex = cfg.ThemeMode switch
        {
            ThemeMode.System => 0,
            ThemeMode.Dark => 1,
            ThemeMode.Light => 2,
            _ => 0,
        };
        AutostartEnabled = _autostart.IsEnabled();
        MinimizeToTray = cfg.MinimizeToTray;
        ShutdownReminderEnabled = cfg.ShutdownReminderEnabled;
        ApplyTheme(cfg.ThemeMode);
        RefreshChannelStatus();
    }

    public void RefreshChannelStatus()
    {
        ChannelModeText = ChannelModeIndex switch
        {
            0 => "自动",
            1 => "仅官方直连",
            2 => "仅CF转发",
            _ => "自动",
        };
        OfficialChannelText = _telegram.OfficialHealth.StateText;
        OfficialChannelGlyph = GlyphFor(_telegram.OfficialHealth.State);
        CfChannelText = _telegram.CfHealth.StateText;
        CfChannelGlyph = GlyphFor(_telegram.CfHealth.State);
        LastNotifyText = _telegram.LastNotifyText;
    }

    private static string GlyphFor(ChannelState state) => state switch
    {
        ChannelState.Ok => "",       // Segoe MDL2 CheckMark
        ChannelState.Error => "",    // Warning
        ChannelState.Unconfigured => "○",
        _ => "○",
    };

    [RelayCommand]
    private async Task CheckProxyAsync()
    {
        try
        {
            var result = await _proxyDetector.CheckAsync();
            ClashText = result.ClashText;
            VpnText = result.VpnText;
            TelegramText = result.TelegramText;
            GoogleText = result.GoogleText;
            ClashGlyph = result.ClashRunning ? "" : "";
            VpnGlyph = result.State switch
            {
                OverallLinkState.Normal => "",
                OverallLinkState.HighLatency => "",
                OverallLinkState.Faulted => "",
                _ => "○",
            };
            TelegramGlyph = result.TelegramReachable ? "" : "";
            GoogleGlyph = result.GoogleReachable ? "" : "";
            StatusMessage = "检测完成";

            // 托盘：任一项异常（含延迟较高）→ Error；全绿 → Normal
            var allHealthy =
                result.ClashRunning &&
                result.State == OverallLinkState.Normal &&
                result.TelegramReachable &&
                result.GoogleReachable;
            HealthChanged?.Invoke(this, allHealthy);
        }
        catch (Exception ex)
        {
            StatusMessage = $"检测失败: {ex.Message}";
            HealthChanged?.Invoke(this, false);
        }
    }

    [RelayCommand]
    private async Task SendBootManualAsync()
    {
        if (ConfirmManualSendAsync != null)
        {
            var ok = await ConfirmManualSendAsync("boot");
            if (!ok) return;
        }

        var result = await _telegram.SendManualBootAsync();
        LastBootText = result.DisplayText;
        LastBootState = result.Success ? "发送成功" : "发送失败";
        LastBootGlyph = result.Success ? "" : "";
        StatusMessage = result.Success
            ? $"开机提醒{result.StatusText}（{result.Channel}）"
            : $"开机提醒{result.StatusText}";
        ToastNotifier.ShowBootResult(result.Success, result.DisplayText);
    }

    [RelayCommand]
    private async Task SendShutdownManualAsync()
    {
        if (ConfirmManualSendAsync != null)
        {
            var ok = await ConfirmManualSendAsync("shutdown");
            if (!ok) return;
        }

        var result = await _telegram.SendManualShutdownAsync();
        LastShutdownText = result.DisplayText;
        LastShutdownState = result.Success ? "发送成功" : "发送失败";
        LastShutdownGlyph = result.Success ? "" : "";
        StatusMessage = result.Success
            ? $"关机提醒{result.StatusText}（{result.Channel}）"
            : $"关机提醒{result.StatusText}";
        ToastNotifier.Show(result.Success ? "关机提醒发送成功" : "关机提醒发送失败", result.DisplayText);

        if (result.Success && ShowAfterShutdownManualAsync != null)
        {
            await Task.Delay(2000);
            await ShowAfterShutdownManualAsync();
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var cfg = BuildConfigFromInputs();
        _configService.Save(cfg);
        _autostart.SetEnabled(cfg.AutostartEnabled);
        ApplyTheme(cfg.ThemeMode);
        StatusMessage = "配置已保存";
        RefreshChannelStatus();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        if (ConfirmResetAsync != null)
        {
            var ok = await ConfirmResetAsync();
            if (!ok) return;
        }

        _configService.Reset();
        LoadFromConfig();
        StatusMessage = "配置已重置";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleTokenVisibility()
        => TokenVisible = !TokenVisible;

    [ObservableProperty] private bool tokenVisible;

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/Yu-xiaohei/online-tracker",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开链接失败: {ex.Message}";
        }
    }

    public void OnThemeSelectionChanged(int index)
    {
        var mode = index switch
        {
            1 => ThemeMode.Dark,
            2 => ThemeMode.Light,
            _ => ThemeMode.System,
        };
        ApplyTheme(mode);
    }

    private void ApplyTheme(ThemeMode mode)
    {
        var theme = mode switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };
        ThemeRequested?.Invoke(this, theme);
    }

    private AppConfig BuildConfigFromInputs()
    {
        var cfg = _configService.Load();
        cfg.BotToken = BotToken;
        cfg.ChatId = ChatId;
        if (int.TryParse(ProxyPortText, out var port)) cfg.ProxyPort = port;
        cfg.CfBaseUrl = AppConfig.AppendTrailingSlash(CfBaseUrl);
        cfg.ChannelMode = ChannelModeIndex switch
        {
            1 => ChannelMode.OfficialOnly,
            2 => ChannelMode.CfOnly,
            _ => ChannelMode.Auto,
        };
        cfg.ThemeMode = ThemeModeIndex switch
        {
            1 => ThemeMode.Dark,
            2 => ThemeMode.Light,
            _ => ThemeMode.System,
        };
        cfg.AutostartEnabled = AutostartEnabled;
        cfg.MinimizeToTray = MinimizeToTray;
        cfg.ShutdownReminderEnabled = ShutdownReminderEnabled;
        return cfg;
    }
}
