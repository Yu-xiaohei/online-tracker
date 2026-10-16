using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Win32;
using StatusTracker.Models;

namespace StatusTracker.Services;

/// <summary>开机自启动：HKCU\...\Run。</summary>
public sealed class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PcStatusNotifier";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Read autostart failed: {ex.Message}");
            return false;
        }
    }

    public bool Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath
                          ?? Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(exePath))
            {
                Logger.Instance.Error("Autostart enable failed: cannot resolve executable path");
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(ValueName, $"\"{exePath}\"");
            Logger.Instance.Info($"Autostart enabled: {exePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Autostart enable failed: {ex.Message}");
            return false;
        }
    }

    public bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            Logger.Instance.Info("Autostart disabled");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Autostart disable failed: {ex.Message}");
            return false;
        }
    }

    public bool SetEnabled(bool enabled) => enabled ? Enable() : Disable();
}

/// <summary>Clash / 代理 / 外网连通性检测。仅用于状态展示，不阻塞发送。</summary>
public sealed class ProxyDetector
{
    private static readonly string[] ClashProcessNames =
    {
        "clash", "Clash for Windows", "Clash Verge", "clash-verge",
        "Clash Verge Rev", "clash-verge-rev", "ClashN", "mihomo", "verge-mihomo",
    };

    private readonly ConfigService _configService;

    public ProxyDetector(ConfigService configService) => _configService = configService;

    public async Task<ProxyCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var cfg = _configService.Load();
        var clashRunning = IsClashRunning();
        var proxyPortOk = await IsPortOpenAsync("127.0.0.1", cfg.ProxyPort, 800, ct);
        var apiPortOk = await IsPortOpenAsync("127.0.0.1", cfg.ClashApiPort, 800, ct);

        long? latency = null;
        var telegramOk = false;
        var googleOk = false;

        try
        {
            using var tg = CreateProxyClient(cfg, TimeSpan.FromSeconds(5));
            var sw = Stopwatch.StartNew();
            using var resp = await tg.GetAsync("https://api.telegram.org", ct);
            sw.Stop();
            latency = sw.ElapsedMilliseconds;
            telegramOk = true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"Telegram reachability: {ex.Message}");
        }

        try
        {
            using var g = CreateProxyClient(cfg, TimeSpan.FromSeconds(5));
            using var resp = await g.GetAsync("https://www.google.com/generate_204", ct);
            googleOk = true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"Google reachability: {ex.Message}");
        }

        var state = Evaluate(latency, telegramOk, googleOk, cfg);
        var result = new ProxyCheckResult(
            clashRunning, proxyPortOk, apiPortOk, telegramOk, googleOk,
            latency, state, $"tg={telegramOk}, google={googleOk}, latency={latency?.ToString() ?? "n/a"}");

        Logger.Instance.Info(
            $"Proxy check: clash={clashRunning}, port={proxyPortOk}, api={apiPortOk}, " +
            $"tg={telegramOk}, google={googleOk}, latency={latency?.ToString() ?? "n/a"}ms, state={state}");
        return result;
    }

    public static bool IsClashRunning()
    {
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName;
                    if (ClashProcessNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
                catch
                {
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"IsClashRunning error: {ex.Message}");
        }

        return false;
    }

    private static OverallLinkState Evaluate(long? latency, bool tg, bool google, AppConfig cfg)
    {
        if (!tg || !google) return OverallLinkState.Faulted;
        if (latency is null) return OverallLinkState.Faulted;
        if (latency.Value > cfg.LatencyBadMs) return OverallLinkState.Faulted;
        if (latency.Value > cfg.LatencyGoodMs) return OverallLinkState.HighLatency;
        return OverallLinkState.Normal;
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>走本地 Clash HTTP 代理的 HttpClient。</summary>
    public static HttpClient CreateProxyClient(AppConfig cfg, TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = new WebProxy(new Uri($"http://127.0.0.1:{cfg.ProxyPort}")),
        };
        return new HttpClient(handler) { Timeout = timeout };
    }

    /// <summary>不走代理的 HttpClient（CF workers.dev 直连）。</summary>
    public static HttpClient CreateDirectClient(TimeSpan timeout)
    {
        return new HttpClient(new HttpClientHandler { UseProxy = true })
        {
            Timeout = timeout,
        };
    }
}
