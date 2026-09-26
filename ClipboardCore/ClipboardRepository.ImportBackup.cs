using System.Text.Json;

namespace ClipboardCore;

public sealed partial class ClipboardRepository
{
    public ImportResult ImportLegacy(string jsonPath, string? settingsPath = null)
    {
        // Parse and validate the entire source before touching the destination database.
        var source = ParseLegacy(jsonPath, settingsPath);
        Initialize();
        using var db = Open();
        db.Transaction(() =>
        {
            if (db.Scalar("SELECT value FROM meta WHERE key='legacy_imported'", r => r.Text(0)) is not null ||
                db.Scalar("SELECT 1 FROM categories LIMIT 1", r => r.Number(0)) == 1 ||
                db.Scalar("SELECT 1 FROM snippets LIMIT 1", r => r.Number(0)) == 1 ||
                db.Scalar("SELECT 1 FROM history LIMIT 1", r => r.Number(0)) == 1)
                throw new InvalidOperationException("只能匯入全新的空資料庫");

            var categoryIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var order = 0;
            foreach (var name in source.Categories)
            {
                if (name is "All" or "Copied") continue;
                var id = Guid.NewGuid();
                categoryIds.Add(name, id);
                db.Execute("INSERT INTO categories(id,name,sort_order) VALUES(?,?,?)", id, name, order++);
            }

            var snippets = source.Snippets.Where(s => s.Category != "Copied").ToList();
            var history = source.Snippets.Where(s => s.Category == "Copied").ToList();
            for (var i = 0; i < snippets.Count; i++)
            {
                var item = snippets[i];
                var categoryId = item.Category == "All" ? (Guid?)null : categoryIds[item.Category];
                db.Execute("INSERT INTO snippets(id,emoji,title,category_id,content,sort_order,deleted_at) VALUES(?,?,?,?,?,?,NULL)",
                           Guid.NewGuid(), item.Emoji, item.Title, categoryId, item.Content, i);
            }
            for (var i = 0; i < history.Count; i++)
            {
                // The old JSON has neither history IDs nor timestamps. Preserve newest-first order.
                db.Execute("INSERT INTO history(id,content,copied_at,sort_order) VALUES(?,?,?,?)",
                           Guid.NewGuid(), history[i].Content, DateTimeOffset.UtcNow.AddMilliseconds(-i), i);
            }
            foreach (var pair in source.Settings)
                db.Execute("INSERT INTO settings(key,value) VALUES(?,?)", pair.Key,
                           pair.Key == "autostart" ? "false" : pair.Value);
            db.Execute("INSERT INTO meta(key,value) VALUES('legacy_imported','1')");

            // A transaction cannot be committed with a partial or changed import.
            var savedSnippets = db.Query("SELECT emoji,title,content FROM snippets ORDER BY sort_order", r =>
                (Emoji: r.Text(0)!, Title: r.Text(1)!, Content: r.Text(2)!));
            if (savedSnippets.Count != snippets.Count || savedSnippets.Where((s, i) =>
                    s.Emoji != snippets[i].Emoji || s.Title != snippets[i].Title || s.Content != snippets[i].Content).Any())
                throw new InvalidDataException("文字匯入核對失敗");
            var savedHistory = db.Query("SELECT content FROM history ORDER BY sort_order", r => r.Text(0)!);
            if (savedHistory.Count != history.Count || savedHistory.Where((s, i) => s != history[i].Content).Any())
                throw new InvalidDataException("歷史匯入核對失敗");
            var savedCategories = db.Query("SELECT name FROM categories ORDER BY sort_order", r => r.Text(0)!);
            var expectedCategories = source.Categories.Where(c => c is not ("All" or "Copied")).ToList();
            if (!savedCategories.SequenceEqual(expectedCategories)) throw new InvalidDataException("分類匯入核對失敗");
        });
        ValidateDatabase(db);
        return new ImportResult(source.Snippets.Count - source.HistoryCount, source.Categories.Count, source.HistoryCount);
    }

    public void CreateBackup(string destinationPath)
    {
        destinationPath = Path.GetFullPath(destinationPath);
        if (File.Exists(destinationPath)) throw new IOException("備份檔已存在，不能覆寫");
        var directory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(directory);
        var tempPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var source = Open())
            using (var destination = new SqliteDb(tempPath))
            {
                ValidateDatabase(source);
                source.BackupTo(destination);
                ValidateDatabase(destination);
                destination.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
                destination.Execute("PRAGMA journal_mode=DELETE");
            }
            File.Move(tempPath, destinationPath);
        }
        finally
        {
            RemoveIfExists(tempPath);
            RemoveIfExists(tempPath + "-wal");
            RemoveIfExists(tempPath + "-shm");
        }
    }

    /// <summary>Restores a verified backup. Automatically saves the current database first.</summary>
    public string RestoreBackup(string sourcePath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        if (string.Equals(sourcePath, DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("不能用目前正在使用的資料庫作為還原來源", nameof(sourcePath));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("備份檔不存在", sourcePath);
        using (var source = new SqliteDb(sourcePath, readOnly: true)) ValidateDatabase(source);
        Initialize();
        var safetyDirectory = Path.Combine(_dataDirectory, "backups");
        Directory.CreateDirectory(safetyDirectory);
        var safetyPath = Path.Combine(safetyDirectory, $"before-restore-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.sqlite");
        CreateBackup(safetyPath);

        var stagingPath = DatabasePath + ".restore-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var source = new SqliteDb(sourcePath, readOnly: true))
            using (var staging = new SqliteDb(stagingPath))
            {
                source.BackupTo(staging);
                ValidateDatabase(staging);
                if (staging.Scalar("SELECT value FROM meta WHERE key='schema_version'", r => r.Text(0)) != "1")
                    throw new InvalidDataException("備份資料格式不受支援");
                staging.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
                staging.Execute("PRAGMA journal_mode=DELETE");
            }
            using (var current = Open())
            {
                current.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
                current.Execute("PRAGMA journal_mode=DELETE");
            }
            File.Replace(stagingPath, DatabasePath, null);
            RemoveIfExists(DatabasePath + "-wal");
            RemoveIfExists(DatabasePath + "-shm");
            Initialize();
            return safetyPath;
        }
        finally
        {
            RemoveIfExists(stagingPath);
            RemoveIfExists(stagingPath + "-wal");
            RemoveIfExists(stagingPath + "-shm");
        }
    }

    public string? CreateDailyBackup(int retainedCount = 30)
    {
        if (retainedCount < 1) throw new ArgumentOutOfRangeException(nameof(retainedCount));
        var backupDirectory = Path.Combine(_dataDirectory, "backups");
        Directory.CreateDirectory(backupDirectory);
        var today = DateTimeOffset.Now.ToString("yyyyMMdd");
        if (Directory.GetFiles(backupDirectory, $"daily-{today}-*.sqlite").Length > 0) return null;
        var path = Path.Combine(backupDirectory, $"daily-{today}-{DateTimeOffset.Now:HHmmss}-{Guid.NewGuid():N}.sqlite");
        CreateBackup(path);
        foreach (var old in Directory.GetFiles(backupDirectory, "daily-*.sqlite")
                     .OrderByDescending(File.GetCreationTimeUtc).Skip(retainedCount))
            File.Delete(old);
        return path;
    }

    private static void RemoveIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static LegacySource ParseLegacy(string jsonPath, string? settingsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("舊資料根節點不是物件");
        var categoriesArray = RequiredArray(root, "categories");
        var categories = new List<string>();
        foreach (var item in categoriesArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                throw new InvalidDataException("舊分類名稱無效");
            categories.Add(item.GetString()!);
        }
        if (categories.Count != categories.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException("舊分類存在重複名稱");
        var snippets = new List<LegacySnippet>();
        foreach (var item in RequiredArray(root, "snippets").EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("舊文字項目不是物件");
            var snippet = new LegacySnippet(RequiredString(item, "emoji"), RequiredString(item, "title"),
                                            RequiredString(item, "category"), RequiredString(item, "content"));
            if (!categories.Contains(snippet.Category, StringComparer.Ordinal))
                throw new InvalidDataException("舊文字項目引用不存在的分類");
            snippets.Add(snippet);
        }
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (settingsPath is not null)
        {
            using var settingsDocument = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (settingsDocument.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("舊設定不是物件");
            foreach (var pair in settingsDocument.RootElement.EnumerateObject())
                settings.Add(pair.Name, pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString()! : pair.Value.GetRawText());
        }
        var historyCount = snippets.Count(s => s.Category == "Copied");
        if (historyCount > 50) throw new InvalidDataException("舊歷史超過 50 筆，須先確認處理方式");
        return new LegacySource(categories, snippets, settings, historyCount);
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"舊資料缺少 {name} 陣列");
        return value;
    }
    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"舊資料缺少 {name} 文字欄位");
        return value.GetString()!;
    }
    private sealed record LegacySnippet(string Emoji, string Title, string Category, string Content);
    private sealed record LegacySource(List<string> Categories, List<LegacySnippet> Snippets, Dictionary<string, string> Settings, int HistoryCount);
}
