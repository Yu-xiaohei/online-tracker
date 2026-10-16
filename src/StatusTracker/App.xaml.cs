using Microsoft.UI.Xaml;

namespace StatusTracker;

/// <summary>
/// 应用入口：静默启动到托盘，不弹主窗口。
/// 开机自启时主窗口保持隐藏，托盘常驻。
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        // 静默启动：不 Activate，托盘先就绪；用户左键托盘再开窗
        // 开发调试时仍可通过托盘打开
    }
}
