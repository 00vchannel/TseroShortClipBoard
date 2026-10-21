using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClipboardApp.Services;
using ClipboardApp.Styles;
using Forms = System.Windows.Forms;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using ClipboardCore;

namespace ClipboardApp;

public partial class QuickPanel : Window
{
    private sealed record FilterChoice(Guid Id, string Name);
    private sealed record ResultItem(Guid Id, string Title, string Preview, string Content, bool IsSnippet, bool CanReorder);

    private readonly IClipboardController controller;
    private ClipboardSnapshot snapshot;
    private bool showingHistory;
    private bool isLoading;
    private bool allowClose;
    private bool composing;
    private FilterChoice? selectedCategory;
    private Guid? pendingDragId;
    private ListBoxItem? pendingDragRow;
    private DragRowPreview? dragPreview;
    private DropTargetIndicator? dropTargetIndicator;
    private Guid? highlightedTargetId;
    private bool dragActive;
    private Point dragStart;
    private double preferredWidth = 620;
    private double preferredHeight = 760;
    private bool recordingManualSize;
    private readonly IAppUpdateService? updates;
    public bool IsBusyForUpdate => composing || dragActive;

    public event EventHandler? ManagerRequested;

    public QuickPanel(IClipboardController controller) : this(controller, null) { }

    public QuickPanel(IClipboardController controller, IAppUpdateService? updates)
    {
        this.updates = updates;
        this.controller = controller;
        snapshot = controller.GetSnapshot();
        InitializeComponent();
        if (updates is not null) updates.Changed += Updates_Changed;
        RenderUpdate();
        controller.Changed += Controller_Changed;
        controller.SettingsChanged += Controller_SettingsChanged;
        Loaded += (_, _) => UiPreferences.Apply(this, controller.GetSettings());
        TextCompositionManager.AddPreviewTextInputStartHandler(SearchBox, (_, _) => composing = true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(SearchBox, (_, _) => composing = true);
        SearchBox.AddHandler(TextCompositionManager.TextInputEvent,
            new TextCompositionEventHandler((_, _) => composing = false), handledEventsToo: true);
        SearchBox.LostKeyboardFocus += (_, _) => composing = false;
        SizeChanged += (_, _) =>
        {
            if (!recordingManualSize) return;
            preferredWidth = ActualWidth;
            preferredHeight = ActualHeight;
        };
        RefreshSnapshot();
    }

    public void ShowPanel()
    {
        CancelDrag();
        showingHistory = false;
        SnippetsTab.Style = (Style)FindResource("AccentButton");
        HistoryTab.Style = (Style)FindResource(typeof(System.Windows.Controls.Button));
        SearchBox.Clear();
        RefreshSnapshot(resetCategory: true);
        recordingManualSize = false;
        Show();
        PositionOnCursorScreen();
        recordingManualSize = true;
        Activate();
        SearchBox.Focus();
    }

    public void TogglePanel()
    {
        if (IsVisible) Hide();
        else ShowPanel();
    }

    public void AllowClose() => allowClose = true;

    public PanelResumeState CaptureResumeState() => new(SearchBox.Text, selectedCategory?.Id, showingHistory,
        (ResultList.SelectedItem as ResultItem)?.Id);

    public void ShowAfterUpdate(PanelResumeState state)
    {
        ShowPanel();
        showingHistory = state.History;
        SnippetsTab.Style = (Style)FindResource(state.History ? typeof(System.Windows.Controls.Button) : "AccentButton");
        HistoryTab.Style = (Style)FindResource(state.History ? "AccentButton" : typeof(System.Windows.Controls.Button));
        selectedCategory = CategoryChoices().FirstOrDefault(c => c.Id == state.CategoryId) ?? selectedCategory;
        SearchBox.Text = state.Search;
        RefreshSnapshot();
        if (state.SelectedId is { } selected)
            ResultList.SelectedItem = ResultList.Items.Cast<ResultItem>().FirstOrDefault(item => item.Id == selected);
        SearchBox.Focus();
    }

    private void PanelHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PanelTitle is null || HeaderActions is null) return;
        var wrap = PanelHeader.ActualWidth < PanelTitle.ActualWidth + HeaderActions.DesiredSize.Width + 16;
        Grid.SetRow(HeaderActions, wrap ? 1 : 0);
        HeaderActions.Margin = new Thickness(0, wrap ? 8 : 0, 0, 0);
    }

    private void Updates_Changed(object? sender, EventArgs e) => RenderUpdate();

    private void RenderUpdate()
    {
        var state = updates?.State ?? new(AppUpdatePhase.Unknown);
        var active = state.Phase is AppUpdatePhase.Downloading or AppUpdatePhase.Preparing;
        UpdateButton.Visibility = state.Phase is AppUpdatePhase.Unknown or AppUpdatePhase.Current ? Visibility.Collapsed : Visibility.Visible;
        UpdateButton.IsEnabled = !active;
        UpdateButton.ToolTip = $"更新至 {state.Version}；下載完成後會自動保存草稿並重啟。";
        UpdateProgressArea.Visibility = active || state.Phase == AppUpdatePhase.Failed ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.IsIndeterminate = state.Percent is null || state.Phase == AppUpdatePhase.Preparing;
        UpdateProgress.Value = state.Percent ?? 0;
        CancelUpdateButton.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        RetryUpdateButton.Visibility = state.Phase == AppUpdatePhase.Failed ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus.Text = state.Phase switch
        {
            AppUpdatePhase.Downloading => state.Percent is { } percent ? $"下載中 {percent}% · 完成後自動重啟" : "下載中 · 完成後自動重啟",
            AppUpdatePhase.Preparing => "正在準備重啟…",
            AppUpdatePhase.Failed => state.Error,
            _ => ""
        };
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (updates is not null) await updates.DownloadAsync();
    }
    private void CancelUpdate_Click(object sender, RoutedEventArgs e) => updates?.Cancel();

    private void PositionOnCursorScreen()
    {
        var cursor = Forms.Cursor.Position;
        var area = Forms.Screen.FromPoint(cursor).WorkingArea;
        var monitor = MonitorFromPoint(new NativePoint(cursor.X, cursor.Y), 2);
        var fallbackDpi = VisualTreeHelper.GetDpi(this);
        var scaleX = fallbackDpi.DpiScaleX;
        var scaleY = fallbackDpi.DpiScaleY;
        if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0)
        {
            scaleX = dpiX / 96.0;
            scaleY = dpiY / 96.0;
        }

        const double screenMargin = 12;
        var availableWidth = Math.Max(1, area.Width / scaleX - screenMargin * 2);
        var availableHeight = Math.Max(1, area.Height / scaleY - screenMargin * 2);
        MinWidth = Math.Min(360, availableWidth);
        MinHeight = Math.Min(450, availableHeight);
        Width = Math.Min(preferredWidth, availableWidth);
        Height = Math.Min(preferredHeight, availableHeight);
        Left = area.Left / scaleX + Math.Max(0, (area.Width / scaleX - Width) / 2);
        Top = area.Top / scaleY + Math.Max(0, (area.Height / scaleY - Height) / 2);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

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

    private void RefreshSnapshot(bool resetCategory = false)
    {
        try
        {
            snapshot = controller.GetSnapshot();
            isLoading = true;
            var choices = CategoryChoices();
            var defaultCategory = choices.FirstOrDefault(c => c.Id != OfficialPrompts.CategoryId);
            selectedCategory = !resetCategory && selectedCategory is not null
                ? choices.FirstOrDefault(c => c.Id == selectedCategory.Id) ?? defaultCategory
                : defaultCategory;
            RenderCategoryChips(choices);
            CategoryArea.Visibility = showingHistory ? Visibility.Collapsed : Visibility.Visible;
            isLoading = false;
            RefreshResults();
        }
        catch (Exception ex)
        {
            isLoading = false;
            StatusText.Text = "讀取失敗：" + ex.Message;
        }
    }

    private List<FilterChoice> CategoryChoices() => snapshot.Categories.OrderBy(c => c.SortOrder)
        .Select(c => new FilterChoice(c.Id, c.Name))
        .Append(new FilterChoice(OfficialPrompts.CategoryId, OfficialPrompts.CategoryName)).ToList();

    private void OfficialCategory_Click(object sender, RoutedEventArgs e) =>
        SelectCategory(new FilterChoice(OfficialPrompts.CategoryId, OfficialPrompts.CategoryName));

    private void SelectCategory(FilterChoice choice)
    {
        CancelDrag();
        selectedCategory = choice;
        UpdateCategoryStyles();
        RefreshResults();
        SearchBox.Focus();
    }

    private void UpdateCategoryStyles()
    {
        foreach (System.Windows.Controls.Button button in CategoryChips.Children)
            button.Style = (Style)FindResource(Equals(button.Tag, selectedCategory) ? "AccentButton" : typeof(System.Windows.Controls.Button));
        OfficialCategoryButton.Style = (Style)FindResource(selectedCategory?.Id == OfficialPrompts.CategoryId
            ? "AccentButton" : typeof(System.Windows.Controls.Button));
    }

    private void RenderCategoryChips(IReadOnlyList<FilterChoice> choices)
    {
        CategoryChips.Children.Clear();
        var normalStyle = (Style)FindResource(typeof(System.Windows.Controls.Button));
        var activeStyle = (Style)FindResource("AccentButton");
        foreach (var choice in choices.Where(c => c.Id != OfficialPrompts.CategoryId))
        {
            var chip = new System.Windows.Controls.Button
            {
                Content = choice.Name,
                Tag = choice,
                Margin = new Thickness(0, 0, 7, 7),
                Padding = new Thickness(11, 7, 11, 7),
                Style = choice == selectedCategory ? activeStyle : normalStyle
            };
            chip.Click += (_, _) => SelectCategory(choice);
            CategoryChips.Children.Add(chip);
        }
        UpdateCategoryStyles();
    }

    private void RefreshResults()
    {
        if (isLoading || ResultList is null) return;
        var terms = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Match(string text) => terms.All(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
        var category = selectedCategory;
        var searching = !string.IsNullOrWhiteSpace(SearchBox.Text);
        var official = category?.Id == OfficialPrompts.CategoryId;
        var canReorder = !showingHistory && !searching && !official;
        var results = showingHistory
            ? snapshot.History.OrderByDescending(h => h.CapturedAt)
                .Where(h => Match(h.Content))
                .Select(h => new ResultItem(h.Id, h.CapturedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm"), h.Content, h.Content, false, false)).ToList()
            : (official ? OfficialPrompts.Items : snapshot.Snippets).OrderBy(s => s.SortOrder)
                .Where(s => searching || category is null || s.CategoryId == category.Id)
                .Where(s => Match(s.Title + " " + s.Content))
                .Select(s => new ResultItem(s.Id, s.Title, s.Content, s.Content, true,
                    canReorder && (category is null || s.CategoryId == category.Id))).ToList();
        ResultList.ItemsSource = results;
        if (results.Count > 0) ResultList.SelectedIndex = 0;
        StatusText.Text = results.Count == 0 ? "沒有符合的文字" : $"{results.Count} 筆 · 點一下或按 Enter 複製";
    }

    private void CopySelected()
    {
        if (ResultList.SelectedItem is not ResultItem item) return;
        if (controller.CopyText(item.Content)) Hide();
        else StatusText.Text = "剪貼簿暫時無法寫入，請重試";
    }

    private void SnippetsTab_Click(object sender, RoutedEventArgs e)
    {
        showingHistory = false;
        SnippetsTab.Style = (Style)FindResource("AccentButton");
        HistoryTab.Style = (Style)FindResource(typeof(System.Windows.Controls.Button));
        CategoryArea.Visibility = Visibility.Visible;
        RefreshResults();
    }

    private void HistoryTab_Click(object sender, RoutedEventArgs e)
    {
        showingHistory = true;
        HistoryTab.Style = (Style)FindResource("AccentButton");
        SnippetsTab.Style = (Style)FindResource(typeof(System.Windows.Controls.Button));
        CategoryArea.Visibility = Visibility.Collapsed;
        RefreshResults();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null)
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        RefreshResults();
    }
    private void ResultList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (pendingDragId is Guid moving)
        {
            var hovered = dragActive
                ? FindAncestor<ListBoxItem>(ResultList.InputHitTest(e.GetPosition(ResultList)) as DependencyObject)?.DataContext as ResultItem
                : null;
            var target = hovered is not null && hovered.Id == highlightedTargetId ? hovered : null;
            CancelDrag();
            if (target is not null) ReorderDroppedSnippet(moving, target);
            e.Handled = true;
            return;
        }
        if (FindAncestor<Border>(e.OriginalSource as DependencyObject, "DragHandle") is not null)
        {
            e.Handled = true;
            return;
        }
        var row = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (row?.DataContext is not ResultItem item) return;
        ResultList.SelectedItem = item;
        CopySelected();
        e.Handled = true;
    }

    private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: ResultItem { CanReorder: true } item }) return;
        pendingDragId = item.Id;
        pendingDragRow = FindAncestor<ListBoxItem>(sender as DependencyObject);
        dragStart = e.GetPosition(ResultList);
        if (e.LeftButton == MouseButtonState.Pressed) ResultList.CaptureMouse();
        e.Handled = true;
    }

    private void DragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CancelDrag();
        e.Handled = true;
    }

    private void ResultList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        UpdatePendingDrag(e.GetPosition(ResultList), e.LeftButton == MouseButtonState.Pressed);
        if (dragActive) e.Handled = true;
    }

    private void UpdatePendingDrag(Point position, bool leftPressed)
    {
        if (pendingDragId is null) return;
        if (!leftPressed)
        {
            CancelDrag();
            return;
        }
        if (!dragActive)
        {
            if ((position - dragStart).Length < 7) return;
            dragActive = true;
            if (pendingDragRow is not null) dragPreview = DragRowPreview.Begin(ResultList, pendingDragRow);
        }
        dragPreview?.UpdatePosition();
        UpdateDropTarget(position);
    }

    private void UpdateDropTarget(Point position)
    {
        if (!dragActive || pendingDragId is not Guid sourceId) { ClearDropTarget(); return; }
        var targetRow = FindAncestor<ListBoxItem>(ResultList.InputHitTest(position) as DependencyObject);
        if (targetRow?.DataContext is not ResultItem target ||
            showingHistory || !string.IsNullOrWhiteSpace(SearchBox.Text) ||
            !target.IsSnippet || !target.CanReorder || target.Id == sourceId)
        {
            ClearDropTarget();
            return;
        }
        var categoryId = selectedCategory?.Id;
        var order = snapshot.Snippets.Where(s => s.CategoryId == categoryId)
            .OrderBy(s => s.SortOrder).Select(s => s.Id).ToList();
        var sourceIndex = order.IndexOf(sourceId);
        var targetIndex = order.IndexOf(target.Id);
        if (sourceIndex < 0 || targetIndex < 0) { ClearDropTarget(); return; }
        if (dropTargetIndicator is null)
            dropTargetIndicator = DropTargetIndicator.Show(targetRow);
        else
            dropTargetIndicator.Update(targetRow);
        highlightedTargetId = target.Id;
    }

    private void ClearDropTarget()
    {
        dropTargetIndicator?.Dispose();
        dropTargetIndicator = null;
        highlightedTargetId = null;
    }

    private void CancelDrag()
    {
        pendingDragId = null;
        pendingDragRow = null;
        dragActive = false;
        ClearDropTarget();
        dragPreview?.Dispose();
        dragPreview = null;
        if (Mouse.Captured == ResultList) ResultList.ReleaseMouseCapture();
    }

    private void ReorderDroppedSnippet(Guid sourceId, ResultItem target)
    {
        if (OfficialPrompts.IsOfficial(selectedCategory?.Id) || OfficialPrompts.IsOfficial(sourceId) || OfficialPrompts.IsOfficial(target.Id)) return;
        if (showingHistory || !string.IsNullOrWhiteSpace(SearchBox.Text) ||
            !target.IsSnippet || !target.CanReorder || sourceId == target.Id) return;
        var categoryId = selectedCategory?.Id;
        if (!snapshot.Snippets.Any(s => s.Id == sourceId && s.CategoryId == categoryId) ||
            !snapshot.Snippets.Any(s => s.Id == target.Id && s.CategoryId == categoryId)) return;
        var current = snapshot.Snippets.Where(s => s.CategoryId == categoryId).OrderBy(s => s.SortOrder).Select(s => s.Id).ToList();
        var order = SnippetReorder.MoveRelativeToTarget(current, sourceId, target.Id);
        if (order is null) return;
        try { controller.ReorderSnippets(categoryId, order); RefreshSnapshot(); }
        catch (Exception ex) { StatusText.Text = "排序失敗：" + ex.Message; }
    }

    private static T? FindAncestor<T>(DependencyObject? element, string? name = null) where T : FrameworkElement
    {
        while (element is not null)
        {
            if (element is T match && (name is null || match.Name == name)) return match;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }

    private void Manager_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ManagerRequested?.Invoke(this, EventArgs.Empty);
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
    private void Window_Deactivated(object sender, EventArgs e) { CancelDrag(); Hide(); }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CancelDrag(); Hide(); e.Handled = true; return; }
        if (e.Key == Key.Enter)
        {
            if (Keyboard.FocusedElement is DependencyObject focused && FindAncestor<System.Windows.Controls.Button>(focused) is not null) return;
            if (composing) return;
            CopySelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocusWithin)
        {
            ResultList.SelectedIndex = Math.Min(ResultList.Items.Count - 1, ResultList.SelectedIndex + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && SearchBox.IsKeyboardFocusWithin)
        {
            ResultList.SelectedIndex = Math.Max(0, ResultList.SelectedIndex - 1);
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!allowClose) { e.Cancel = true; Hide(); }
        else
        {
            controller.Changed -= Controller_Changed;
            if (updates is not null) updates.Changed -= Updates_Changed;
            controller.SettingsChanged -= Controller_SettingsChanged;
        }
    }
}
