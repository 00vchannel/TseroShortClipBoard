using ClipboardApp.Services;

internal sealed class ProbeController(ClipboardSnapshot snapshot) : IClipboardController
{
    public event EventHandler? Changed { add { } remove { } }
    public event EventHandler? SettingsChanged;
    public List<string> Copied { get; } = [];
    public IReadOnlyList<Guid>? LastOrderedIds { get; private set; }
    public string? LastSavedEmoji { get; private set; }
    public List<Guid> DeletedIds { get; } = [];
    public string Theme { get; private set; } = "dark";
    public double FontScale { get; private set; } = 1;

    public ClipboardSnapshot GetSnapshot() => snapshot;
    public ClipboardSettings GetSettings() => new("right alt", false, FontScale, Theme, "");
    public void ChangeTheme(string theme) { Theme = theme; SettingsChanged?.Invoke(this, EventArgs.Empty); }
    public void ChangeFontScale(double scale) { FontScale = scale; SettingsChanged?.Invoke(this, EventArgs.Empty); }
    public bool CopyText(string text) { Copied.Add(text); return true; }
    public void ReorderSnippets(Guid? categoryId, IReadOnlyList<Guid> orderedIds) => LastOrderedIds = orderedIds.ToArray();
    public void SetHotkey(string hotkey) => throw new NotSupportedException();
    public void SetAutostart(bool enabled) => throw new NotSupportedException();
    public void SetFontScale(double scale) => throw new NotSupportedException();
    public void SetTheme(string theme) => throw new NotSupportedException();
    public void SetBackupDirectory(string path) => throw new NotSupportedException();
    public Guid SaveSnippet(Guid? id, string title, string emoji, string content, Guid? categoryId)
    { LastSavedEmoji = emoji; return id ?? Guid.NewGuid(); }
    public void SaveDraft(Guid id, string title, string emoji, string content, Guid? categoryId) { }
    public void DiscardDraft(Guid id) { }
    public void DeleteSnippets(IEnumerable<Guid> ids) => DeletedIds.AddRange(ids);
    public void RestoreSnippet(Guid id) => throw new NotSupportedException();
    public void PermanentlyDeleteSnippets(IEnumerable<Guid> ids) => throw new NotSupportedException();
    public void MoveSnippets(IEnumerable<Guid> ids, Guid? categoryId) => throw new NotSupportedException();
    public Guid AddCategory(string name) => throw new NotSupportedException();
    public void RenameCategory(Guid id, string name) => throw new NotSupportedException();
    public void DeleteCategory(Guid id) => throw new NotSupportedException();
    public void ReorderCategories(IReadOnlyList<Guid> ids) => throw new NotSupportedException();
    public void SetHistoryPaused(bool paused) => throw new NotSupportedException();
    public void ClearHistory() => throw new NotSupportedException();
    public Guid PromoteHistory(Guid id) => throw new NotSupportedException();
    public string CreateBackup() => throw new NotSupportedException();
    public void RestoreBackup(string path) => throw new NotSupportedException();
}
