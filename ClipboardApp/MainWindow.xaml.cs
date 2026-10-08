using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClipboardApp.Services;
using ClipboardApp.Styles;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using ComboBox = System.Windows.Controls.ComboBox;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using GiveFeedbackEventArgs = System.Windows.GiveFeedbackEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;

namespace ClipboardApp;

public partial class MainWindow : Window
{
    private enum ListMode { Snippets, History, Drafts, Trash }
    private sealed record ListModeChoice(string Name);
    internal sealed record CategoryChoice(Guid? Id, string Name);

    private readonly IClipboardController controller;
    private readonly DispatcherTimer draftTimer;
    private ClipboardSnapshot snapshot;
    private ListMode mode;
    private Guid? selectedCategoryId;
    private Guid? editingId;
    private string editingEmoji = "";
    private bool isLoading;
    private bool allowClose;
    private bool draftDirty;
    private Point dragStart;
    private Guid? draggingSnippetId;
    private ListBoxItem? draggingSnippetRow;
    private DropTargetIndicator? dropTargetIndicator;

    public event EventHandler? QuickPanelRequested;

    public MainWindow(IClipboardController controller)
    {
        this.controller = controller;
        snapshot = controller.GetSnapshot();
        selectedCategoryId = snapshot.Categories.OrderBy(c => c.SortOrder).FirstOrDefault()?.Id ?? Guid.Empty;
        InitializeComponent();
        ViewSelector.ItemsSource = new[]
        {
            new ListModeChoice("常用文字"), new ListModeChoice("複製歷史"),
            new ListModeChoice("未完成草稿"), new ListModeChoice("垃圾桶")
        };
        draftTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        draftTimer.Tick += (_, _) => FlushDraft();
        controller.Changed += Controller_Changed;
        controller.SettingsChanged += Controller_SettingsChanged;
        Loaded += (_, _) => UiPreferences.Apply(this, controller.GetSettings());
        RefreshSnapshot();
        ShowMode(ListMode.Snippets);
    }

    public void AllowClose() => allowClose = true;
    public bool TryPrepareToClose() => FlushDraft();

    private void Controller_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) RefreshSnapshot();
        else Dispatcher.BeginInvoke(RefreshSnapshot);
    }

    private void Controller_SettingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) UiPreferences.Apply(this, controller.GetSettings());
        else Dispatcher.BeginInvoke(() => UiPreferences.Apply(this, controller.GetSettings()));
    }

    private void RefreshSnapshot()
    {
        try
        {
            snapshot = controller.GetSnapshot();
            isLoading = true;
            var filterChoices = new List<CategoryChoice> { new(null, "所有分類"), new(Guid.Empty, "未分類") };
            filterChoices.AddRange(snapshot.Categories.OrderBy(c => c.SortOrder).Select(c => new CategoryChoice(c.Id, c.Name)));
            if (selectedCategoryId is Guid selected && selected != Guid.Empty && filterChoices.All(c => c.Id != selected))
                selectedCategoryId = snapshot.Categories.OrderBy(c => c.SortOrder).FirstOrDefault()?.Id ?? Guid.Empty;
            CategoryList.ItemsSource = filterChoices;
            CategoryList.SelectedItem = filterChoices.First(c => c.Id == selectedCategoryId);
            RefreshCategoryChoices();
            RefreshLists();
            PauseHistoryButton.Header = snapshot.IsHistoryPaused ? "繼續記錄複製歷史" : "暫停記錄複製歷史";
            isLoading = false;
        }
        catch (Exception ex)
        {
            isLoading = false;
            SetError("資料更新失敗", ex);
        }
    }

    private void RefreshCategoryChoices()
    {
        var selected = (EditorCategory.SelectedItem as CategoryChoice)?.Id;
        var choices = new List<CategoryChoice> { new(null, "未分類") };
        choices.AddRange(snapshot.Categories.OrderBy(c => c.SortOrder).Select(c => new CategoryChoice(c.Id, c.Name)));
        EditorCategory.ItemsSource = choices;
        EditorCategory.SelectedItem = choices.FirstOrDefault(c => c.Id == selected) ?? choices[0];
    }

    private void RefreshLists()
    {
        var terms = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Match(string value) => terms.All(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
        var selectedSnippetIds = SnippetList.SelectedItems.Cast<SnippetView>().Select(s => s.Id).ToHashSet();
        var snippets = snapshot.Snippets
            .Where(s => !string.IsNullOrWhiteSpace(SearchBox.Text) || selectedCategoryId == null ||
                        (selectedCategoryId == Guid.Empty ? s.CategoryId == null : s.CategoryId == selectedCategoryId))
            .Where(s => Match(s.Title + " " + s.Content))
            .OrderBy(s => s.SortOrder).ToList();
        if (mode == ListMode.Drafts)
        {
            snippets = snapshot.Drafts.Values
                .Where(d => Match(d.Title + " " + d.Content))
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => new SnippetView(d.SnippetId, "草稿 · " + d.Title, d.Emoji, d.Content, d.CategoryId, 0, d.UpdatedAt))
                .ToList();
        }
        SnippetList.ItemsSource = snippets;
        foreach (var snippet in snippets.Where(s => selectedSnippetIds.Contains(s.Id))) SnippetList.SelectedItems.Add(snippet);
        var history = snapshot.History.Where(h => Match(h.Content)).OrderByDescending(h => h.CapturedAt).ToList();
        HistoryList.ItemsSource = history;
        TrashList.ItemsSource = snapshot.Trash.Where(s => Match(s.Title + " " + s.Content)).ToList();
        ListCount.Text = mode switch
        {
            ListMode.History => $"{history.Count} 筆",
            ListMode.Drafts => $"{snippets.Count} 筆",
            ListMode.Trash => $"{TrashList.Items.Count} 筆",
            _ => $"{snippets.Count} 筆"
        };
    }

    private void ShowMode(ListMode newMode)
    {
        if (!FlushDraft())
        {
            isLoading = true;
            ViewSelector.SelectedIndex = (int)mode;
            isLoading = false;
            return;
        }
        mode = newMode;
        SnippetList.Visibility = mode is ListMode.Snippets or ListMode.Drafts ? Visibility.Visible : Visibility.Collapsed;
        SnippetList.DataContext = mode == ListMode.Snippets ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = mode == ListMode.History ? Visibility.Visible : Visibility.Collapsed;
        TrashList.Visibility = mode == ListMode.Trash ? Visibility.Visible : Visibility.Collapsed;
        NewSnippetButton.Visibility = mode == ListMode.Snippets ? Visibility.Visible : Visibility.Collapsed;
        UpdateDragHandleAvailability();
        MoveAction.Visibility = DeleteAction.Visibility = mode == ListMode.Snippets ? Visibility.Visible : Visibility.Collapsed;
        PromoteAction.Visibility = PauseHistoryButton.Visibility = ClearHistoryAction.Visibility = mode == ListMode.History ? Visibility.Visible : Visibility.Collapsed;
        DiscardDraftsAction.Visibility = mode == ListMode.Drafts ? Visibility.Visible : Visibility.Collapsed;
        RestoreAction.Visibility = PermanentDeleteAction.Visibility = mode == ListMode.Trash ? Visibility.Visible : Visibility.Collapsed;
        CategoryList.IsEnabled = mode == ListMode.Snippets;
        if (ViewSelector.SelectedIndex != (int)mode) ViewSelector.SelectedIndex = (int)mode;
        EditorHeading.Text = mode == ListMode.History ? "歷史全文" : mode == ListMode.Trash ? "已刪除文字" : "文字編輯";
        ListTitle.Text = mode == ListMode.History ? "複製歷史" : mode == ListMode.Drafts ? "未完成草稿" : mode == ListMode.Trash ? "垃圾桶" : "常用文字";
        ClearEditor();
        RefreshLists();
    }

    private void ClearEditor()
    {
        draftTimer.Stop();
        isLoading = true;
        editingId = null;
        draftDirty = false;
        editingEmoji = "";
        TitleBox.Text = "";
        ContentBox.Text = "";
        EditorCategory.SelectedIndex = 0;
        DraftStatus.Text = "";
        isLoading = false;
        SetEditorEnabled(false);
    }

    private void SetEditorEnabled(bool enabled)
    {
        bool editable = enabled && mode is ListMode.Snippets or ListMode.Drafts;
        TitleBox.IsReadOnly = !editable;
        ContentBox.IsReadOnly = !editable;
        EditorCategory.IsEnabled = editable;
        SaveButton.IsEnabled = editable;
    }

    private void LoadSnippet(SnippetView snippet, bool readOnly = false)
    {
        isLoading = true;
        editingId = snippet.Id;
        draftDirty = false;
        var draft = !readOnly && snapshot.Drafts.TryGetValue(snippet.Id, out var found) ? found : null;
        editingEmoji = draft?.Emoji ?? snippet.Emoji;
        TitleBox.Text = draft?.Title ?? snippet.Title;
        ContentBox.Text = draft?.Content ?? snippet.Content;
        SelectEditorCategory(draft is null ? snippet.CategoryId : draft.CategoryId);
        DraftStatus.Text = draft is null ? "已儲存" : "有未儲存草稿";
        isLoading = false;
        SetEditorEnabled(true);
    }

    private void SelectEditorCategory(Guid? id)
    {
        EditorCategory.SelectedItem = EditorCategory.Items.Cast<CategoryChoice>().FirstOrDefault(c => c.Id == id)
                                      ?? EditorCategory.Items.Cast<CategoryChoice>().FirstOrDefault();
    }

    private bool FlushDraft()
    {
        draftTimer.Stop();
        if (isLoading || editingId is not Guid id || mode is not (ListMode.Snippets or ListMode.Drafts) || !draftDirty) return true;
        try
        {
            controller.SaveDraft(id, TitleBox.Text, editingEmoji, ContentBox.Text, (EditorCategory.SelectedItem as CategoryChoice)?.Id);
            draftDirty = false;
            DraftStatus.Text = "草稿已保存";
            return true;
        }
        catch (Exception ex) { SetError("草稿保存失敗，請保留目前視窗並重試", ex); return false; }
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e) => MarkDraftDirty();
    private void EditorCategory_SelectionChanged(object sender, SelectionChangedEventArgs e) => MarkDraftDirty();
    private void MarkDraftDirty()
    {
        if (isLoading || editingId is null || mode is not (ListMode.Snippets or ListMode.Drafts)) return;
        DraftStatus.Text = "尚未儲存";
        draftDirty = true;
        draftTimer.Stop();
        draftTimer.Start();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (editingId is not Guid id) return;
        draftTimer.Stop();
        try
        {
            var savedId = controller.SaveSnippet(id, TitleBox.Text, editingEmoji, ContentBox.Text, (EditorCategory.SelectedItem as CategoryChoice)?.Id);
            editingId = savedId;
            draftDirty = false;
            DraftStatus.Text = "已儲存";
            if (mode == ListMode.Drafts) ShowMode(ListMode.Snippets);
            RefreshSnapshot();
            SnippetList.SelectedItem = snapshot.Snippets.FirstOrDefault(s => s.Id == savedId);
            SetStatus("文字已儲存");
        }
        catch (Exception ex)
        {
            DraftStatus.Text = "儲存失敗，內容仍在編輯區";
            draftDirty = true;
            SetError("儲存失敗", ex);
        }
    }

    private void NewSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (!FlushDraft()) return;
        SnippetList.SelectedItems.Clear();
        isLoading = true;
        editingId = Guid.NewGuid();
        editingEmoji = "";
        TitleBox.Text = "";
        ContentBox.Text = "";
        SelectEditorCategory(selectedCategoryId);
        DraftStatus.Text = "尚未儲存";
        draftDirty = true;
        isLoading = false;
        SetEditorEnabled(true);
        TitleBox.Focus();
    }

    private void SnippetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoading) return;
        var next = SnippetList.SelectedItem as SnippetView;
        if (next is null || next.Id == editingId) return;
        if (!FlushDraft())
        {
            isLoading = true;
            SnippetList.SelectedItem = SnippetList.Items.Cast<SnippetView>().FirstOrDefault(s => s.Id == editingId);
            isLoading = false;
            return;
        }
        LoadSnippet(next);
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoading || HistoryList.SelectedItem is not HistoryView item) return;
        isLoading = true;
        editingId = null;
        editingEmoji = "";
        TitleBox.Text = item.CapturedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        ContentBox.Text = item.Content;
        DraftStatus.Text = "";
        isLoading = false;
        SetEditorEnabled(true);
    }

    private void TrashList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoading || TrashList.SelectedItem is not SnippetView item) return;
        LoadSnippet(item, true);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SnippetList is null || snapshot is null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        UpdateDragHandleAvailability();
        RefreshLists();
    }

    private void UpdateDragHandleAvailability()
    {
        if (SnippetList is null || SearchBox is null) return;
        SnippetList.Tag = mode == ListMode.Snippets && selectedCategoryId is Guid &&
                          string.IsNullOrWhiteSpace(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoading || CategoryList.SelectedItem is not CategoryChoice item) return;
        if (!FlushDraft())
        {
            isLoading = true;
            CategoryList.SelectedItem = CategoryList.Items.Cast<CategoryChoice>().FirstOrDefault(c => c.Id == selectedCategoryId);
            isLoading = false;
            return;
        }
        selectedCategoryId = item.Id;
        ShowMode(ListMode.Snippets);
    }

    private void ViewSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoading || draftTimer is null || ViewSelector.SelectedIndex < 0 || (int)mode == ViewSelector.SelectedIndex) return;
        ShowMode((ListMode)ViewSelector.SelectedIndex);
    }

    private static void OpenContextMenu(object sender)
    {
        if (sender is not Button button || button.ContextMenu is null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void OpenAppMenu_Click(object sender, RoutedEventArgs e) => OpenContextMenu(sender);
    private void OpenCategoryMenu_Click(object sender, RoutedEventArgs e) => OpenContextMenu(sender);
    private void OpenModeMenu_Click(object sender, RoutedEventArgs e) => OpenContextMenu(sender);

    private void DiscardSelectedDrafts_Click(object sender, RoutedEventArgs e)
    {
        if (mode != ListMode.Drafts) return;
        var ids = SnippetList.SelectedItems.Cast<SnippetView>().Select(s => s.Id).ToList();
        if (ids.Count == 0) { SetStatus("請先選擇草稿"); return; }
        if (!UiPrompt.Confirm(this, "放棄草稿", $"確定放棄 {ids.Count} 筆未完成草稿？", "放棄草稿")) return;
        try
        {
            draftTimer.Stop();
            foreach (var id in ids) controller.DiscardDraft(id);
            if (editingId is Guid current && ids.Contains(current)) ClearEditor();
            RefreshSnapshot();
            SetStatus($"已放棄 {ids.Count} 筆草稿");
        }
        catch (Exception ex) { SetError("放棄草稿失敗", ex); }
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var name = UiPrompt.Ask(this, "新增分類", "分類名稱");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var id = controller.AddCategory(name.Trim());
            RefreshSnapshot();
            CategoryList.SelectedItem = CategoryList.Items.Cast<CategoryChoice>().FirstOrDefault(c => c.Id == id);
        }
        catch (Exception ex) { SetError("新增分類失敗", ex); }
    }

    private void RenameCategory_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryList.SelectedItem is not CategoryChoice { Id: Guid id } item || id == Guid.Empty) { SetStatus("請先選擇自訂分類"); return; }
        var name = UiPrompt.Ask(this, "重新命名分類", "分類名稱", item.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        try { controller.RenameCategory(id, name.Trim()); RefreshSnapshot(); }
        catch (Exception ex) { SetError("重新命名失敗", ex); }
    }

    private void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryList.SelectedItem is not CategoryChoice { Id: Guid id } item || id == Guid.Empty) { SetStatus("請先選擇自訂分類"); return; }
        if (!UiPrompt.Confirm(this, "刪除分類", $"刪除「{item.Name}」？其中的文字會移至未分類。", "刪除分類")) return;
        try
        {
            controller.DeleteCategory(id);
            selectedCategoryId = null;
            RefreshSnapshot();
            ShowMode(ListMode.Snippets);
        }
        catch (Exception ex) { SetError("刪除分類失敗", ex); }
    }

    private void MoveCategoryUp_Click(object sender, RoutedEventArgs e) => MoveCategory(-1);
    private void MoveCategoryDown_Click(object sender, RoutedEventArgs e) => MoveCategory(1);

    private void MoveCategory(int direction)
    {
        if (CategoryList.SelectedItem is not CategoryChoice { Id: Guid id } || id == Guid.Empty) { SetStatus("請先選擇自訂分類"); return; }
        var ordered = snapshot.Categories.OrderBy(c => c.SortOrder).Select(c => c.Id).ToList();
        var index = ordered.IndexOf(id);
        var next = index + direction;
        if (index < 0 || next < 0 || next >= ordered.Count) return;
        (ordered[index], ordered[next]) = (ordered[next], ordered[index]);
        try { controller.ReorderCategories(ordered); RefreshSnapshot(); SetStatus("分類順序已更新"); }
        catch (Exception ex) { SetError("分類排序失敗", ex); }
    }

    private void MoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var ids = SnippetList.SelectedItems.Cast<SnippetView>().Select(s => s.Id).ToList();
        if (ids.Count == 0) { SetStatus("請先選擇文字，可用 Ctrl 或 Shift 多選"); return; }
        var choices = new List<CategoryChoice> { new(null, "未分類") };
        choices.AddRange(snapshot.Categories.OrderBy(c => c.SortOrder).Select(c => new CategoryChoice(c.Id, c.Name)));
        var choice = UiPrompt.Choose(this, "搬移文字", "目標分類", choices);
        if (choice is null) return;
        try
        {
            if (!FlushDraft()) return;
            controller.MoveSnippets(ids, choice.Id);
            RefreshSnapshot();
            if (editingId is Guid current && ids.Contains(current) && snapshot.Snippets.FirstOrDefault(s => s.Id == current) is { } edited)
                LoadSnippet(edited);
            SetStatus($"已搬移 {ids.Count} 筆文字");
        }
        catch (Exception ex) { SetError("搬移失敗", ex); }
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var ids = SnippetList.SelectedItems.Cast<SnippetView>().Select(s => s.Id).ToList();
        if (ids.Count == 0) { SetStatus("請先選擇文字"); return; }
        try
        {
            if (!FlushDraft()) return;
            controller.DeleteSnippets(ids);
            if (editingId is Guid id && ids.Contains(id)) ClearEditor();
            RefreshSnapshot();
            SetStatus($"已將 {ids.Count} 筆文字移至垃圾桶");
        }
        catch (Exception ex) { SetError("刪除失敗", ex); }
    }

    private void DeleteSnippetRow_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (mode != ListMode.Snippets || sender is not Button { DataContext: SnippetView snippet }) return;
        if (!UiPrompt.Confirm(this, "刪除文字", $"將「{snippet.Title}」移至垃圾桶？之後仍可復原。", "移至垃圾桶")) return;
        try
        {
            if (editingId == snippet.Id && !FlushDraft()) return;
            controller.DeleteSnippets(new[] { snippet.Id });
            if (editingId == snippet.Id) ClearEditor();
            RefreshSnapshot();
            SetStatus("已移至垃圾桶，可在垃圾桶復原");
        }
        catch (Exception ex) { SetError("刪除失敗", ex); }
    }

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var ids = TrashList.SelectedItems.Cast<SnippetView>().Select(s => s.Id).ToList();
        if (ids.Count == 0) { SetStatus("請先選擇要復原的文字"); return; }
        try { foreach (var id in ids) controller.RestoreSnippet(id); RefreshSnapshot(); ClearEditor(); SetStatus($"已復原 {ids.Count} 筆文字"); }
        catch (Exception ex) { SetError("復原失敗", ex); }
    }

    private void PermanentlyDeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var ids = TrashList.SelectedItems.Cast<SnippetView>().Select(s => s.Id).ToList();
        if (ids.Count == 0) { SetStatus("請先選擇要永久刪除的文字"); return; }
        if (!UiPrompt.Confirm(this, "永久刪除", $"永久刪除 {ids.Count} 筆文字後，垃圾桶將無法復原。確定刪除？", "永久刪除")) return;
        try
        {
            controller.PermanentlyDeleteSnippets(ids);
            RefreshSnapshot();
            ClearEditor();
            SetStatus($"已永久刪除 {ids.Count} 筆文字");
        }
        catch (Exception ex) { SetError("永久刪除失敗", ex); }
    }

    private void PauseHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            controller.SetHistoryPaused(!snapshot.IsHistoryPaused);
            RefreshSnapshot();
            SetStatus(snapshot.IsHistoryPaused ? "已暫停記錄複製歷史" : "已繼續記錄複製歷史");
        }
        catch (Exception ex) { SetError("切換歷史記錄失敗", ex); }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!UiPrompt.Confirm(this, "清空歷史", "確定清空全部複製歷史？", "清空歷史")) return;
        try { controller.ClearHistory(); RefreshSnapshot(); ClearEditor(); SetStatus("複製歷史已清空"); }
        catch (Exception ex) { SetError("清空歷史失敗", ex); }
    }

    private void PromoteHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryView item) { SetStatus("請先選擇歷史文字"); return; }
        try
        {
            var id = controller.PromoteHistory(item.Id);
            selectedCategoryId = null;
            RefreshSnapshot();
            ShowMode(ListMode.Snippets);
            SnippetList.SelectedItem = snapshot.Snippets.FirstOrDefault(s => s.Id == id);
            SetStatus("已轉存為常用文字");
        }
        catch (Exception ex) { SetError("轉存失敗", ex); }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!FlushDraft()) return;
        try
        {
            var path = controller.CreateBackup();
            UiPrompt.Notify(this, "手動備份", $"備份已建立：\n{path}");
        }
        catch (Exception ex) { SetError("備份失敗", ex); }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "選擇零零快捷剪貼板備份", Filter = "備份檔 (*.db;*.sqlite)|*.db;*.sqlite|所有檔案 (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        if (!UiPrompt.Confirm(this, "還原備份", $"還原「{System.IO.Path.GetFileName(dialog.FileName)}」將取代目前資料。還原前會先備份現況。確定繼續？", "還原備份")) return;
        if (!FlushDraft()) return;
        try
        {
            controller.RestoreBackup(dialog.FileName);
            ClearEditor();
            RefreshSnapshot();
            SetStatus("備份已還原");
        }
        catch (Exception ex) { SetError("還原失敗，原資料應保持不變", ex); }
    }

    private void OpenQuickPanel_Click(object sender, RoutedEventArgs e) => QuickPanelRequested?.Invoke(this, EventArgs.Empty);

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(controller) { Owner = this };
        settings.ShowDialog();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && SaveButton.IsEnabled)
        {
            Save_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!FlushDraft()) { e.Cancel = true; return; }
        if (!allowClose) { e.Cancel = true; Hide(); }
        else
        {
            controller.Changed -= Controller_Changed;
            controller.SettingsChanged -= Controller_SettingsChanged;
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;
    private void SetError(string message, Exception ex)
    {
        StatusText.Text = $"{message}：{ex.Message}";
        UiPrompt.Notify(this, message, ex.Message);
    }

    private void SnippetHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        draggingSnippetId = null;
        draggingSnippetRow = null;
        if (mode != ListMode.Snippets || selectedCategoryId is not Guid categoryId ||
            !string.IsNullOrWhiteSpace(SearchBox.Text) || sender is not FrameworkElement { DataContext: SnippetView snippet } ||
            snippet.CategoryId != (categoryId == Guid.Empty ? null : categoryId)) return;
        draggingSnippetId = snippet.Id;
        draggingSnippetRow = FindListItem(sender as DependencyObject);
        dragStart = e.GetPosition(SnippetList);
        e.Handled = true;
    }

    private void SnippetHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { draggingSnippetId = null; draggingSnippetRow = null; return; }
        if (draggingSnippetId is not Guid id ||
            mode != ListMode.Snippets || !string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        if ((e.GetPosition(SnippetList) - dragStart).Length < 8) return;
        var row = draggingSnippetRow;
        draggingSnippetId = null;
        draggingSnippetRow = null;
        using var preview = row is null ? null : DragRowPreview.Begin(SnippetList, row);
        if (preview is not null)
        {
            SnippetList.GiveFeedback += FollowCursor;
        }
        try { DragDrop.DoDragDrop(SnippetList, new DataObject("snippet-order", id), DragDropEffects.Move); }
        finally
        {
            SnippetList.GiveFeedback -= FollowCursor;
            ClearDropTarget();
        }
        void FollowCursor(object sender, GiveFeedbackEventArgs args) => preview?.UpdatePosition();
    }

    private void SnippetList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        draggingSnippetId = null;
        draggingSnippetRow = null;
        ClearDropTarget();
    }

    private void SnippetList_DragOver(object sender, DragEventArgs e)
    {
        if (mode != ListMode.Snippets || !e.Data.GetDataPresent("snippet-order") ||
            selectedCategoryId is not Guid categoryId || !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            ClearDropTarget();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var targetRow = FindListItem(e.OriginalSource as DependencyObject);
        var target = targetRow?.DataContext as SnippetView;
        var moving = (Guid)e.Data.GetData("snippet-order")!;
        Guid? actualCategoryId = categoryId == Guid.Empty ? null : categoryId;
        var order = snapshot.Snippets.Where(s => s.CategoryId == actualCategoryId)
            .OrderBy(s => s.SortOrder).Select(s => s.Id).ToList();
        var sourceIndex = order.IndexOf(moving);
        var targetIndex = target is null ? -1 : order.IndexOf(target.Id);
        if (targetRow is null || target is null || target.CategoryId != actualCategoryId ||
            sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            ClearDropTarget();
            e.Effects = DragDropEffects.None;
        }
        else
        {
            if (dropTargetIndicator is null)
                dropTargetIndicator = DropTargetIndicator.Show(targetRow);
            else
                dropTargetIndicator.Update(targetRow);
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    private void SnippetList_DragLeave(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(SnippetList);
        if (point.X < 0 || point.Y < 0 || point.X >= SnippetList.ActualWidth || point.Y >= SnippetList.ActualHeight)
            ClearDropTarget();
    }

    private void ClearDropTarget()
    {
        dropTargetIndicator?.Dispose();
        dropTargetIndicator = null;
    }

    private void SnippetList_Drop(object sender, DragEventArgs e)
    {
        ClearDropTarget();
        if (mode != ListMode.Snippets || !e.Data.GetDataPresent("snippet-order") ||
            selectedCategoryId is not Guid categoryId || !string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        var target = FindListItem(e.OriginalSource as DependencyObject)?.DataContext as SnippetView;
        var moving = (Guid)e.Data.GetData("snippet-order")!;
        Guid? actualCategoryId = categoryId == Guid.Empty ? null : categoryId;
        if (target is null || target.CategoryId != actualCategoryId || target.Id == moving) return;
        var current = snapshot.Snippets.Where(s => s.CategoryId == actualCategoryId).OrderBy(s => s.SortOrder).Select(s => s.Id).ToList();
        var order = SnippetReorder.MoveRelativeToTarget(current, moving, target.Id);
        if (order is null) return;
        try { controller.ReorderSnippets(actualCategoryId, order); RefreshSnapshot(); SetStatus("文字順序已更新"); }
        catch (Exception ex) { SetError("文字排序失敗", ex); }
    }

    private static ListBoxItem? FindListItem(DependencyObject? element)
    {
        while (element is not null && element is not ListBoxItem) element = VisualTreeHelper.GetParent(element);
        return element as ListBoxItem;
    }
}

internal static class UiPrompt
{
    public static string? ChooseStartup()
    {
        var dialog = MakeDialog(null, "歡迎使用零零快捷剪貼板");
        var panel = DialogBody("第一次使用？");
        panel.Children.Add(new TextBlock { Text = "可以直接開始，也能匯入以前保存的文字。", TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18) });
        string? choice = null;
        foreach (var (value, label) in new[] { ("new", "開始使用"), ("import", "匯入舊版備份"), ("cancel", "取消") })
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 0, 8),
                IsCancel = value == "cancel", IsDefault = value == "new",
                Style = (Style)dialog.FindResource(value == "new" ? "AccentButton" : typeof(Button)) };
            button.Click += (_, _) => { choice = value == "cancel" ? null : value; dialog.DialogResult = value != "cancel"; };
            panel.Children.Add(button);
        }
        SetBody(dialog, panel);
        return dialog.ShowDialog() == true ? choice : null;
    }

    public static string? Ask(Window owner, string title, string label, string initial = "")
    {
        var dialog = MakeDialog(owner, title);
        var text = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 18) };
        var panel = DialogBody(title);
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(text);
        panel.Children.Add(ActionButtons(dialog, "確定"));
        SetBody(dialog, panel);
        dialog.Loaded += (_, _) => { text.Focus(); text.SelectAll(); };
        return dialog.ShowDialog() == true ? text.Text : null;
    }

    public static MainWindow.CategoryChoice? Choose(Window owner, string title, string label, List<MainWindow.CategoryChoice> choices)
    {
        var dialog = MakeDialog(owner, title);
        var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 18), Height = 38 };
        var panel = DialogBody(title);
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(combo);
        panel.Children.Add(ActionButtons(dialog, "確定"));
        SetBody(dialog, panel);
        return dialog.ShowDialog() == true ? combo.SelectedItem as MainWindow.CategoryChoice : null;
    }

    public static bool Confirm(Window? owner, string title, string message, string action)
    {
        var dialog = MakeDialog(owner, title);
        var panel = DialogBody(title);
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22) });
        panel.Children.Add(ActionButtons(dialog, action));
        SetBody(dialog, panel);
        return dialog.ShowDialog() == true;
    }

    public static void Notify(Window? owner, string title, string message)
    {
        var dialog = MakeDialog(owner, title);
        var panel = DialogBody(title);
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22) });
        var acknowledge = new Button { Content = "知道了", MinWidth = 94, HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)dialog.FindResource("AccentButton") };
        acknowledge.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(acknowledge);
        SetBody(dialog, panel);
        dialog.ShowDialog();
    }

    private static Window MakeDialog(Window? owner, string title)
    {
        var dialog = new Window
        {
            Owner = owner, Title = title, Width = Math.Min(440, SystemParameters.WorkArea.Width - 24),
            MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height - 24), SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            FontFamily = new FontFamily("Microsoft JhengHei UI"), FontSize = owner?.FontSize ?? 14
        };
        if (owner?.Resources.MergedDictionaries.FirstOrDefault() is { } theme)
            dialog.Resources.MergedDictionaries.Add(theme);
        else
            dialog.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/ZeroZeroClipboard;component/Styles/Theme.xaml")
            });
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            dialog.DialogResult = false;
            e.Handled = true;
        };
        dialog.Loaded += (_, _) =>
        {
            var light = owner?.TryFindResource("PageBrush") is SolidColorBrush brush && brush.Color.R > 128;
            UiPreferences.Apply(dialog, new ClipboardSettings("right alt", false, (owner?.FontSize ?? 14) / 14,
                light ? "light" : "dark", ""));
        };
        return dialog;
    }

    private static StackPanel DialogBody(string title)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14) });
        return panel;
    }

    private static void SetBody(Window dialog, StackPanel panel)
    {
        var frame = new Border { Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, CornerRadius = new CornerRadius(18), Padding = new Thickness(24),
            BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "MutedBrush");
        dialog.Content = frame;
    }

    private static StackPanel ActionButtons(Window dialog, string action)
    {
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true,
            Style = (Style)dialog.FindResource(typeof(Button)) };
        var confirm = new Button { Content = action, MinWidth = 94, Style = (Style)dialog.FindResource("AccentButton") };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        confirm.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        return buttons;
    }
}
