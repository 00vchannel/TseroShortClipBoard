using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardApp;
using ClipboardApp.Services;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using TextBox = System.Windows.Controls.TextBox;

Exception? failure = null;
var uiThread = new Thread(() =>
{
    try { Run(); }
    catch (Exception ex) { failure = ex; }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
uiThread.Join();
if (failure is not null) { Console.Error.WriteLine(failure); Environment.Exit(1); }

static void Run()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    var firstId = Guid.NewGuid();
    var secondId = Guid.NewGuid();
    var snippets = new[]
    {
        new SnippetView(Guid.NewGuid(), "工作一", "", "第一筆合成內容", firstId, 0, DateTimeOffset.UtcNow),
        new SnippetView(Guid.NewGuid(), "工作二", "", "第二筆合成內容", firstId, 1, DateTimeOffset.UtcNow),
        new SnippetView(Guid.NewGuid(), "工作三" + new string('長', 80), "",
            "第三筆合成內容" + new string('長', 180) + "\n" + new string('後', 180), firstId, 2, DateTimeOffset.UtcNow),
        new SnippetView(Guid.NewGuid(), "生活一", "", "其他分類合成內容", secondId, 3, DateTimeOffset.UtcNow),
        new SnippetView(Guid.NewGuid(), "未分類一", "", "未分類合成內容", null, 4, DateTimeOffset.UtcNow)
    };
    var history = new[] { new HistoryView(Guid.NewGuid(), "歷史合成內容", DateTimeOffset.UtcNow) };
    var controller = new ProbeController(new ClipboardSnapshot(
        [new CategoryView(firstId, "工作", 0), new CategoryView(secondId, "生活", 1)],
        snippets, history, [], new Dictionary<Guid, DraftView>(), false));
    var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    CheckFirstStartup(root, application);
    var prompt = typeof(MainWindow).Assembly.GetType("ClipboardApp.UiPrompt")
        ?? throw new Exception("找不到共用對話框");
    var makeDialog = prompt.GetMethod("MakeDialog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new Exception("找不到風格化對話框建立方法");
    var sampleDialog = (Window)(makeDialog.Invoke(null, [null, "風格檢查"])
        ?? throw new Exception("無法建立風格化對話框"));
    Check(sampleDialog.FindResource("AccentBrush") is SolidColorBrush && sampleDialog.WindowStyle == WindowStyle.None,
        "確認視窗套用共用主題");
    sampleDialog.Close();
    var settingsWindow = new SettingsWindow(controller);
    try
    {
        settingsWindow.Show();
        settingsWindow.UpdateLayout();
        Check(settingsWindow.FindResource(typeof(System.Windows.Controls.CheckBox)) is Style &&
              settingsWindow.FindResource(typeof(System.Windows.Controls.Slider)) is Style,
            "設定視窗使用一致的勾選與滑桿樣式");
        Render(settingsWindow, Path.Combine(root, "ui-preview", "settings-ui-fixes.png"));
        var confirmMethod = prompt.GetMethod("Confirm", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?? throw new Exception("找不到共用確認視窗");
        foreach (var theme in new[] { "dark", "light" })
        {
            controller.ChangeTheme(theme);
            var captured = false;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) =>
            {
                var confirmation = application.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "刪除文字");
                if (confirmation is null) return;
                Render(confirmation, Path.Combine(root, "ui-preview", $"delete-confirm-{theme}.png"));
                captured = true;
                timer.Stop();
                confirmation.DialogResult = false;
            };
            timer.Start();
            var accepted = (bool)(confirmMethod.Invoke(null, [settingsWindow, "刪除文字", "移至垃圾桶後仍可復原。", "移至垃圾桶"])
                ?? throw new Exception("確認視窗沒有結果"));
            Check(captured && !accepted, $"{theme} 主題確認視窗可顯示並取消");
        }
        controller.ChangeTheme("dark");
    }
    finally { settingsWindow.Close(); }
    controller.AutostartReadable = false;
    var restrictedSettings = new SettingsWindow(controller);
    try
    {
        Check(!((System.Windows.Controls.CheckBox)restrictedSettings.FindName("AutostartCheck")!).IsEnabled &&
            ((TextBlock)restrictedSettings.FindName("AutostartStatusText")!).Text.Contains("無法讀取"), "無權讀取啟動設定時顯示未知狀態");
    }
    finally { restrictedSettings.Close(); controller.AutostartReadable = true; }
    controller.ChangeFontScale(1.75);
    var largeSettings = new SettingsWindow(controller);
    try
    {
        largeSettings.Show();
        largeSettings.UpdateLayout();
        var label = FindVisual<TextBlock>(largeSettings, text => text.Text == "開機時自動啟動")!;
        Check(Math.Abs(label.FontSize - 24.5) < 0.1, "首次以 175% 開啟時繼承文字只縮放一次");
        Check(largeSettings.MinWidth <= 540 && largeSettings.Content is not null, "設定視窗保留可捲動內容");
        foreach (var theme in new[] { "dark", "light" })
        {
            controller.ChangeTheme(theme);
            Render(largeSettings, Path.Combine(root, "ui-preview", $"settings-175-{theme}.png"));
            RespondToDialog(application, "放大確認", false, () =>
            {
                var confirm = prompt.GetMethod("Confirm")!;
                confirm.Invoke(null, [largeSettings, "放大確認", "放大文字後仍可取消。", "確定"]);
            });
        }
    }
    finally { largeSettings.Close(); controller.ChangeFontScale(1); controller.ChangeTheme("dark"); }
    var failedSaveManager = new MainWindow(controller);
    try
    {
        failedSaveManager.Show();
        failedSaveManager.UpdateLayout();
        var newButton = (Button)failedSaveManager.FindName("NewSnippetButton")!;
        newButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var content = (TextBox)failedSaveManager.FindName("ContentBox")!;
        content.Text = "儲存失敗後仍須保留的合成內容";
        controller.FailSnippetSave = true;
        RespondToDialog(application, "儲存失敗", false,
            () => ((Button)failedSaveManager.FindName("SaveButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        controller.FailSnippetSave = false;
        Check(failedSaveManager.TryPrepareToClose() && controller.SavedDrafts.Last().Content == content.Text,
            "正式儲存失敗後仍可保存草稿");
        content.Text = "草稿失敗時不能離開的合成內容";
        controller.FailDraftSave = true;
        var switched = false;
        RespondToDialog(application, "草稿保存失敗，請保留目前視窗並重試", false,
            () => { switched = failedSaveManager.TryPrepareToClose(); });
        Check(!switched && content.Text == "草稿失敗時不能離開的合成內容", "草稿失敗阻止關閉並保留編輯文字");
        RespondToDialog(application, "草稿保存失敗，請保留目前視窗並重試", false,
            () => ((ComboBox)failedSaveManager.FindName("ViewSelector")!).SelectedIndex = 1);
        Check(((ComboBox)failedSaveManager.FindName("ViewSelector")!).SelectedIndex == 0 && content.Text.Contains("不能離開"),
            "草稿失敗阻止切換檢視");
        controller.FailDraftSave = false;
        Check(failedSaveManager.TryPrepareToClose() && controller.SavedDrafts.Last().Content == content.Text, "恢復可寫入後能重試保存");
    }
    finally
    {
        controller.FailDraftSave = false;
        controller.FailSnippetSave = false;
        failedSaveManager.AllowClose();
        failedSaveManager.Close();
    }
    var managerUi = new MainWindow(controller);
    try
    {
        managerUi.Show();
        managerUi.UpdateLayout();
        var view = managerUi.FindName("ViewSelector") as ComboBox ?? throw new Exception("找不到檢視選單");
        Check(view.DisplayMemberPath == "Name" && view.Items.Count == 4, "檢視選單使用中文名稱");
        var managerRows = managerUi.FindName("SnippetList") as ListBox ?? throw new Exception("找不到管理文字列表");
        var firstManagerRow = managerRows.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem
            ?? throw new Exception("找不到常用文字列");
        var rowDelete = FindVisual<Button>(firstManagerRow, button => Equals(button.ToolTip, "移至垃圾桶（可復原）"))
            ?? throw new Exception("找不到每列刪除入口");
        Check(rowDelete.Visibility == Visibility.Visible, "常用文字顯示可復原刪除入口");
        RespondToDialog(application, "刪除文字", false,
            () => rowDelete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Check(controller.DeletedIds.Count == 0, "取消每列刪除不更動資料");
        RespondToDialog(application, "刪除文字", true,
            () => rowDelete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Check(controller.DeletedIds.SequenceEqual(new[] { snippets[0].Id }), "確認每列刪除只處理該筆文字");
        var categoryButton = FindVisual<Button>(managerUi, button => Equals(button.Content, "分類 ▾"))
            ?? throw new Exception("找不到分類選單");
        foreach (var theme in new[] { "dark", "light" })
        {
            controller.ChangeTheme(theme);
            var menu = categoryButton.ContextMenu ?? throw new Exception("找不到分類選項");
            menu.PlacementTarget = categoryButton;
            menu.IsOpen = true;
            menu.UpdateLayout();
            RenderElement(menu, Path.Combine(root, "ui-preview", $"category-menu-{theme}.png"));
            menu.IsOpen = false;
        }
        controller.ChangeTheme("dark");
    }
    finally { managerUi.AllowClose(); managerUi.Close(); }
    var newDraftId = Guid.NewGuid();
    var draftController = new ProbeController(new ClipboardSnapshot(
        [new CategoryView(firstId, "工作", 0)],
        [snippets[0] with { Emoji = "🙂" }], [], [],
        new Dictionary<Guid, DraftView>
        {
            [snippets[0].Id] = new(snippets[0].Id, "已建立文字的修改", "🙂", "修改內容", null, DateTimeOffset.UtcNow),
            [newDraftId] = new(newDraftId, "新建草稿", "", "新內容", firstId, DateTimeOffset.UtcNow)
        }, false));
    var draftManager = new MainWindow(draftController);
    try
    {
        draftManager.Show();
        var draftView = draftManager.FindName("ViewSelector") as ComboBox ?? throw new Exception("找不到草稿檢視");
        draftView.SelectedIndex = 2;
        var draftRows = draftManager.FindName("SnippetList") as ListBox ?? throw new Exception("找不到草稿列表");
        Check(draftRows.Items.Count == 2, "草稿檢視同時列出新建與既有文字修改");
        var existingDraft = draftRows.Items.Cast<SnippetView>().Single(s => s.Id == snippets[0].Id);
        draftRows.SelectedItem = existingDraft;
        Check(((ComboBox)draftManager.FindName("EditorCategory")!).SelectedIndex == 0, "草稿改為未分類時不回到原本分類");
        var save = draftManager.FindName("SaveButton") as Button ?? throw new Exception("找不到儲存按鈕");
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(draftController.LastSavedEmoji == "🙂", "儲存既有修改時保留隱藏符號資料");
    }
    finally { draftManager.AllowClose(); draftManager.Close(); }
    CheckReorderDirection(snippets);
    var panel = new QuickPanel(controller);
    try
    {
        panel.ShowPanel();
        panel.UpdateLayout();
        var chips = panel.FindName("CategoryChips") as WrapPanel
            ?? throw new Exception("找不到分類標籤列");
        var results = panel.FindName("ResultList") as ListBox
            ?? throw new Exception("找不到文字列表");
        var search = panel.FindName("SearchBox") as TextBox
            ?? throw new Exception("找不到搜尋欄");
        var buttons = chips.Children.OfType<Button>().ToArray();
        Check(buttons.Select(b => b.Content).SequenceEqual(new object[] { "工作", "生活" }), "只顯示自訂分類");
        Check(results.Items.Count == 3, "開啟時預設第一個分類");
        Check(panel.FindResource(typeof(ScrollBar)) is Style, "快捷面板使用自訂捲軸樣式");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth),
            (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(panel);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var preview = Path.Combine(root, "ui-preview", "quick-panel-categories.png");
        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
        using (var output = File.Create(preview)) encoder.Save(output);
        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(results.Items.Count == 1, "自訂分類可直接切換");
        search.Text = "未分類一";
        Check(results.Items.Count == 1, "搜尋能找到未分類文字");
        search.Text = "工作一";
        Check(results.Items.Count == 1, "搜尋跨越目前分類");
        panel.UpdateLayout();
        var filteredRow = results.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem
            ?? throw new Exception("找不到搜尋結果列");
        var filteredHandle = FindVisual<Border>(filteredRow, element => element.Name == "DragHandle")
            ?? throw new Exception("找不到搜尋結果拖曳符號");
        Check(filteredHandle.Visibility == Visibility.Collapsed, "搜尋時不顯示拖曳符號");
        search.Clear();
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        panel.UpdateLayout();
        var firstRow = results.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem
            ?? throw new Exception("找不到常用文字列");
        var secondRow = results.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem
            ?? throw new Exception("找不到第二筆常用文字列");
        var thirdRow = results.ItemContainerGenerator.ContainerFromIndex(2) as ListBoxItem
            ?? throw new Exception("找不到第三筆常用文字列");
        var secondHandle = FindVisual<Border>(secondRow, element => element.Name == "DragHandle")
            ?? throw new Exception("找不到第二筆拖曳符號");
        Check(Math.Abs(firstRow.ActualHeight - thirdRow.ActualHeight) < 0.1 &&
              Math.Abs(firstRow.ActualHeight - 72) < 0.1, "快捷面板長文字不會撐高文字列");
        controller.ChangeFontScale(1.75);
        panel.UpdateLayout();
        Check(Math.Abs(firstRow.ActualHeight - thirdRow.ActualHeight) < 0.1 &&
              Math.Abs(firstRow.ActualHeight - 126) < 0.1, "快捷面板放大文字後仍維持等高列");
        controller.ChangeFontScale(1);
        panel.UpdateLayout();
        Check(secondHandle.IsEnabled && secondHandle.Visibility == Visibility.Visible,
            "快捷面板第二筆可從把手開始拖曳");
        var secondHandleCenter = secondHandle.TranslatePoint(
            new System.Windows.Point(secondHandle.ActualWidth / 2, secondHandle.ActualHeight / 2), results);
        var hitRow = FindParent<ListBoxItem>(results.InputHitTest(secondHandleCenter) as DependencyObject);
        Check(ReferenceEquals(hitRow, secondRow), "快捷面板第二筆把手可被滑鼠命中");
        InvokeMouseHandler(panel, "DragHandle_PreviewMouseLeftButtonDown", secondHandle, UIElement.PreviewMouseLeftButtonDownEvent);
        Check((Guid?)typeof(QuickPanel).GetField("pendingDragId",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(panel) == snippets[1].Id,
            "快捷面板第二筆把手會選定正確文字");
        var start = (System.Windows.Point)(typeof(QuickPanel).GetField("dragStart",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(panel)
            ?? throw new Exception("找不到拖曳起點"));
        typeof(QuickPanel).GetMethod("UpdatePendingDrag",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(panel,
                [new System.Windows.Point(start.X + 20, start.Y), true]);
        Check(secondRow.Opacity < 1 && AdornerLayer.GetAdornerLayer(results)?.GetAdorners(results)?.Length == 1,
            "快捷面板第二筆可顯示整列拖曳預覽");
        var secondAdorner = AdornerLayer.GetAdornerLayer(results)!.GetAdorners(results)![0];
        var previewBorder = secondAdorner.GetType().GetField("preview",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(secondAdorner) as Border;
        var previewBitmap = (previewBorder?.Child as System.Windows.Controls.Image)?.Source as BitmapSource;
        if (previewBitmap is not null)
        {
            var pixels = new byte[previewBitmap.PixelWidth * previewBitmap.PixelHeight * 4];
            previewBitmap.CopyPixels(pixels, previewBitmap.PixelWidth * 4, 0);
            Check(Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 3] != 0),
                "快捷面板第二筆預覽影像不能是空白");
        }
        secondAdorner.GetType().GetProperty("Position")?.SetValue(secondAdorner,
            new System.Windows.Point(18, secondRow.TranslatePoint(new System.Windows.Point(), results).Y + 30));
        AdornerLayer.GetAdornerLayer(results)!.Update(results);
        Render(panel, Path.Combine(root, "ui-preview", "quick-panel-second-row-drag-preview.png"));
        var updateTarget = typeof(QuickPanel).GetMethod("UpdateDropTarget",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new Exception("找不到拖曳落點提示動作");
        var firstPoint = firstRow.TranslatePoint(new System.Windows.Point(70, firstRow.ActualHeight / 2), results);
        updateTarget.Invoke(panel, [firstPoint]);
        var firstCue = AdornerLayer.GetAdornerLayer(firstRow)?.GetAdorners(firstRow);
        Check(firstCue?.Length == 1, "拖曳至第一筆時會標示落點");
        Check(ReferenceEquals(FindParent<ListBoxItem>(results.InputHitTest(firstPoint) as DependencyObject), firstRow),
            "落點提示不會阻擋放開時的目標判定");
        Render(panel, Path.Combine(root, "ui-preview", "quick-panel-drop-target.png"));
        var thirdPoint = thirdRow.TranslatePoint(new System.Windows.Point(70, thirdRow.ActualHeight / 2), results);
        updateTarget.Invoke(panel, [thirdPoint]);
        Check(AdornerLayer.GetAdornerLayer(firstRow)?.GetAdorners(firstRow) is null &&
              AdornerLayer.GetAdornerLayer(thirdRow)?.GetAdorners(thirdRow)?.Length == 1,
            "落點提示會移到目前指向的列");
        typeof(QuickPanel).GetMethod("CancelDrag",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(panel, null);
        Check(secondRow.Opacity == 1 && AdornerLayer.GetAdornerLayer(results)?.GetAdorners(results) is null,
            "快捷面板第二筆放開後還原");
        Check(AdornerLayer.GetAdornerLayer(thirdRow)?.GetAdorners(thirdRow) is null,
            "拖曳結束後清除落點提示");
        CheckDragPreview(panel, results, thirdRow, "快捷面板第三筆",
            Path.Combine(root, "ui-preview", "quick-panel-third-row-drag-preview.png"));
        CheckDragPreview(panel, results, firstRow, "快捷面板",
            Path.Combine(root, "ui-preview", "quick-panel-drag-preview.png"));
        var reorderDrop = typeof(QuickPanel).GetMethod("ReorderDroppedSnippet",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new Exception("找不到快捷面板換位動作");
        reorderDrop.Invoke(panel, [snippets[0].Id, results.Items[1]]);
        Check(controller.LastOrderedIds?.SequenceEqual([snippets[1].Id, snippets[0].Id, snippets[2].Id]) == true,
            "快捷面板第一筆放到第二筆時會保存換位");
        panel.UpdateLayout();
        firstRow = results.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem
            ?? throw new Exception("換位後找不到常用文字列");
        var handle = FindVisual<Border>(firstRow, element => element.Name == "DragHandle")
            ?? throw new Exception("找不到拖曳符號");
        RaiseMouse(handle, UIElement.PreviewMouseLeftButtonDownEvent);
        RaiseMouse(handle, UIElement.PreviewMouseLeftButtonUpEvent);
        Check(controller.Copied.Count == 0, "拖曳符號不複製");
        InvokeClick(panel, results, firstRow);
        Check(controller.Copied.SequenceEqual(new[] { snippets[0].Content }) && !panel.IsVisible,
            "單擊常用文字複製並收起");

        panel.ShowPanel();
        var historyTab = panel.FindName("HistoryTab") as Button ?? throw new Exception("找不到歷史分頁");
        historyTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        panel.UpdateLayout();
        Check(results.Items.Count == 1, "歷史分頁顯示一筆");
        var historyRow = results.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem
            ?? throw new Exception("找不到歷史列");
        InvokeClick(panel, results, historyRow);
        Check(controller.Copied.Last() == history[0].Content && !panel.IsVisible,
            "單擊複製歷史並收起");

        panel.ShowPanel();
        var openedManager = 0;
        panel.ManagerRequested += (_, _) => openedManager++;
        var managerButton = FindVisual<Button>(panel, button => Equals(button.Content, "管理"))
            ?? throw new Exception("找不到管理入口");
        managerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(openedManager == 1 && !panel.IsVisible, "管理入口開啟管理視窗");

        var manager = new MainWindow(controller);
        try
        {
            manager.Show();
            manager.UpdateLayout();
            var mode = manager.FindName("ViewSelector") as ComboBox ?? throw new Exception("找不到管理檢視選單");
            var managerSnippets = manager.FindName("SnippetList") as ListBox ?? throw new Exception("找不到管理文字列表");
            Check(mode.Items.Count == 4 && managerSnippets.Items.Count == 3, "兩欄管理視窗保留四種檢視並預設首分類");
            var managerRow = managerSnippets.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem
                ?? throw new Exception("找不到管理文字列");
            var secondManagerRow = managerSnippets.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem
                ?? throw new Exception("找不到管理視窗第二筆常用文字列");
            var thirdManagerRowForHeight = managerSnippets.ItemContainerGenerator.ContainerFromIndex(2) as ListBoxItem
                ?? throw new Exception("找不到管理視窗長文字列");
            Check(Math.Abs(managerRow.ActualHeight - thirdManagerRowForHeight.ActualHeight) < 0.1 &&
                  Math.Abs(managerRow.ActualHeight - 72) < 0.1, "管理視窗長文字不會撐高文字列");
            controller.ChangeFontScale(1.75);
            manager.UpdateLayout();
            Check(Math.Abs(managerRow.ActualHeight - thirdManagerRowForHeight.ActualHeight) < 0.1 &&
                  Math.Abs(managerRow.ActualHeight - 126) < 0.1, "管理視窗放大文字後仍維持等高列");
            controller.ChangeFontScale(1);
            manager.UpdateLayout();
            var secondManagerHandle = FindVisual<TextBlock>(secondManagerRow, element => element.Text == "⠿")
                ?? throw new Exception("找不到管理視窗第二筆拖曳符號");
            Check(secondManagerHandle.Visibility == Visibility.Visible,
                "管理視窗第二筆顯示拖曳把手");
            RaiseMouse(secondManagerHandle, UIElement.PreviewMouseLeftButtonDownEvent);
            Check((Guid?)typeof(MainWindow).GetField("draggingSnippetId",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(manager) == snippets[1].Id,
                "管理視窗第二筆把手會選定正確文字");
            RaiseMouse(secondManagerHandle, UIElement.PreviewMouseLeftButtonUpEvent);
            CheckDragPreview(manager, managerSnippets, managerRow, "管理視窗",
                Path.Combine(root, "ui-preview", "manager-drag-preview.png"));
            CheckDragPreview(manager, managerSnippets, secondManagerRow, "管理視窗第二筆",
                Path.Combine(root, "ui-preview", "manager-second-row-drag-preview.png"));
            var thirdManagerRow = managerSnippets.ItemContainerGenerator.ContainerFromIndex(2) as ListBoxItem
                ?? throw new Exception("找不到管理視窗第三筆常用文字列");
            CheckDragPreview(manager, managerSnippets, thirdManagerRow, "管理視窗第三筆",
                Path.Combine(root, "ui-preview", "manager-third-row-drag-preview.png"));
            CheckDropIndicator(manager, secondManagerRow,
                Path.Combine(root, "ui-preview", "manager-drop-target.png"));
            var appMenuButton = FindVisual<Button>(manager, button => Equals(button.Content, "設定與資料 ▾"))
                ?? throw new Exception("找不到設定與資料選單");
            var appMenu = appMenuButton.ContextMenu ?? throw new Exception("找不到設定與資料選項");
            appMenu.PlacementTarget = appMenuButton;
            appMenu.IsOpen = true;
            appMenu.UpdateLayout();
            Check(appMenu.Background is SolidColorBrush { Color: var menuColor } && menuColor.R < 70,
                "管理選單使用深色背景");
            appMenu.IsOpen = false;
            controller.ChangeTheme("light");
            CheckDropIndicator(manager, secondManagerRow,
                Path.Combine(root, "ui-preview", "manager-drop-target-light.png"));
            appMenu.IsOpen = true;
            appMenu.UpdateLayout();
            Check(appMenu.Background is SolidColorBrush { Color: var lightMenuColor } && lightMenuColor.R > 180,
                "管理選單會跟隨淺色主題");
            appMenu.IsOpen = false;
            Check(manager.FindResource(typeof(ScrollBar)) is Style, "兩個主題共用自訂捲軸樣式");
            Render(manager, Path.Combine(root, "ui-preview", "manager-two-column-light.png"));
            controller.ChangeTheme("dark");
            Render(manager, Path.Combine(root, "ui-preview", "manager-two-column.png"));
            mode.SelectedIndex = 1;
            var managerHistory = manager.FindName("HistoryList") as ListBox ?? throw new Exception("找不到管理歷史列表");
            Check(managerHistory.Visibility == Visibility.Visible && managerHistory.Items.Count == 1,
                "管理視窗可切換複製歷史");
        }
        finally
        {
            manager.AllowClose();
            manager.Close();
        }

        Console.WriteLine("Quick panel smoke passed: category search, single-click copy, drag handle and manager entry.");
    }
    finally
    {
        panel.AllowClose();
        panel.Close();
        application.Shutdown();
    }
}

static void RaiseMouse(UIElement source, RoutedEvent routedEvent)
{
    source.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
    {
        RoutedEvent = routedEvent
    });
}

static void CheckFirstStartup(string root, Application application)
{
    var testRoot = Path.Combine(root, "ClipboardApp.Smoke", ".test-output", Guid.NewGuid().ToString("N"));
    var legacyDirectory = Path.Combine(testRoot, "legacy");
    var startup = typeof(App).GetMethod("PrepareRepository", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    ClipboardCore.ClipboardRepository Prepare(string path)
    {
        try { return (ClipboardCore.ClipboardRepository)startup.Invoke(null, [path, legacyDirectory])!; }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    void Choose(string label, Action start)
    {
        var responded = false;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            var window = application.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "歡迎使用零零快捷剪貼板");
            if (window is null) return;
            responded = true;
            timer.Stop();
            Render(window, Path.Combine(root, "ui-preview", "first-startup.png"));
            var button = FindVisual<Button>(window, control => Equals(control.Content, label))!;
            Check(button.Focusable, "首次啟動按鈕可使用鍵盤");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start();
        try { start(); }
        finally { timer.Stop(); }
        Check(responded, "首次設定出現可選擇的操作");
    }
    var cancelPath = Path.Combine(testRoot, "cancelled");
    try { Choose("取消", () => Prepare(cancelPath)); throw new Exception("取消沒有中止設定"); }
    catch (OperationCanceledException) { }
    Check(!File.Exists(Path.Combine(cancelPath, "clipboard.db")), "取消首次設定不建立資料庫");
    var freshPath = Path.Combine(testRoot, "fresh");
    ClipboardCore.ClipboardRepository? fresh = null;
    Choose("開始使用", () => fresh = Prepare(freshPath));
    Check(fresh!.IsSetupComplete() && fresh.ListSnippets().Count == 0, "沒有舊版資料也能直接開始");
    var controller = new ClipboardController(fresh, freshPath);
    Check(!controller.CopyText(""), "空文字不造成剪貼簿例外");
    var id = controller.SaveSnippet(null, "全新文字", "", "全新用戶正文", null);
    Check(Prepare(freshPath).GetSnippet(id)!.Content == "全新用戶正文", "全新用戶新增後重新開啟仍保留");
    fresh.SetSetting("font_scale", "NaN");
    fresh.SetSetting("hotkey", "不存在的快捷鍵");
    fresh.SetSetting("theme", "未知主題");
    var safeSettings = controller.GetSettings();
    Check(safeSettings.FontScale == 1 && safeSettings.Hotkey == "right alt" && safeSettings.Theme == "dark", "無效舊設定採可用預設值");
    controller.SetTheme("light");
    controller.SetHotkey("f2");
    var backup = controller.CreateBackup();
    controller.SetTheme("dark");
    controller.SetHotkey("right ctrl");
    var changed = 0;
    controller.SettingsChanged += (_, _) => changed++;
    controller.RestoreBackup(backup);
    Check(changed == 1 && controller.GetSettings().Theme == "light" && controller.GetSettings().Hotkey == "f2",
        "還原立即通知更新主題與快捷鍵");
    var missingPath = Path.Combine(testRoot, "missing");
    Directory.CreateDirectory(missingPath);
    File.WriteAllText(Path.Combine(missingPath, "import-complete"), "existing setup");
    try { Prepare(missingPath); throw new Exception("資料庫遺失仍然啟動"); }
    catch (InvalidDataException) { }
    Check(!File.Exists(Path.Combine(missingPath, "clipboard.db")), "遺失資料庫時不建立空白庫");
    Directory.CreateDirectory(legacyDirectory);
    var legacyFile = Path.Combine(legacyDirectory, "clipboard_data.json");
    File.WriteAllText(legacyFile, "{\"categories\":[\"All\",\"Copied\"],\"snippets\":[{\"emoji\":\"\",\"title\":\"舊文字\",\"category\":\"All\",\"content\":\"完整舊內容\"}]}");
    var sourceBytes = File.ReadAllBytes(legacyFile);
    var importedPath = Path.Combine(testRoot, "imported");
    Check(Prepare(importedPath).ListSnippets().Single().Content == "完整舊內容", "存在舊資料時自動匯入全文");
    Check(Prepare(importedPath).ListSnippets().Count == 1, "沒有外部標記檔仍可辨識完成的匯入");
    Check(File.ReadAllBytes(legacyFile).SequenceEqual(sourceBytes), "匯入不修改舊版來源");
    Console.WriteLine("Startup and controller checks passed using isolated synthetic data.");
}

static void InvokeMouseHandler(object target, string name, UIElement source, RoutedEvent routedEvent)
{
    var handler = target.GetType().GetMethod(name,
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new Exception($"找不到 {name}");
    handler.Invoke(target, [source, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
    {
        RoutedEvent = routedEvent
    }]);
}

static void CheckReorderDirection(IReadOnlyList<SnippetView> snippets)
{
    var type = typeof(QuickPanel).Assembly.GetType("ClipboardApp.Services.SnippetReorder")
        ?? throw new Exception("找不到排序規則");
    var move = type.GetMethod("MoveRelativeToTarget",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        ?? throw new Exception("找不到排序動作");
    var first = snippets[0].Id;
    var second = snippets[1].Id;
    List<Guid> Apply(Guid source, Guid target) =>
        move.Invoke(null, [new List<Guid> { first, second }, source, target]) as List<Guid>
        ?? throw new Exception("排序動作失敗");
    Check(Apply(first, second).SequenceEqual([second, first]), "第一筆往下拖可換位");
    Check(Apply(second, first).SequenceEqual([second, first]), "第二筆往上拖可換位");
}

static void CheckDragPreview(Window owner, ListBox list, ListBoxItem row, string context, string output)
{
    var layer = AdornerLayer.GetAdornerLayer(list)
        ?? throw new Exception($"{context}缺少拖曳預覽圖層");
    var type = typeof(QuickPanel).Assembly.GetType("ClipboardApp.Styles.DragRowPreview")
        ?? throw new Exception("找不到拖曳預覽元件");
    var begin = type.GetMethod("Begin", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        ?? throw new Exception("找不到拖曳預覽入口");
    var opacity = row.Opacity;
    using (var preview = begin.Invoke(null, [list, row]) as IDisposable
        ?? throw new Exception($"{context}無法顯示整列拖曳預覽"))
    {
        Check(row.Opacity < opacity && layer.GetAdorners(list)?.Length == 1,
            $"{context}拖曳時顯示整列預覽並淡化原列");
        var adorner = layer.GetAdorners(list)![0];
        var previewBorder = adorner.GetType().GetField("preview",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(adorner) as Border;
        var bitmap = (previewBorder?.Child as System.Windows.Controls.Image)?.Source as BitmapSource
            ?? throw new Exception($"{context}預覽影像不存在");
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Check(Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 3] != 0),
            $"{context}預覽影像不能是空白");
        adorner.GetType().GetProperty("Position")?.SetValue(adorner,
            new System.Windows.Point(18, row.TranslatePoint(new System.Windows.Point(), list).Y + 18));
        owner.UpdateLayout();
        Render(owner, output);
    }
    Check(Math.Abs(row.Opacity - opacity) < 0.001 && layer.GetAdorners(list) is null,
        $"{context}拖曳結束後清除預覽並還原原列");
}

static void CheckDropIndicator(Window owner, ListBoxItem row, string output)
{
    var type = typeof(QuickPanel).Assembly.GetType("ClipboardApp.Styles.DropTargetIndicator")
        ?? throw new Exception("找不到拖曳落點提示元件");
    var show = type.GetMethod("Show", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        ?? throw new Exception("找不到落點提示入口");
    using (var indicator = show.Invoke(null, [row]) as IDisposable
        ?? throw new Exception("管理視窗無法顯示落點提示"))
    {
        Check(AdornerLayer.GetAdornerLayer(row)?.GetAdorners(row)?.Length == 1,
            "管理視窗標示預計落點");
        Render(owner, output);
    }
    Check(AdornerLayer.GetAdornerLayer(row)?.GetAdorners(row) is null,
        "管理視窗結束拖曳後清除落點提示");
}

static void InvokeClick(QuickPanel panel, ListBox list, ListBoxItem row)
{
    var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
    {
        RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent
    };
    row.RaiseEvent(args);
    var handler = typeof(QuickPanel).GetMethod("ResultList_PreviewMouseLeftButtonUp",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new Exception("找不到單擊處理");
    handler.Invoke(panel, [list, args]);
}

static T? FindVisual<T>(DependencyObject parent, Func<T, bool> match) where T : DependencyObject
{
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
    {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is T typed && match(typed)) return typed;
        var nested = FindVisual(child, match);
        if (nested is not null) return nested;
    }
    return null;
}

static T? FindParent<T>(DependencyObject? current) where T : DependencyObject
{
    while (current is not null)
    {
        if (current is T result) return result;
        current = VisualTreeHelper.GetParent(current);
    }
    return null;
}

static void Render(Window window, string path)
{
    window.UpdateLayout();
    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
        (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(window);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var output = File.Create(path);
    encoder.Save(output);
}

static void RenderElement(FrameworkElement element, string path)
{
    element.UpdateLayout();
    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),
        (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(element);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var output = File.Create(path);
    encoder.Save(output);
}

static void RespondToDialog(Application application, string title, bool accept, Action open)
{
    var responded = false;
    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
    timer.Tick += (_, _) =>
    {
        var dialog = application.Windows.OfType<Window>().FirstOrDefault(w => w.Title == title);
        if (dialog is null) return;
        responded = true;
        timer.Stop();
        dialog.DialogResult = accept;
    };
    timer.Start();
    open();
    timer.Stop();
    Check(responded, $"{title} 確認視窗已開啟");
}

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception($"驗收失敗：{label}");
}
