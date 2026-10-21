using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardApp;
using ClipboardApp.Services;
using ClipboardCore;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using MenuItem = System.Windows.Controls.MenuItem;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;

internal static class OfficialPromptsSmoke
{
    public static void Run(string root)
    {
        CheckCatalog();
        CheckStorage(root);
        var categoryId = Guid.NewGuid();
        var snippet = new SnippetView(Guid.NewGuid(), "個人 README", "", "個人內容", categoryId, 0, DateTimeOffset.MinValue);
        var categories = Enumerable.Range(0, 24).Select(i => new CategoryView(i == 0 ? categoryId : Guid.NewGuid(),
            i == 0 ? OfficialPrompts.CategoryName : $"個人分類 {i}", i)).ToArray();
        var snapshot = new ClipboardSnapshot(categories, [snippet], [], [], new Dictionary<Guid, DraftView>(), false);
        CheckManager(root, new ProbeController(snapshot), snippet);
        CheckPanel(root, new ProbeController(snapshot));
        var empty = new ProbeController(new ClipboardSnapshot([], [], [], [], new Dictionary<Guid, DraftView>(), false));
        var panel = new QuickPanel(empty);
        try
        {
            panel.ShowPanel();
            Click(panel, "OfficialCategoryButton");
            Check(Find<ListBox>(panel, "ResultList").Items.Count == 5, "沒有自訂分類仍可取用官方提示語");
        }
        finally { panel.AllowClose(); panel.Close(); }
        Console.WriteLine("Official prompts smoke passed (isolated data and simulated clipboard).");
    }

    private static void CheckCatalog()
    {
        var items = OfficialPrompts.Items;
        Check(items.Count == 5 && items.Select(s => s.Id).Append(OfficialPrompts.CategoryId).Distinct().Count() == 6,
            "五筆官方提示語有獨立且固定的識別碼");
        Check(items.Select(s => s.Title).SequenceEqual(new[] { "和 AI 一起討論計劃", "要求 AI 主動問你問題來完善計劃",
            "叫 AI 使用 Multi-agent 和建立Goal來實行計劃", "為專案建立 README", "為專案更新 README" }), "官方標題與順序");
        Check(items.Select(s => s.SortOrder).SequenceEqual(Enumerable.Range(0, 5)), "官方順序固定");
        Check(items[0].Content == "@READMEGameIntro\n先不要給我程式碼,我想要先討論和計劃一下如何以AI生成代碼為前提如何最好地實現這個功能。避免過度設計,並建議採用更專業的方式來設計和實現這個功能。然後像我12歲一樣來解釋它。另外你要事先分析和閱讀腳本或檔案讓你更好地理解遊戲的運作以更精準和專業的方式來建立這個功能。\n\n關於我的要求如果你實際上有什麼地方是含糊或不是很清楚，我命令你要主動問我問題來完善整個計畫，確保這是最專業的產品級計畫書。",
            "第一筆使用最後確認的正文及段落");
        Check(items.All(s => s.Content == s.Content.Trim() && !s.Content.Contains('\\')) &&
            items[3].Content.Contains("README().md") && items[2].Content.Contains("Multi-Agent"), "保留提示語字面內容並移除排版符號");
    }

    private static void CheckStorage(string root)
    {
        var directory = Path.Combine(root, "ClipboardCore.Tests", ".test-output", "official-" + Guid.NewGuid().ToString("N"));
        var repository = new ClipboardRepository(directory);
        repository.Initialize();
        repository.CompleteSetup();
        var category = repository.AddCategory(OfficialPrompts.CategoryName);
        var personal = repository.SaveSnippet(new Snippet { Title = "個人", Content = "測試", CategoryId = category.Id });
        var controller = new ClipboardController(repository, directory);
        var before = repository.CaptureUpdateFingerprint();
        var reader = new MainWindow(controller);
        try
        {
            reader.Show();
            var choices = Find<ComboBox>(reader, "CategoryList");
            choices.SelectedItem = choices.Items.Cast<MainWindow.CategoryChoice>().Single(c => c.Id == OfficialPrompts.CategoryId);
            Find<ListBox>(reader, "SnippetList").SelectedIndex = 0;
            Find<TextBox>(reader, "SearchBox").Text = "README";
            Check(reader.TryPrepareToClose(), "真實資料層閱讀官方提示語不產生待存草稿");
        }
        finally { reader.AllowClose(); reader.Close(); }
        ClipboardRepository.RequireMatchingFingerprints(before, repository.CaptureUpdateFingerprint());
        var official = OfficialPrompts.Items[0].Id;
        var officialCategory = OfficialPrompts.CategoryId;
        var rejected = new Action[]
        {
            () => controller.SaveSnippet(official, "改", "", "改", null),
            () => controller.SaveSnippet(null, "改", "", "改", officialCategory),
            () => controller.SaveDraft(official, "改", "", "改", null),
            () => controller.SaveDraft(personal.Id, "改", "", "改", officialCategory),
            () => controller.DiscardDraft(official),
            () => controller.DeleteSnippets([personal.Id, official]),
            () => controller.RestoreSnippet(official),
            () => controller.PermanentlyDeleteSnippets([personal.Id, official]),
            () => controller.MoveSnippets([personal.Id], officialCategory),
            () => controller.MoveSnippets([personal.Id, official], null),
            () => controller.ReorderSnippets(officialCategory, [official]),
            () => controller.ReorderSnippets(category.Id, [personal.Id, official]),
            () => controller.RenameCategory(officialCategory, "改"),
            () => controller.DeleteCategory(officialCategory),
            () => controller.ReorderCategories([category.Id, officialCategory])
        };
        foreach (var action in rejected)
        {
            var refused = false;
            try { action(); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("內建唯讀")) { refused = true; }
            Check(refused, "控制器阻止官方寫入且批次操作不部分提交");
        }
        var snapshot = controller.GetSnapshot();
        Check(snapshot.Categories.Count == 1 && snapshot.Snippets.Count == 1 && snapshot.Drafts.Count == 0 && snapshot.Trash.Count == 0,
            "官方內容不混入資料庫與個人快照，同名個人分類保留");
        ClipboardRepository.RequireMatchingFingerprints(before, repository.CaptureUpdateFingerprint());
        var backup = Path.Combine(directory, "before.sqlite");
        repository.CreateBackup(backup);
        controller.SaveSnippet(personal.Id, "個人可修改", "", "修改正文", category.Id);
        controller.RestoreBackup(backup);
        ClipboardRepository.RequireMatchingFingerprints(before, repository.CaptureUpdateFingerprint());
        Check(OfficialPrompts.Items.Count == 5 && controller.GetSnapshot().Snippets.Single().Title == "個人", "還原備份保留官方內容並還原個人內容");
    }

    private static void CheckManager(string root, ProbeController controller, SnippetView personal)
    {
        var window = new MainWindow(controller);
        try
        {
            window.Show();
            var categories = Find<ComboBox>(window, "CategoryList");
            var rows = Find<ListBox>(window, "SnippetList");
            var search = Find<TextBox>(window, "SearchBox");
            var content = Find<TextBox>(window, "ContentBox");
            var officialChoice = categories.Items.Cast<MainWindow.CategoryChoice>().Last();
            Check(officialChoice.Id == OfficialPrompts.CategoryId && officialChoice.Name.Contains("內建唯讀"), "管理分類最後加入內建唯讀分類");
            rows.SelectedIndex = 0;
            content.Text = "個人待存草稿";
            categories.SelectedItem = officialChoice;
            Check(controller.SavedDrafts.Single().Content == "個人待存草稿", "切入官方分類前保存個人草稿");
            Check(rows.Items.Count == 5, "管理視窗顯示五筆官方提示語");
            Invoke(window, "RefreshSnapshot");
            Check((categories.SelectedItem as MainWindow.CategoryChoice)?.Id == OfficialPrompts.CategoryId,
                "資料刷新保留官方分類選取");
            foreach (var item in OfficialPrompts.Items)
            {
                rows.SelectedItem = item;
                Check(content.IsReadOnly && content.Text == item.Content && Find<TextBox>(window, "TitleBox").IsReadOnly,
                    "完整唯讀正文可閱讀");
                Click(window, "CopyOfficialButton");
                Check(controller.Copied.Last() == item.Content && window.IsVisible, "管理複製全文不附加標題且保留視窗");
            }
            Check(!Find<Button>(window, "SaveButton").IsEnabled &&
                Find<Button>(window, "NewSnippetButton").Visibility == Visibility.Collapsed &&
                !Find<MenuItem>(window, "RenameCategoryAction").IsEnabled &&
                !Find<MenuItem>(window, "DeleteCategoryAction").IsEnabled &&
                !Find<MenuItem>(window, "MoveCategoryUpAction").IsEnabled &&
                !Find<MenuItem>(window, "MoveCategoryDownAction").IsEnabled, "官方編輯與分類變更操作不可用");
            Check(Find<ComboBox>(window, "EditorCategory").Items.Cast<MainWindow.CategoryChoice>().All(c => c.Id != OfficialPrompts.CategoryId),
                "個人文字編輯目的地不含官方分類");
            foreach (var handler in new[] { "Save_Click", "NewSnippet_Click", "DeleteSelected_Click", "MoveSelected_Click",
                         "RenameCategory_Click", "DeleteCategory_Click", "MoveCategoryUp_Click", "MoveCategoryDown_Click" })
                Invoke(window, handler, window, new RoutedEventArgs());
            Check(window.TryPrepareToClose() && controller.SavedDrafts.Count == 1 && controller.DeletedIds.Count == 0 &&
                controller.LastSavedEmoji is null && controller.LastOrderedIds is null, "直接呼叫操作入口也不改官方內容、不產生草稿");
            controller.FailCopy = true;
            Click(window, "CopyOfficialButton");
            Check(Find<TextBlock>(window, "StatusText").Text.Contains("重試") && window.IsVisible, "管理複製失敗可重試");
            controller.FailCopy = false;
            search.Text = "README";
            Check(rows.Items.Count == 3 && rows.Items.Cast<SnippetView>().All(s => OfficialPrompts.IsOfficial(s.Id)), "官方搜尋只列官方結果");
            search.Text = "沒有這個字";
            Check(rows.Items.Count == 0, "官方搜尋可為空結果");
            search.Clear();
            rows.SelectedIndex = 0;
            foreach (var theme in new[] { "dark", "light" })
            foreach (var scale in new[] { .75, 1.0, 1.75 })
            {
                controller.ChangeTheme(theme);
                controller.ChangeFontScale(scale);
                window.Width = 780;
                window.Height = 740;
                window.UpdateLayout();
                CheckRowsEqual(rows);
                var copy = Find<Button>(window, "CopyOfficialButton");
                Check(IsInside(copy, window), "管理視窗複製按鈕在可見範圍");
                Render(window, Path.Combine(root, "ui-preview", $"official-manager-{theme}-{scale:0.00}.png"));
            }
            controller.ChangeFontScale(1);
            categories.SelectedItem = categories.Items.Cast<MainWindow.CategoryChoice>().First(c => c.Id is null);
            search.Text = "README";
            Check(rows.Items.Count == 1 && rows.Items.Cast<SnippetView>().Single().Id == personal.Id, "所有分類搜尋不混入官方提示語");
            search.Clear();
            rows.SelectedIndex = 0;
            Check(!content.IsReadOnly && Find<Button>(window, "SaveButton").IsEnabled, "返回個人文字恢復編輯");
            content.Text = "保存失敗時仍需保留";
            controller.FailDraftSave = true;
            var dismissed = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Title.StartsWith("草稿保存失敗"));
                if (dialog is null) return;
                dismissed = true;
                dialog.Close();
            };
            timer.Start();
            try { categories.SelectedItem = officialChoice; }
            finally { timer.Stop(); controller.FailDraftSave = false; }
            Check(dismissed && (categories.SelectedItem as MainWindow.CategoryChoice)?.Id != OfficialPrompts.CategoryId &&
                content.Text == "保存失敗時仍需保留", "草稿失敗阻止切入官方分類且保留編輯內容");
        }
        finally { controller.FailDraftSave = false; window.AllowClose(); window.Close(); }
    }

    private static void CheckPanel(string root, ProbeController controller)
    {
        var window = new QuickPanel(controller);
        try
        {
            window.ShowPanel();
            var rows = Find<ListBox>(window, "ResultList");
            var search = Find<TextBox>(window, "SearchBox");
            var button = Find<Button>(window, "OfficialCategoryButton");
            Click(window, "OfficialCategoryButton");
            Check(rows.Items.Count == 5, "快捷面板顯示五筆官方提示語");
            search.Text = "README";
            Check(rows.Items.Count == 3, "快捷面板官方搜尋不混入個人文字");
            var state = window.CaptureResumeState();
            window.Hide();
            window.ShowAfterUpdate(state);
            Check(window.CaptureResumeState() == state && rows.Items.Count == 3, "更新重開恢復官方分類、搜尋及選取");
            search.Clear();
            window.UpdateLayout();
            var keyboardSource = PresentationSource.FromVisual(window)!;
            typeof(QuickPanel).GetField("composing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, keyboardSource, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(controller.Copied.Count == 0 && window.IsVisible, "模擬中文組字中 Enter 不複製");
            typeof(QuickPanel).GetField("composing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
            controller.FailCopy = true;
            Invoke(window, "CopySelected");
            Check(window.IsVisible && Find<TextBlock>(window, "StatusText").Text.Contains("重試"), "快捷複製失敗保留面板並提示重試");
            controller.FailCopy = false;
            search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, keyboardSource, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(!window.IsVisible && controller.Copied.Last() == OfficialPrompts.Items[0].Content, "Enter 複製官方全文並收起");
            window.ShowPanel();
            Check(window.CaptureResumeState().CategoryId != OfficialPrompts.CategoryId, "一般手動開啟維持原有預設分類");
            Click(window, "OfficialCategoryButton");
            foreach (var theme in new[] { "dark", "light" })
            foreach (var scale in new[] { .75, 1.0, 1.75 })
            {
                controller.ChangeTheme(theme);
                controller.ChangeFontScale(scale);
                window.Width = 360;
                window.Height = 760;
                window.UpdateLayout();
                var scroller = Find<ScrollViewer>(window, "CategoryScroller");
                var before = button.TranslatePoint(new Point(), window);
                scroller.ScrollToEnd();
                window.UpdateLayout();
                Check(button.TranslatePoint(new Point(), window) == before && IsInside(button, window), "窄面板大量分類捲動時官方入口固定且可見");
                Check(scroller.TranslatePoint(new Point(scroller.ActualWidth, 0), window).X <= before.X, "個人分類不覆蓋官方入口");
                CheckRowsEqual(rows);
                foreach (var item in rows.Items)
                    Check(!(bool)item.GetType().GetProperty("CanReorder")!.GetValue(item)!, "官方列無法排序");
                Render(window, Path.Combine(root, "ui-preview", $"official-panel-{theme}-{scale:0.00}.png"));
            }
            var selected = rows.SelectedItem!;
            Invoke(window, "ReorderDroppedSnippet", OfficialPrompts.Items[1].Id, selected);
            Check(controller.LastOrderedIds is null, "官方拖曳入口不提交排序");
            Click(window, "HistoryTab");
            Check(Find<Grid>(window, "CategoryArea").Visibility == Visibility.Collapsed, "歷史模式隱藏整個分類區");
            Click(window, "SnippetsTab");
            Check(Find<Grid>(window, "CategoryArea").Visibility == Visibility.Visible, "返回常用文字恢復分類區");
            search.Clear();
            rows.SelectedIndex = 4;
            rows.ScrollIntoView(rows.SelectedItem);
            window.UpdateLayout();
            var row = (ListBoxItem)rows.ItemContainerGenerator.ContainerFromIndex(4);
            var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent };
            row.RaiseEvent(mouse);
            // Synthetic button-specific preview events do not perform WPF's native mouse-event promotion.
            Invoke(window, "ResultList_PreviewMouseLeftButtonUp", rows, mouse);
            Check(!window.IsVisible && controller.Copied.Last() == OfficialPrompts.Items[4].Content,
                "滑鼠點官方文字列完整複製第五筆並收起");
        }
        finally { window.AllowClose(); window.Close(); }
    }

    private static bool IsInside(FrameworkElement element, Window window)
    {
        var point = element.TranslatePoint(new Point(), window);
        return element.IsVisible && point.X >= 0 && point.Y >= 0 && point.X + element.ActualWidth <= window.ActualWidth &&
            point.Y + element.ActualHeight <= window.ActualHeight;
    }

    private static void CheckRowsEqual(ListBox rows)
    {
        rows.UpdateLayout();
        var containers = rows.Items.Cast<object>().Select(i => rows.ItemContainerGenerator.ContainerFromItem(i))
            .OfType<ListBoxItem>().ToArray();
        Check(containers.Length >= 2 && containers.All(c => Math.Abs(c.ActualHeight - containers[0].ActualHeight) < .1), "長短官方提示語列高一致");
    }

    private static T Find<T>(Window window, string name) where T : class => (T)window.FindName(name);
    private static void Click(Window window, string name) => Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("Official prompts: " + label);
        Console.WriteLine("PASS " + label);
    }
    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        png.Save(stream);
    }
}
