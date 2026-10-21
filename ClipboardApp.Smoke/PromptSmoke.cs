using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClipboardApp;
using ClipboardApp.Services;
using ClipboardApp.Styles;
using Application = System.Windows.Application;

internal static class PromptSmoke
{
    internal static void Run(ProbeController controller)
    {
        var manager = new MainWindow(controller);
        try
        {
            foreach (var theme in new[] { "dark", "light" })
            {
                UiPreferences.Apply(manager, new ClipboardSettings("right alt", false, 1.75, theme, ""));
                NotifyAndDismiss(manager, expectsOwner: false, theme, "主視窗尚未顯示");
            }
            controller.ChangeTheme("light");
            controller.ChangeFontScale(1.75);
            manager.Show();
            NotifyAndDismiss(manager, expectsOwner: true, "light", "主視窗已顯示");
            manager.Hide();
            NotifyAndDismiss(manager, expectsOwner: false, "light", "主視窗已收起");
            manager.AllowClose(); manager.Close();
            NotifyAndDismiss(manager, expectsOwner: false, "light", "主視窗已關閉");
        }
        finally { if (manager.IsVisible) { manager.AllowClose(); manager.Close(); } }
    }

    private static void NotifyAndDismiss(Window owner, bool expectsOwner, string theme, string scenario)
    {
        var seen = false;
        Exception? failure = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "啟動提示驗證");
            if (dialog is null) return;
            try
            {
                if (!dialog.IsVisible || (expectsOwner ? dialog.Owner != owner : dialog.Owner is not null))
                    throw new Exception(scenario + "：提示歸屬或顯示狀態錯誤");
                if (!expectsOwner && (!dialog.ShowInTaskbar || dialog.WindowStartupLocation != WindowStartupLocation.CenterScreen))
                    throw new Exception(scenario + "：獨立提示無法由工作列找到");
                var brush = (SolidColorBrush)dialog.FindResource("PageBrush");
                if ((brush.Color.R > 128) != (theme == "light") || Math.Abs(dialog.FontSize - 24.5) > .01)
                    throw new Exception(scenario + "：主題或文字縮放未保留");
                seen = true;
            }
            catch (Exception ex) { failure = ex; }
            finally { timer.Stop(); dialog.DialogResult = true; }
        };
        timer.Start();
        try { UiPrompt.Notify(owner, "啟動提示驗證", "合成提醒，不應造成啟動中斷。"); }
        finally { timer.Stop(); }
        if (failure is not null) throw failure;
        if (!seen) throw new Exception(scenario + "：提示沒有出現");
        Console.WriteLine($"PASS 提示視窗：{scenario}／{theme}");
    }
}
