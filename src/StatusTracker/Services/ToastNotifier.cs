using Windows.UI.Notifications;

namespace StatusTracker.Services;

/// <summary>
/// Windows 系统通知（右侧横幅）。用于开机发送成功/失败提示。
/// </summary>
public static class ToastNotifier
{
    public static void Show(string title, string message)
    {
        try
        {
            // ToastContentBuilder 来自 Microsoft.Toolkit.Uwp.Notifications（含在 WindowsAppSDK 依赖中不可用，
            // 改用原始 ToastNotification XML，避免额外包）
            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var texts = xml.GetElementsByTagName("text");
            if (texts.Length > 0) texts[0].AppendChild(xml.CreateTextNode(title));
            if (texts.Length > 1) texts[1].AppendChild(xml.CreateTextNode(message));

            var toast = new ToastNotification(xml);
            ToastNotificationManager.CreateToastNotifier("StatusTracker").Show(toast);
            Logger.Instance.Info($"Toast shown: {title} / {message}");
        }
        catch (Exception ex)
        {
            // 通知失败不影响主流程
            Logger.Instance.Warn($"Toast failed: {ex.Message}");
        }
    }

    public static void ShowBootResult(bool success, string detail)
        => Show(
            success ? "开机提醒发送成功" : "开机提醒发送失败",
            detail);
}
