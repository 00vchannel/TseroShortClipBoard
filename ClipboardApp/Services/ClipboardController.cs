using System.Windows;
using System.IO;
using Microsoft.Win32;
using ClipboardCore;

namespace ClipboardApp.Services;

public sealed class ClipboardController : IClipboardController
{
    private const string AutostartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutostartValue = "ZeroZeroClipboardV2";
    private readonly ClipboardRepository _repository;
    private readonly string _defaultBackupDirectory;
    private DateOnly? _lastAutomaticBackup;
    private string? _suppressClipboardText;
    private bool _historyErrorReported;

    public event EventHandler? Changed;
    public event EventHandler? SettingsChanged;
    public event EventHandler<string>? BackupFailed;
    public event EventHandler<string>? HistoryFailed;

    public ClipboardController(ClipboardRepository repository, string dataDirectory)
    {
        _repository = repository;
        _defaultBackupDirectory = Path.Combine(dataDirectory, "backups");
    }

    public ClipboardSettings GetSettings()
    {
        var hotkey = _repository.GetSetting("hotkey") ?? "right alt";
        if (!GlobalHotkeyHook.IsSupportedHotkey(hotkey)) hotkey = "right alt";
        var scale = double.TryParse(_repository.GetSetting("font_scale"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 1.0;
        if (!double.IsFinite(scale) || scale is < 0.75 or > 1.75) scale = 1.0;
        var theme = _repository.GetSetting("theme");
        var autostart = GetAutostart();
        return new ClipboardSettings(hotkey, autostart ?? false, scale,
            theme is "light" ? "light" : "dark", _repository.GetSetting("backup_directory") ?? _defaultBackupDirectory,
            AutostartReadable: autostart.HasValue);
    }

    public void SetHotkey(string hotkey)
    {
        if (!GlobalHotkeyHook.IsSupportedHotkey(hotkey)) throw new ArgumentException("不支援的快捷鍵", nameof(hotkey));
        _repository.SetSetting("hotkey", hotkey);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        NotifySaved();
    }

    public void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AutostartKey, writable: true)
            ?? throw new InvalidOperationException("無法開啟 Windows 開機啟動設定");
        var previous = key.GetValue(AutostartValue);
        var previousKind = previous is null ? RegistryValueKind.String : key.GetValueKind(AutostartValue);
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("找不到程式位置");
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("目前不是可開機啟動的發佈版本");
            key.SetValue(AutostartValue, $"\"{path}\"");
        }
        else key.DeleteValue(AutostartValue, throwOnMissingValue: false);
        try { _repository.SetSetting("autostart", enabled ? "true" : "false"); }
        catch
        {
            if (previous is null) key.DeleteValue(AutostartValue, throwOnMissingValue: false);
            else key.SetValue(AutostartValue, previous, previousKind);
            throw;
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        NotifySaved();
    }

    private bool? GetAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutostartKey);
            return Environment.ProcessPath is { } path &&
                string.Equals(key?.GetValue(AutostartValue) as string, $"\"{path}\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
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
        _lastAutomaticBackup = null;
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
        if (string.IsNullOrEmpty(text)) return false;
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
            _historyErrorReported = false;
        }
        catch (System.Runtime.InteropServices.ExternalException) { /* Clipboard can be temporarily locked by another app. */ }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            if (_historyErrorReported) return;
            _historyErrorReported = true;
            HistoryFailed?.Invoke(this, ex.Message);
        }
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
        _lastAutomaticBackup = null;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        NotifySaved();
    }

    private void NotifySaved()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_lastAutomaticBackup == today) return;
        try
        {
            _repository.CreateDailyBackup(directory: GetSettings().BackupDirectory);
            _lastAutomaticBackup = today;
        }
        catch (Exception ex)
        {
            BackupFailed?.Invoke(this, ex.Message);
        }
    }

    private static SnippetView ToView(Snippet item) => new(item.Id, item.Title, item.Emoji, item.Content, item.CategoryId, item.SortOrder, DateTimeOffset.MinValue);
}
