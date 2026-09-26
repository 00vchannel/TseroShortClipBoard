using System.Windows;
using System.IO;
using Microsoft.Win32;
using ClipboardCore;

namespace ClipboardApp.Services;

public sealed class ClipboardController : IClipboardController
{
    private readonly ClipboardRepository _repository;
    private readonly string _defaultBackupDirectory;
    private DateOnly? _lastAutomaticBackup;
    private string? _suppressClipboardText;

    public event EventHandler? Changed;
    public event EventHandler? SettingsChanged;
    public event EventHandler<string>? BackupFailed;

    public ClipboardController(ClipboardRepository repository, string dataDirectory)
    {
        _repository = repository;
        _defaultBackupDirectory = Path.Combine(dataDirectory, "backups");
    }

    public ClipboardSettings GetSettings()
    {
        var hotkey = _repository.GetSetting("hotkey") ?? "right alt";
        var scale = double.TryParse(_repository.GetSetting("font_scale"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 1.0;
        return new ClipboardSettings(hotkey, _repository.GetSetting("autostart") == "true", scale,
            _repository.GetSetting("theme") ?? "dark", _repository.GetSetting("backup_directory") ?? _defaultBackupDirectory);
    }

    public void SetHotkey(string hotkey)
    {
        if (!RightAltHook.IsSupportedHotkey(hotkey)) throw new ArgumentException("不支援的快捷鍵", nameof(hotkey));
        _repository.SetSetting("hotkey", hotkey);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        NotifySaved();
    }

    public void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
            ?? throw new InvalidOperationException("無法開啟 Windows 開機啟動設定");
        const string valueName = "ZeroZeroClipboardV2";
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("找不到程式位置");
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("目前不是可開機啟動的發佈版本");
            key.SetValue(valueName, $"\"{path}\"");
        }
        else key.DeleteValue(valueName, throwOnMissingValue: false);
        _repository.SetSetting("autostart", enabled ? "true" : "false");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        NotifySaved();
    }

    public void SetFontScale(double scale)
    {
        if (scale is < 0.75 or > 1.75 || double.IsNaN(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
        _repository.SetSetting("font_scale", scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        NotifySaved();
    }

    public void SetTheme(string theme)
    {
        if (theme is not ("dark" or "light")) throw new ArgumentException("不支援的主題", nameof(theme));
        _repository.SetSetting("theme", theme);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        NotifySaved();
    }

    public void SetBackupDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("備份位置需使用完整路徑", nameof(path));
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(path);
        var sample = Path.Combine(path, $"daily-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        _repository.CreateBackup(sample);
        _repository.SetSetting("backup_directory", path);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        NotifySaved();
    }

    public ClipboardSnapshot GetSnapshot()
    {
        return new ClipboardSnapshot(
            _repository.ListCategories().Select(x => new CategoryView(x.Id, x.Name, x.SortOrder)).ToList(),
            _repository.ListSnippets().Select(ToView).ToList(),
            _repository.ListHistory().Select(x => new HistoryView(x.Id, x.Content, x.CopiedAtUtc)).ToList(),
            _repository.ListSnippets(includeDeleted: true).Select(ToView).ToList(),
            _repository.ListDrafts().ToDictionary(x => x.SnippetId, x => new DraftView(x.SnippetId, x.Title, x.Emoji, x.Content, x.CategoryId, x.UpdatedAtUtc)),
            _repository.GetHistoryPaused());
    }

    public Guid SaveSnippet(Guid? id, string title, string emoji, string content, Guid? categoryId)
    {
        var saved = _repository.SaveSnippet(new Snippet { Id = id ?? Guid.Empty, Title = title, Emoji = emoji, Content = content, CategoryId = categoryId });
        NotifySaved();
        return saved.Id;
    }

    public void SaveDraft(Guid id, string title, string emoji, string content, Guid? categoryId)
    {
        _repository.SaveDraft(new SnippetDraft { SnippetId = id, Title = title, Emoji = emoji, Content = content, CategoryId = categoryId });
        NotifySaved();
    }

    public void DiscardDraft(Guid id) { _repository.DeleteDraft(id); NotifySaved(); }
    public void DeleteSnippets(IEnumerable<Guid> ids) { _repository.DeleteSnippets(ids.ToArray()); NotifySaved(); }
    public void RestoreSnippet(Guid id) { _repository.RestoreSnippets([id]); NotifySaved(); }
    public void PermanentlyDeleteSnippets(IEnumerable<Guid> ids) { _repository.PermanentlyDeleteSnippets(ids.ToArray()); NotifySaved(); }
    public void MoveSnippets(IEnumerable<Guid> ids, Guid? categoryId) { _repository.MoveSnippets(ids.ToArray(), categoryId); NotifySaved(); }
    public void ReorderSnippets(Guid? categoryId, IReadOnlyList<Guid> orderedIds) { _repository.ReorderSnippets(categoryId, orderedIds); NotifySaved(); }
    public Guid AddCategory(string name) { var category = _repository.AddCategory(name); NotifySaved(); return category.Id; }
    public void RenameCategory(Guid id, string name) { _repository.RenameCategory(id, name); NotifySaved(); }
    public void DeleteCategory(Guid id) { _repository.DeleteCategory(id); NotifySaved(); }
    public void ReorderCategories(IReadOnlyList<Guid> ids) { _repository.ReorderCategories(ids); NotifySaved(); }
    public void SetHistoryPaused(bool paused) { _repository.SetHistoryPaused(paused); NotifySaved(); }
    public void ClearHistory() { _repository.ClearHistory(); NotifySaved(); }

    public Guid PromoteHistory(Guid id)
    {
        var item = _repository.ListHistory().FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("找不到複製歷史");
        return SaveSnippet(null, item.Content.Split('\n')[0].TrimEnd('\r') is { Length: > 0 } title ? title[..Math.Min(60, title.Length)] : "未命名文字", "", item.Content, null);
    }

    public bool CopyText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                _suppressClipboardText = text;
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(40);
            }
        }
        _suppressClipboardText = null;
        return false;
    }

    public void OnClipboardChanged()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText()) return;
            var content = System.Windows.Clipboard.GetText();
            if (_suppressClipboardText == content)
            {
                _suppressClipboardText = null;
                return;
            }
            _suppressClipboardText = null;
            if (_repository.AddHistory(content) is not null) NotifySaved();
        }
        catch (System.Runtime.InteropServices.ExternalException) { /* Clipboard can be temporarily locked by another app. */ }
    }

    public string CreateBackup()
    {
        var directory = GetSettings().BackupDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db");
        _repository.CreateBackup(path);
        return path;
    }

    public void RestoreBackup(string path)
    {
        _repository.RestoreBackup(path);
        NotifySaved();
    }

    private void NotifySaved()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_lastAutomaticBackup == today) return;
        try
        {
            var directory = GetSettings().BackupDirectory;
            if (Path.GetFullPath(directory).Equals(Path.GetFullPath(_defaultBackupDirectory), StringComparison.OrdinalIgnoreCase))
                _repository.CreateDailyBackup();
            else
            {
                Directory.CreateDirectory(directory);
                if (!Directory.EnumerateFiles(directory, $"daily-{today:yyyyMMdd}-*.db").Any())
                {
                    _repository.CreateBackup(Path.Combine(directory, $"daily-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db"));
                    foreach (var old in Directory.EnumerateFiles(directory, "daily-*.db").OrderByDescending(File.GetCreationTimeUtc).Skip(30))
                        File.Delete(old);
                }
            }
            _lastAutomaticBackup = today;
        }
        catch (Exception ex)
        {
            BackupFailed?.Invoke(this, ex.Message);
        }
    }

    private static SnippetView ToView(Snippet item) => new(item.Id, item.Title, item.Emoji, item.Content, item.CategoryId, item.SortOrder, DateTimeOffset.MinValue);
}
