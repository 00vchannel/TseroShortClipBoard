#if UPDATE_TEST_BUILD
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ClipboardApp.Services;
using ClipboardCore;

namespace ClipboardApp;

// Compiled only for isolated package-to-package acceptance tests, never in a production release.
internal static class UpdateTestHarness
{
    internal static string? DataDirectory { get; private set; }
    internal static string? Feed { get; private set; }

    internal static void Configure()
    {
        var root = Environment.GetEnvironmentVariable("ZEROZERO_UPDATE_TEST_ROOT");
        Feed = Environment.GetEnvironmentVariable("ZEROZERO_UPDATE_TEST_FEED");
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) ||
            !File.Exists(Path.Combine(root, "isolated-update-test.marker")) || string.IsNullOrWhiteSpace(Feed) ||
            !Path.IsPathFullyQualified(Feed)) throw new InvalidOperationException("隔離測試入口缺少明確資料位置。");
        DataDirectory = Path.Combine(root, "data");
        var repository = new ClipboardRepository(DataDirectory);
        repository.Initialize();
        if (!repository.IsSetupComplete())
        {
            repository.CompleteSetup();
            repository.SaveSnippet(new Snippet { Title = "更新驗證", Content = "合成正式文字，不能被草稿取代" });
        }
    }

    internal static void RecordStartupFailure(Exception error)
    {
        if (DataDirectory is not null)
            File.WriteAllText(Path.Combine(DataDirectory, "acceptance-error.txt"), error.ToString());
    }

    internal static async void Drive(App app, AppUpdateService updates, MainWindow manager, QuickPanel panel,
        ClipboardRepository repository, bool resumed)
    {
        try
        {
            if (resumed)
            {
                var state = panel.CaptureResumeState();
                var draft = repository.ListDrafts().Single();
                if (state.Search != "更新" || !draft.Content.Contains("下載期間最後輸入")) throw new Exception("畫面或草稿未還原");
                UpdateJournal.WriteAtomic(Path.Combine(DataDirectory!, "acceptance.json"), new
                { Version = Assembly.GetExecutingAssembly().GetName().Version!.ToString(), Search = state.Search,
                    DraftPreserved = true, FormalPreserved = repository.ListSnippets().Single().Content == "合成正式文字，不能被草稿取代" });
                manager.AllowClose(); panel.AllowClose(); app.Shutdown();
                return;
            }
            panel.ShowPanel();
            ((System.Windows.Controls.TextBox)panel.FindName("SearchBox")).Text = "更新";
            var list = (System.Windows.Controls.ListBox)manager.FindName("SnippetList");
            list.SelectedIndex = 0;
            ((System.Windows.Controls.TextBox)manager.FindName("ContentBox")).Text = "下載期間最後輸入";
            await updates.CheckAsync();
            if (updates.State.Phase != AppUpdatePhase.Available) throw new Exception("沒有找到測試新版");
            await updates.DownloadAsync();
            if (updates.State.Phase == AppUpdatePhase.Failed) throw new Exception(updates.State.Error);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(DataDirectory!, "acceptance-error.txt"), ex.ToString());
            manager.AllowClose(); panel.AllowClose(); app.Shutdown(1);
        }
    }
}
#endif
