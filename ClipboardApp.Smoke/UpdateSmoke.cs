using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardApp;
using ClipboardApp.Services;
using Velopack;
using Velopack.NuGet;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

internal static class UpdateSmoke
{
    public static void Run(ProbeController controller, string root)
    {
        var updates = new ProbeUpdates();
        var panel = new QuickPanel(controller, updates);
        try
        {
            panel.ShowPanel();
            var update = (Button)panel.FindName("UpdateButton");
            var progress = (ProgressBar)panel.FindName("UpdateProgress");
            Check(update.Visibility == Visibility.Collapsed, "未知版本隱藏按鈕");
            updates.Set(new(AppUpdatePhase.Current));
            Check(update.Visibility == Visibility.Collapsed, "沒有更新隱藏按鈕");
            updates.Set(new(AppUpdatePhase.Available, "2.1.1"));
            Check(update.IsEnabled && update.Visibility == Visibility.Visible, "有新版才可按");
            panel.Activate(); update.Focus();
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(panel), 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            var beforeCopy = controller.Copied.Count;
            update.RaiseEvent(key);
            Check(!key.Handled && controller.Copied.Count == beforeCopy, "更新按鈕的 Enter 不誤複製");
            update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(updates.Downloads == 1, "點擊啟動更新");
            updates.Set(new(AppUpdatePhase.Downloading, "2.1.1", 45));
            Check(!update.IsEnabled && progress.Value == 45 && !progress.IsIndeterminate, "下载百分比與重複點擊保護");
            ((TextBox)panel.FindName("SearchBox")).Text = "工作";
            Check(((TextBox)panel.FindName("SearchBox")).IsEnabled, "下載時搜尋保持可用");
            panel.Hide(); updates.Set(new(AppUpdatePhase.Downloading, "2.1.1", 68)); panel.ShowPanel();
            Check(progress.Value == 68, "面板收起後下載進度持續");
            updates.Set(new(AppUpdatePhase.Downloading, "2.1.1"));
            Check(progress.IsIndeterminate, "未知長度不虛報百分比");
            ((Button)panel.FindName("CancelUpdateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(updates.Cancels == 1, "可取消下載");
            foreach (var theme in new[] { "dark", "light" })
            foreach (var scale in new[] { .75, 1.0, 1.75 })
            {
                controller.ChangeTheme(theme); controller.ChangeFontScale(scale);
                panel.Width = 360;
                updates.Set(new(AppUpdatePhase.Downloading, "2.1.1", 45));
                panel.UpdateLayout();
                var title = (TextBlock)panel.FindName("PanelTitle");
                var actions = (WrapPanel)panel.FindName("HeaderActions");
                var titleBounds = title.TransformToAncestor(panel).TransformBounds(new Rect(title.RenderSize));
                var actionsBounds = actions.TransformToAncestor(panel).TransformBounds(new Rect(actions.RenderSize));
                Check(!titleBounds.IntersectsWith(actionsBounds), $"{theme}/{scale} 窄視窗標題不重疊");
                Check(actionsBounds.Right <= panel.ActualWidth, $"{theme}/{scale} 按鈕不超出視窗");
                var bitmap = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(panel);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.Combine(root, "ui-preview"));
                using var file = File.Create(Path.Combine(root, "ui-preview", $"update-{theme}-{scale:0.00}.png"));
                encoder.Save(file);
            }
            updates.Set(new(AppUpdatePhase.Failed, "2.1.1", Error: "合成斷線情境"));
            Check(((Button)panel.FindName("RetryUpdateButton")).Visibility == Visibility.Visible, "失敗可重試");
            ((Button)panel.FindName("RetryUpdateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(updates.Downloads == 2, "重試重新下載");
        }
        finally { panel.AllowClose(); panel.Close(); controller.ChangeTheme("dark"); controller.ChangeFontScale(1); }
        RunAsync(CheckServiceAsync);
    }

    private static async Task CheckServiceAsync()
    {
        var manager = new FakeManager();
        var prepared = 0;
        using var service = new AppUpdateService(Dispatcher.CurrentDispatcher, (_, _, _) => { prepared++; return Task.CompletedTask; }, manager);
        await service.CheckAsync();
        Check(service.State.Phase == AppUpdatePhase.Current, "服務確認目前版本");
        manager.FailCheck = true;
        await service.CheckAsync();
        Check(service.State.Phase == AppUpdatePhase.Unknown, "斷線不冒充最新版");
        manager.FailCheck = false;
        manager.Target = new(new VelopackAsset { Version = SemanticVersion.Parse("2.2.0-beta.1") }, false);
        await service.CheckAsync();
        Check(service.State.Phase == AppUpdatePhase.Current, "忽略測試版");
        manager.Target = new(new VelopackAsset { Version = SemanticVersion.Parse("2.0.0") }, true);
        await service.CheckAsync();
        Check(service.State.Phase == AppUpdatePhase.Current, "禁止降版");
        manager.Target = new(new VelopackAsset { Version = SemanticVersion.Parse("2.1.1") }, false);
        await service.CheckAsync();
        manager.Wait = true;
        var pending = service.DownloadAsync();
        await Task.Delay(60);
        await service.DownloadAsync();
        Check(manager.Downloads == 1, "連點不產生第二個下載");
        service.Cancel(); await pending;
        Check(prepared == 0 && service.State.Phase == AppUpdatePhase.Available, "取消不進入替換");
        manager.Wait = false; manager.FailDownload = true;
        await service.DownloadAsync();
        Check(prepared == 0 && service.State.Phase == AppUpdatePhase.Failed, "下載失敗不進入替換");
        manager.FailDownload = false;
        await service.DownloadAsync();
        Check(prepared == 1, "重試成功才進入保存交接");
    }

    private static void RunAsync(Func<Task> run)
    {
        var frame = new DispatcherFrame();
        Exception? failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await run(); } catch (Exception ex) { failure = ex; }
            finally { frame.Continue = false; }
        });
        Dispatcher.PushFrame(frame);
        if (failure is not null) throw failure;
    }

    private static void Check(bool value, string label)
    { if (!value) throw new Exception("更新驗證失敗：" + label); Console.WriteLine("PASS " + label); }

    private sealed class ProbeUpdates : IAppUpdateService
    {
        public AppUpdateState State { get; private set; } = new(AppUpdatePhase.Unknown);
        public event EventHandler? Changed;
        public int Downloads, Cancels;
        public void Set(AppUpdateState state) { State = state; Changed?.Invoke(this, EventArgs.Empty); }
        public Task DownloadAsync() { Downloads++; return Task.CompletedTask; }
        public void Cancel() => Cancels++;
    }

    private sealed class FakeManager() : UpdateManager(Path.GetTempPath(), locator: new Velopack.Locators.WindowsVelopackLocator(null, null))
    {
        public bool FailCheck, Wait, FailDownload;
        public int Downloads;
        public UpdateInfo? Target;
        public override SemanticVersion? CurrentVersion => SemanticVersion.Parse("2.1.0");
        public override Task<UpdateInfo?> CheckForUpdatesAsync() => FailCheck
            ? throw new IOException("合成網路故障") : Task.FromResult(Target);
        public override async Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)
        {
            Downloads++;
            if (FailDownload) throw new IOException("合成損壞套件");
            progress?.Invoke(20);
            if (Wait) await Task.Delay(Timeout.Infinite, cancelToken);
            progress?.Invoke(100);
        }
    }
}
