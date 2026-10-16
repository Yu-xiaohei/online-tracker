using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using StatusTracker.Helpers;
using StatusTracker.Services;
using StatusTracker.ViewModels;

namespace StatusTracker;

public sealed partial class MainWindow : Window
{
    public StatusViewModel Vm { get; }

    private readonly ConfigService _configService = new();
    private readonly TelegramService _telegram;
    private readonly AutostartManager _autostart = new();
    private ShutdownMonitor? _shutdownMonitor;
    private TaskbarIcon? _trayIcon;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        Title = "于の电脑 状态追踪器";

        var proxy = new ProxyDetector(_configService);
        _telegram = new TelegramService(_configService);
        Vm = new StatusViewModel(_configService, proxy, _telegram, _autostart);

        Vm.ThemeRequested += (_, theme) =>
        {
            if (Content is FrameworkElement root) root.RequestedTheme = theme;
        };

        Vm.ConfirmManualSendAsync = kind =>
            ShowTextDialogAsync(
                kind == "boot" ? "手动发送开机提醒" : "手动发送关机提醒",
                kind == "boot"
                    ? "您确定要手动发送 开机提醒 的消息给 AI Agent 吗?这可能会使 Agent 主动退出微信及相关应用，且容易造成 AI 判断出现失误，您确定吗?"
                    : "您确定要手动发送 关机提醒 的消息给 AI Agent 吗?这可能会使 Agent 主动尝试登录微信及相关应用，且容易造成 AI 判断出现失误，您确定吗?",
                confirmText: "确定", cancelText: "取消");

        Vm.ShowAfterShutdownManualAsync = () =>
            ShowTextDialogAsync(
                "请在手机上确认微信登录",
                "AI Agent 已收到您的关机提醒消息，请注意在手机 微信 中点击 确认登录 ，以便允许 Agent 登录您的微信电脑版。",
                confirmText: "确定", cancelText: null);

        Vm.ConfirmResetAsync = () =>
            ShowTextDialogAsync(
                "重置您的配置",
                "您确定要重置全部配置信息吗?这会导致您的 Bot Token、Chat ID 等信息全部丢失，您需要重新填写后才可恢复软件使用。此操作不可逆，您确定吗?",
                confirmText: "确定", cancelText: "取消",
                primaryIsCancel: true);

        Vm.HealthChanged += (_, healthy) => UpdateTrayFromHealth(healthy);

        TokenBox.Password = Vm.BotToken;
        InitTray();

        // 关机拦截：确定放行 / X·Esc 取消关机
        _shutdownMonitor = new ShutdownMonitor(_telegram, _configService, () =>
            ShowTextDialogAsync(
                "请在手机上确认微信登录",
                "设备即将关机，稍后 AI Agent 将尝试登录您的微信电脑版。请注意在手机 微信 中点击 确认登录 ，以便允许 Agent 登录您的微信电脑版。",
                confirmText: "确定", cancelText: null,
                treatCloseAsCancelShutdown: true));

        AppWindow.Closing += OnAppWindowClosing;

        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) return;
            try
            {
                _shutdownMonitor?.Attach(this);
            }
            catch (Exception ex)
            {
                Logger.Instance.Warn($"ShutdownMonitor attach: {ex.Message}");
            }
        };

        _ = Vm.CheckProxyCommand.ExecuteAsync(null);

        // 开机通知：仅系统启动窗口内自动发送
        _ = Task.Run(async () =>
        {
            try
            {
                var boot = new BootNotificationService(_configService, _telegram);
                await boot.RunIfBootAsync();
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Boot notify flow: {ex.Message}");
            }
        });
    }

    private void InitTray()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "于の电脑 状态追踪器",
        };
        _trayIcon.IconSource = new BitmapImage(new Uri("ms-appx:///Assets/tray-normal.png"));

        var menu = new MenuFlyout();
        var openItem = new MenuFlyoutItem { Text = "打开应用窗口" };
        openItem.Click += OnOpenWindowClick;
        var autostartItem = new MenuFlyoutItem { Text = "开机自启动" };
        autostartItem.Click += OnToggleAutostartClick;
        var reminderItem = new MenuFlyoutItem { Text = "关机前登录提醒" };
        reminderItem.Click += OnToggleShutdownReminderClick;
        var exitItem = new MenuFlyoutItem { Text = "退出" };
        exitItem.Click += OnExitClick;
        menu.Items.Add(openItem);
        menu.Items.Add(autostartItem);
        menu.Items.Add(reminderItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exitItem);
        _trayIcon.ContextFlyout = menu;

        // 左键打开窗口
        _trayIcon.ForceCreate();
        _trayIcon.LeftClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ShowFromTray);
        Logger.Instance.Info("Tray icon initialized");
    }

    private void ShowFromTray()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AppWindow.Show();
            Activate();
            var hwnd = NativeMethods.GetWindowHandle(this);
            NativeMethods.SetForegroundWindow(hwnd);
        });
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_forceClose || !Vm.MinimizeToTray)
        {
            try
            {
                _trayIcon?.Dispose();
                _shutdownMonitor?.Dispose();
            }
            catch
            {
            }
            return;
        }

        args.Cancel = true;
        AppWindow.Hide();
        Logger.Instance.Info("Window hidden to tray");
    }

    private void SyncTrayIcon()
    {
        try
        {
            var cfg = _configService.Load();
            var icon = string.IsNullOrWhiteSpace(cfg.TrayIconPath)
                ? "ms-appx:///Assets/tray-normal.png"
                : cfg.TrayIconPath;
            if (_trayIcon is not null)
                _trayIcon.IconSource = new BitmapImage(new Uri(icon));
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"Tray icon sync: {ex.Message}");
        }
    }

    /// <summary>检测异常时切换托盘 Error 图标。</summary>
    public void UpdateTrayFromHealth(bool allHealthy)
    {
        try
        {
            var name = allHealthy ? "tray-normal.png" : "tray-error.png";
            if (_trayIcon is not null)
                _trayIcon.IconSource = new BitmapImage(new Uri($"ms-appx:///Assets/{name}"));
        }
        catch (Exception ex)
        {
            Logger.Instance.Warn($"Tray icon update: {ex.Message}");
        }
    }

    private void OnOpenWindowClick(object sender, RoutedEventArgs e) => ShowFromTray();

    private void OnToggleAutostartClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item) return;
        var target = !Vm.AutostartEnabled;
        Vm.AutostartEnabled = target;
        _autostart.SetEnabled(target);
        item.Text = target ? "开机自启动：开" : "开机自启动：关";
    }

    private void OnToggleShutdownReminderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item) return;
        Vm.ShutdownReminderEnabled = !Vm.ShutdownReminderEnabled;
        item.Text = Vm.ShutdownReminderEnabled ? "关机前登录提醒：开" : "关机前登录提醒：关";
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        _forceClose = true;
        try
        {
            _trayIcon?.Dispose();
            _shutdownMonitor?.Dispose();
        }
        catch
        {
        }
        Application.Current.Exit();
    }

    private void OnThemeComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo) Vm.OnThemeSelectionChanged(combo.SelectedIndex);
    }

    private bool _tokenSyncing;

    private void OnTokenPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_tokenSyncing) return;
        _tokenSyncing = true;
        Vm.BotToken = TokenBox.Password;
        _tokenSyncing = false;
    }

    private void OnTokenVisibilityClick(object sender, RoutedEventArgs e)
    {
        var show = Vm.TokenVisible;
        TokenBox.PasswordChar = show ? "" : "●";
    }

    /// <summary>
    /// 统一弹窗。cancelText=null 表示单按钮；
    /// treatCloseAsCancelShutdown=true 时 X/Esc 视为取消关机（返回 false）。
    /// </summary>
    private async Task<bool> ShowTextDialogAsync(
        string title,
        string body,
        string confirmText,
        string? cancelText,
        bool primaryIsCancel = false,
        bool treatCloseAsCancelShutdown = false)
    {
        var tcs = new TaskCompletionSource<bool>();

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = body,
            CloseButtonText = cancelText,
            PrimaryButtonText = confirmText,
            DefaultButton = primaryIsCancel ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };

        dialog.PrimaryButtonClick += (_, _) => tcs.TrySetResult(true);
        dialog.CloseButtonClick += (_, _) => tcs.TrySetResult(false);
        dialog.Closing += (_, _) =>
        {
            if (tcs.Task.IsCompleted) return;
            if (treatCloseAsCancelShutdown || cancelText is null)
                tcs.TrySetResult(false);
        };

        _ = dialog.ShowAsync();
        return await tcs.Task;
    }
}
