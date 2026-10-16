using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StatusTracker.Models;

namespace StatusTracker.Services;

/// <summary>
/// 配置读写。Bot Token / CF SECRET 使用 DPAPI（CurrentUser）加密落盘，
/// 磁盘上不存明文，日志只写掩码。
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _lock = new();
    private readonly string _configPath;

    public ConfigService(string? configDirectory = null)
    {
        var dir = configDirectory
                  ?? Path.Combine(
                      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                      "PcStatusNotifier");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "config.json");
        Logger.Instance.Info($"Config path: {_configPath}");
    }

    public string ConfigPath => _configPath;

    public AppConfig Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    var defaults = new AppConfig();
                    Save(defaults);
                    Logger.Instance.Info("Config not found, created default config.json");
                    return defaults;
                }

                var dto = JsonSerializer.Deserialize<ConfigDto>(
                    File.ReadAllText(_configPath), JsonOptions) ?? new ConfigDto();

                return new AppConfig
                {
                    BotToken = Unprotect(dto.BotTokenEnc),
                    ChatId = dto.ChatId ?? string.Empty,
                    ProxyPort = dto.ProxyPort,
                    ClashApiPort = dto.ClashApiPort,
                    CfBaseUrl = Unprotect(dto.CfBaseUrlEnc),
                    ChannelMode = dto.ChannelMode,
                    ThemeMode = dto.ThemeMode,
                    AutostartEnabled = dto.AutostartEnabled,
                    MinimizeToTray = dto.MinimizeToTray,
                    ShutdownReminderEnabled = dto.ShutdownReminderEnabled,
                    TrayIconPath = dto.TrayIconPath ?? string.Empty,
                    LatencyGoodMs = dto.LatencyGoodMs,
                    LatencyBadMs = dto.LatencyBadMs,
                    BootDetectWindowMinutes = dto.BootDetectWindowMinutes,
                    OfficialTimeoutSeconds = dto.OfficialTimeoutSeconds,
                    ShutdownTimeoutSeconds = dto.ShutdownTimeoutSeconds,
                    CfTimeoutSeconds = dto.CfTimeoutSeconds,
                    ChannelMaxAttempts = dto.ChannelMaxAttempts,
                    BootWaitClashSeconds = dto.BootWaitClashSeconds,
                };
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Failed to load config: {ex.Message}");
                return new AppConfig();
            }
        }
    }

    public void Save(AppConfig config)
    {
        lock (_lock)
        {
            try
            {
                var dto = new ConfigDto
                {
                    BotTokenEnc = Protect(config.BotToken),
                    ChatId = config.ChatId,
                    ProxyPort = config.ProxyPort,
                    ClashApiPort = config.ClashApiPort,
                    CfBaseUrlEnc = Protect(AppConfig.AppendTrailingSlash(config.CfBaseUrl)),
                    ChannelMode = config.ChannelMode,
                    ThemeMode = config.ThemeMode,
                    AutostartEnabled = config.AutostartEnabled,
                    MinimizeToTray = config.MinimizeToTray,
                    ShutdownReminderEnabled = config.ShutdownReminderEnabled,
                    TrayIconPath = config.TrayIconPath,
                    LatencyGoodMs = config.LatencyGoodMs,
                    LatencyBadMs = config.LatencyBadMs,
                    BootDetectWindowMinutes = config.BootDetectWindowMinutes,
                    OfficialTimeoutSeconds = config.OfficialTimeoutSeconds,
                    ShutdownTimeoutSeconds = config.ShutdownTimeoutSeconds,
                    CfTimeoutSeconds = config.CfTimeoutSeconds,
                    ChannelMaxAttempts = config.ChannelMaxAttempts,
                    BootWaitClashSeconds = config.BootWaitClashSeconds,
                };

                File.WriteAllText(_configPath, JsonSerializer.Serialize(dto, JsonOptions));
                Logger.Instance.Info(
                    $"Config saved. token={config.BotTokenMasked}, chatId={config.ChatId}, " +
                    $"cf={config.CfSecretMasked}, mode={config.ChannelMode}, theme={config.ThemeMode}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Failed to save config: {ex.Message}");
            }
        }
    }

    /// <summary>重置为默认值（清空敏感信息）。</summary>
    public void Reset()
    {
        Save(new AppConfig());
        Logger.Instance.Warn("Config reset to defaults");
    }

    private static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(cipher), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DPAPI unprotect failed: {ex.Message}");
            return string.Empty;
        }
    }

    private sealed class ConfigDto
    {
        public string BotTokenEnc { get; set; } = string.Empty;
        public string? ChatId { get; set; }
        public int ProxyPort { get; set; } = 7890;
        public int ClashApiPort { get; set; } = 9090;
        public string CfBaseUrlEnc { get; set; } = string.Empty;
        public ChannelMode ChannelMode { get; set; } = ChannelMode.Auto;
        public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
        public bool AutostartEnabled { get; set; }
        public bool MinimizeToTray { get; set; } = true;
        public bool ShutdownReminderEnabled { get; set; } = true;
        public string? TrayIconPath { get; set; }
        public int LatencyGoodMs { get; set; } = 1500;
        public int LatencyBadMs { get; set; } = 2500;
        public int BootDetectWindowMinutes { get; set; } = 3;
        public int OfficialTimeoutSeconds { get; set; } = 15;
        public int ShutdownTimeoutSeconds { get; set; } = 10;
        public int CfTimeoutSeconds { get; set; } = 15;
        public int ChannelMaxAttempts { get; set; } = 2;
        public int BootWaitClashSeconds { get; set; } = 60;
    }
}
