using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using StatusTracker.Services;

namespace StatusTracker.Helpers;

/// <summary>Win32 互操作：关机拦截 / 窗口子类化。</summary>
public static class NativeMethods
{
    public const int WM_QUERYENDSESSION = 0x0011;
    public const int WM_ENDSESSION = 0x0016;
    public const int ENDSESSION_CLOSEAPP = 0x0001;
    public const int ENDSESSION_CRITICAL = 0x40000000;

    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string reason);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShutdownBlockReasonQuery(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] out string reason, out uint reasonLength);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public const int GWLP_WNDPROC = -4;
    public const uint EWX_SHUTDOWN = 0x00000001;
    public const uint EWX_FORCEIFHUNG = 0x00000010;
    public const uint EWX_POWEROFF = 0x00000008;

    /// <summary>获取 WinUI Window 的 HWND。</summary>
    public static IntPtr GetWindowHandle(Window window)
        => WinRT.Interop.WindowNative.GetWindowHandle(window);
}

/// <summary>
/// 关机拦截：WM_QUERYENDSESSION 时弹窗。
/// 确定=放行；X/Esc=取消本次关机。
/// 「关机前登录提醒」关闭：不拦截不弹窗，仍发消息。
/// </summary>
public sealed class ShutdownMonitor : IDisposable
{
    private readonly TelegramService _telegram;
    private readonly ConfigService _config;
    private readonly Func<Task<bool>> _showConfirmAsync;
    private IntPtr _hwnd;
    private NativeMethods.WndProcDelegate? _hook;
    private IntPtr _prevProc;
    private bool _handling;

    public event EventHandler<string>? StatusReported;

    public ShutdownMonitor(
        TelegramService telegram,
        ConfigService config,
        Func<Task<bool>> showConfirmAsync)
    {
        _telegram = telegram;
        _config = config;
        _showConfirmAsync = showConfirmAsync;
    }

    public void Attach(Window window)
    {
        _hwnd = NativeMethods.GetWindowHandle(window);
        _hook = HookProc;
        var ptr = Marshal.GetFunctionPointerForDelegate(_hook);
        _prevProc = NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_WNDPROC, ptr);
        Logger.Instance.Info($"ShutdownMonitor attached hwnd=0x{_hwnd:X}");
    }

    private IntPtr HookProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_QUERYENDSESSION)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await OnEndSessionAsync();
                }
                catch (Exception ex)
                {
                    Logger.Instance.Error($"OnEndSession failed: {ex.Message}");
                }
            });

            // 阻塞本次关机，等待用户确认或取消
            NativeMethods.ShutdownBlockReasonCreate(hWnd, "正在发送关机提醒并等待微信登录确认");
            return IntPtr.Zero;
        }

        return NativeMethods.CallWindowProc(_prevProc, hWnd, msg, wParam, lParam);
    }

    private async Task OnEndSessionAsync()
    {
        if (_handling) return;
        _handling = true;
        try
        {
            var cfg = _config.Load();

            // 先发关机消息（不弹窗也要发）
            var sendTask = _telegram.SendShutdownAsync();
            StatusReported?.Invoke(this, "关机提醒发送中…");

            if (!cfg.ShutdownReminderEnabled)
            {
                // 开关关闭：不拦截不弹窗，消息仍发
                var r = await sendTask;
                Logger.Instance.Info($"Shutdown notify (no intercept): success={r.Success}");
                AllowShutdown();
                return;
            }

            // 发送与弹窗并行：用户点确定时若发送未完则继续等完（关机场景发送本身很短）
            var confirmTask = _showConfirmAsync();
            var sendResult = await sendTask;
            Logger.Instance.Info($"Shutdown notify: success={sendResult.Success} channel={sendResult.Channel}");

            var confirmed = await confirmTask;
            if (confirmed)
            {
                AllowShutdown();
            }
            else
            {
                // X / Esc = 取消本次关机
                CancelShutdown();
            }
        }
        finally
        {
            _handling = false;
        }
    }

    /// <summary>放行关机。</summary>
    private void AllowShutdown()
    {
        NativeMethods.ShutdownBlockReasonDestroy(_hwnd);
        // 由系统继续完成剩余关机流程
        NativeMethods.ExitWindowsEx(NativeMethods.EWX_SHUTDOWN | NativeMethods.EWX_FORCEIFHUNG, 0);
        Logger.Instance.Info("Shutdown allowed");
    }

    /// <summary>取消本次关机。</summary>
    private void CancelShutdown()
    {
        NativeMethods.ShutdownBlockReasonDestroy(_hwnd);
        Logger.Instance.Info("Shutdown cancelled by user");
        StatusReported?.Invoke(this, "已取消关机");
    }

    public void Dispose()
    {
        if (_prevProc != IntPtr.Zero && _hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_WNDPROC, _prevProc);
            _prevProc = IntPtr.Zero;
        }
        NativeMethods.ShutdownBlockReasonDestroy(_hwnd);
    }
}
