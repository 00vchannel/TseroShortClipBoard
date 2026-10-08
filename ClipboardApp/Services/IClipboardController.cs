namespace ClipboardApp.Services;

public sealed record ClipboardSnapshot(
    IReadOnlyList<CategoryView> Categories,
    IReadOnlyList<SnippetView> Snippets,
    IReadOnlyList<HistoryView> History,
    IReadOnlyList<SnippetView> Trash,
    IReadOnlyDictionary<Guid, DraftView> Drafts,
    bool IsHistoryPaused);

public sealed record CategoryView(Guid Id, string Name, int SortOrder);
public sealed record SnippetView(Guid Id, string Title, string Emoji, string Content, Guid? CategoryId, int SortOrder, DateTimeOffset UpdatedAt);
public sealed record HistoryView(Guid Id, string Content, DateTimeOffset CapturedAt);
public sealed record DraftView(Guid SnippetId, string Title, string Emoji, string Content, Guid? CategoryId, DateTimeOffset UpdatedAt);
public sealed record ClipboardSettings(string Hotkey, bool Autostart, double FontScale, string Theme, string BackupDirectory,
    bool AutostartReadable = true);

public interface IClipboardController
{
    event EventHandler? Changed;
    event EventHandler? SettingsChanged;
    ClipboardSnapshot GetSnapshot();
    ClipboardSettings GetSettings();
    void SetHotkey(string hotkey);
    void SetAutostart(bool enabled);
    void SetFontScale(double scale);
    void SetTheme(string theme);
    void SetBackupDirectory(string path);
    Guid SaveSnippet(Guid? id, string title, string emoji, string content, Guid? categoryId);
    void SaveDraft(Guid id, string title, string emoji, string content, Guid? categoryId);
    void DiscardDraft(Guid id);
    void DeleteSnippets(IEnumerable<Guid> ids);
    void RestoreSnippet(Guid id);
    void PermanentlyDeleteSnippets(IEnumerable<Guid> ids);
    void MoveSnippets(IEnumerable<Guid> ids, Guid? categoryId);
    void ReorderSnippets(Guid? categoryId, IReadOnlyList<Guid> orderedIds);
    Guid AddCategory(string name);
    void RenameCategory(Guid id, string name);
    void DeleteCategory(Guid id);
    void ReorderCategories(IReadOnlyList<Guid> ids);
    void SetHistoryPaused(bool paused);
    void ClearHistory();
    Guid PromoteHistory(Guid id);
    bool CopyText(string text);
    string CreateBackup();
    void RestoreBackup(string path);
}
