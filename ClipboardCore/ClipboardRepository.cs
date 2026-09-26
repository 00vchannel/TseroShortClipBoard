using System.Text.Json;

namespace ClipboardCore;

/// <summary>Windows-only, local SQLite storage. Each operation uses a short-lived connection.</summary>
public sealed partial class ClipboardRepository(string dataDirectory)
{
    private readonly string _dataDirectory = Path.GetFullPath(dataDirectory);
    private string DatabasePath => Path.Combine(_dataDirectory, "clipboard.db");

    public void Initialize()
    {
        Directory.CreateDirectory(_dataDirectory);
        using var db = Open();
        db.Transaction(() =>
        {
            db.Execute("CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
            db.Execute("CREATE TABLE IF NOT EXISTS categories(id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE, sort_order INTEGER NOT NULL)");
            db.Execute("CREATE TABLE IF NOT EXISTS snippets(id TEXT PRIMARY KEY, emoji TEXT NOT NULL, title TEXT NOT NULL, category_id TEXT REFERENCES categories(id) ON DELETE SET NULL, content TEXT NOT NULL, sort_order INTEGER NOT NULL, deleted_at TEXT)");
            db.Execute("CREATE TABLE IF NOT EXISTS drafts(snippet_id TEXT PRIMARY KEY, emoji TEXT NOT NULL, title TEXT NOT NULL, category_id TEXT REFERENCES categories(id) ON DELETE SET NULL, content TEXT NOT NULL, updated_at TEXT NOT NULL)");
            db.Execute("CREATE TABLE IF NOT EXISTS history(id TEXT PRIMARY KEY, content TEXT NOT NULL, copied_at TEXT NOT NULL, sort_order INTEGER NOT NULL)");
            db.Execute("CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
            db.Execute("CREATE INDEX IF NOT EXISTS idx_snippets_category_order ON snippets(category_id, deleted_at, sort_order)");
            var version = db.Scalar("SELECT value FROM meta WHERE key='schema_version'", r => r.Text(0));
            if (version is null) db.Execute("INSERT INTO meta(key,value) VALUES('schema_version','1')");
            else if (version != "1") throw new InvalidDataException($"資料格式版本 {version} 不受支援");
        });
        ValidateDatabase(db);
    }

    public IReadOnlyList<Category> ListCategories()
    {
        using var db = Open();
        return db.Query("SELECT id,name,sort_order FROM categories ORDER BY sort_order", ReadCategory);
    }

    public Category AddCategory(string name)
    {
        name = ValidateCategoryName(name);
        using var db = Open();
        var category = new Category { Id = Guid.NewGuid(), Name = name, SortOrder = NextOrder(db, "categories") };
        db.Execute("INSERT INTO categories(id,name,sort_order) VALUES(?,?,?)", category.Id, category.Name, category.SortOrder);
        return category;
    }

    public void RenameCategory(Guid id, string name)
    {
        name = ValidateCategoryName(name);
        using var db = Open();
        RequireCategory(db, id);
        db.Execute("UPDATE categories SET name=? WHERE id=?", name, id);
    }

    public void DeleteCategory(Guid id)
    {
        using var db = Open();
        db.Transaction(() =>
        {
            RequireCategory(db, id);
            db.Execute("UPDATE snippets SET category_id=NULL WHERE category_id=?", id);
            db.Execute("UPDATE drafts SET category_id=NULL WHERE category_id=?", id);
            db.Execute("DELETE FROM categories WHERE id=?", id);
        });
    }

    public void ReorderCategories(IReadOnlyList<Guid> ids)
    {
        using var db = Open();
        db.Transaction(() =>
        {
            RequireExactIds(ids, db.Query("SELECT id FROM categories", r => r.Guid(0)));
            for (var i = 0; i < ids.Count; i++) db.Execute("UPDATE categories SET sort_order=? WHERE id=?", i, ids[i]);
        });
    }

    public IReadOnlyList<Snippet> ListSnippets(Guid? categoryId = null, string? query = null, bool includeDeleted = false)
    {
        using var db = Open();
        var sql = "SELECT id,emoji,title,category_id,content,sort_order,deleted_at FROM snippets WHERE " +
                  (includeDeleted ? "deleted_at IS NOT NULL" : "deleted_at IS NULL") +
                  (categoryId is null ? "" : " AND category_id=?") + " ORDER BY sort_order,id";
        var snippets = db.Query(sql, ReadSnippet, categoryId is null ? [] : [categoryId.Value]);
        if (string.IsNullOrWhiteSpace(query)) return snippets;
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return snippets.Where(s => terms.All(term => s.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                                      s.Content.Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public Snippet? GetSnippet(Guid id)
    {
        using var db = Open();
        return db.Scalar("SELECT id,emoji,title,category_id,content,sort_order,deleted_at FROM snippets WHERE id=?", ReadSnippet, id);
    }

    public Snippet SaveSnippet(Snippet snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        using var db = Open();
        Snippet saved = snippet;
        db.Transaction(() =>
        {
            if (snippet.CategoryId is Guid categoryId) RequireCategory(db, categoryId);
            var id = snippet.Id == Guid.Empty ? Guid.NewGuid() : snippet.Id;
            var existing = db.Scalar("SELECT sort_order FROM snippets WHERE id=?", r => (int)r.Number(0), id);
            var order = existing == 0 && !ExistsSnippet(db, id)
                ? (int)db.Scalar("SELECT COALESCE(MIN(sort_order),1)-1 FROM snippets", r => r.Number(0))
                : existing;
            saved = snippet with { Id = id, SortOrder = order, IsDeleted = false };
            db.Execute("INSERT INTO snippets(id,emoji,title,category_id,content,sort_order,deleted_at) VALUES(?,?,?,?,?,?,NULL) " +
                       "ON CONFLICT(id) DO UPDATE SET emoji=excluded.emoji,title=excluded.title,category_id=excluded.category_id,content=excluded.content,deleted_at=NULL",
                       id, saved.Emoji, saved.Title, saved.CategoryId, saved.Content, order);
            db.Execute("DELETE FROM drafts WHERE snippet_id=?", id);
        });
        return saved;
    }

    public void DeleteSnippets(IReadOnlyList<Guid> ids)
    {
        using var db = Open();
        db.Transaction(() =>
        {
            foreach (var id in ids)
            {
                RequireSnippet(db, id);
                db.Execute("UPDATE snippets SET deleted_at=? WHERE id=? AND deleted_at IS NULL", DateTimeOffset.UtcNow, id);
            }
        });
    }

    public void RestoreSnippets(IReadOnlyList<Guid> ids)
    {
        using var db = Open();
        db.Transaction(() =>
        {
            foreach (var id in ids)
            {
                RequireSnippet(db, id);
                db.Execute("UPDATE snippets SET deleted_at=NULL WHERE id=?", id);
            }
        });
    }

    public void PermanentlyDeleteSnippets(IReadOnlyList<Guid> ids)
    {
        using var db = Open();
        db.Transaction(() =>
        {
            foreach (var id in ids)
            {
                db.Execute("DELETE FROM drafts WHERE snippet_id=?", id);
                db.Execute("DELETE FROM snippets WHERE id=? AND deleted_at IS NOT NULL", id);
            }
        });
    }

    public void MoveSnippets(IReadOnlyList<Guid> ids, Guid? categoryId)
    {
        using var db = Open();
        db.Transaction(() =>
        {
            if (categoryId is Guid id) RequireCategory(db, id);
            foreach (var snippetId in ids)
            {
                RequireSnippet(db, snippetId);
                db.Execute("UPDATE snippets SET category_id=? WHERE id=?", categoryId, snippetId);
            }
        });
    }

    public void ReorderSnippets(Guid? categoryId, IReadOnlyList<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var db = Open();
        db.Transaction(() =>
        {
            var all = db.Query("SELECT id,category_id FROM snippets WHERE deleted_at IS NULL ORDER BY sort_order,id",
                r => (Id: r.Guid(0), CategoryId: r.NullableGuid(1)));
            RequireExactIds(ids, all.Where(item => item.CategoryId == categoryId).Select(item => item.Id).ToList());

            // Retain each category's positions in the global list. Replacing just
            // the selected category's slots preserves every other category's order.
            var next = 0;
            for (var i = 0; i < all.Count; i++)
            {
                var id = all[i].CategoryId == categoryId ? ids[next++] : all[i].Id;
                db.Execute("UPDATE snippets SET sort_order=? WHERE id=?", i, id);
            }
        });
    }

    public SnippetDraft? GetDraft(Guid snippetId)
    {
        using var db = Open();
        return db.Scalar("SELECT snippet_id,emoji,title,category_id,content,updated_at FROM drafts WHERE snippet_id=?", ReadDraft, snippetId);
    }

    public IReadOnlyList<SnippetDraft> ListDrafts()
    {
        using var db = Open();
        return db.Query("SELECT snippet_id,emoji,title,category_id,content,updated_at FROM drafts ORDER BY updated_at DESC", ReadDraft);
    }

    public void SaveDraft(SnippetDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.SnippetId == Guid.Empty) throw new ArgumentException("草稿需要固定識別碼", nameof(draft));
        using var db = Open();
        if (draft.CategoryId is Guid categoryId) RequireCategory(db, categoryId);
        db.Execute("INSERT INTO drafts(snippet_id,emoji,title,category_id,content,updated_at) VALUES(?,?,?,?,?,?) " +
                   "ON CONFLICT(snippet_id) DO UPDATE SET emoji=excluded.emoji,title=excluded.title,category_id=excluded.category_id,content=excluded.content,updated_at=excluded.updated_at",
                   draft.SnippetId, draft.Emoji, draft.Title, draft.CategoryId, draft.Content, DateTimeOffset.UtcNow);
    }

    public void DeleteDraft(Guid snippetId)
    {
        using var db = Open();
        db.Execute("DELETE FROM drafts WHERE snippet_id=?", snippetId);
    }

    public IReadOnlyList<HistoryEntry> ListHistory()
    {
        using var db = Open();
        return db.Query("SELECT id,content,copied_at FROM history ORDER BY sort_order", ReadHistory);
    }

    public HistoryEntry? AddHistory(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        using var db = Open();
        HistoryEntry? entry = null;
        db.Transaction(() =>
        {
            if (db.Scalar("SELECT value FROM settings WHERE key='history_paused'", r => r.Text(0)) == "true") return;
            var id = Guid.NewGuid();
            var timestamp = DateTimeOffset.UtcNow;
            db.Execute("DELETE FROM history WHERE content=?", content);
            // Compact existing positions before applying the limit. Deleting a duplicate
            // can leave gaps; trimming by sort_order alone would then discard entries
            // even while fewer than 50 remain.
            var remaining = db.Query("SELECT id FROM history ORDER BY sort_order,id", r => r.Guid(0));
            for (var i = 0; i < remaining.Count; i++)
            {
                if (i < 49) db.Execute("UPDATE history SET sort_order=? WHERE id=?", i + 1, remaining[i]);
                else db.Execute("DELETE FROM history WHERE id=?", remaining[i]);
            }
            db.Execute("INSERT INTO history(id,content,copied_at,sort_order) VALUES(?,?,?,0)", id, content, timestamp);
            entry = new HistoryEntry { Id = id, Content = content, CopiedAtUtc = timestamp };
        });
        return entry;
    }

    public void ClearHistory()
    {
        using var db = Open();
        db.Execute("DELETE FROM history");
    }

    public bool GetHistoryPaused() => GetSetting("history_paused") == "true";
    public void SetHistoryPaused(bool paused) => SetSetting("history_paused", paused ? "true" : "false");

    public string? GetSetting(string key)
    {
        using var db = Open();
        return db.Scalar("SELECT value FROM settings WHERE key=?", r => r.Text(0), key);
    }

    public void SetSetting(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        using var db = Open();
        db.Execute("INSERT INTO settings(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", key, value);
    }

    private SqliteDb Open() => new(DatabasePath);
    private static Category ReadCategory(SqliteDb.RowReader r) => new() { Id = r.Guid(0), Name = r.Text(1)!, SortOrder = (int)r.Number(2) };
    private static Snippet ReadSnippet(SqliteDb.RowReader r) => new() { Id = r.Guid(0), Emoji = r.Text(1)!, Title = r.Text(2)!, CategoryId = r.NullableGuid(3), Content = r.Text(4)!, SortOrder = (int)r.Number(5), IsDeleted = !r.IsNull(6) };
    private static SnippetDraft ReadDraft(SqliteDb.RowReader r) => new() { SnippetId = r.Guid(0), Emoji = r.Text(1)!, Title = r.Text(2)!, CategoryId = r.NullableGuid(3), Content = r.Text(4)!, UpdatedAtUtc = DateTimeOffset.Parse(r.Text(5)!) };
    private static HistoryEntry ReadHistory(SqliteDb.RowReader r) => new() { Id = r.Guid(0), Content = r.Text(1)!, CopiedAtUtc = DateTimeOffset.Parse(r.Text(2)!) };
    private static int NextOrder(SqliteDb db, string table) => (int)(db.Scalar($"SELECT COALESCE(MAX(sort_order),-1)+1 FROM {table}", r => r.Number(0)));
    private static bool ExistsSnippet(SqliteDb db, Guid id) => db.Scalar("SELECT 1 FROM snippets WHERE id=?", r => r.Number(0), id) == 1;
    private static void RequireCategory(SqliteDb db, Guid id)
    {
        if (db.Scalar("SELECT 1 FROM categories WHERE id=?", r => r.Number(0), id) != 1) throw new KeyNotFoundException("分類不存在");
    }
    private static void RequireSnippet(SqliteDb db, Guid id)
    {
        if (!ExistsSnippet(db, id)) throw new KeyNotFoundException("文字項目不存在");
    }
    private static string ValidateCategoryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("分類名稱不可為空", nameof(name));
        return name.Trim();
    }
    private static void RequireExactIds(IReadOnlyList<Guid> proposed, IReadOnlyList<Guid> actual)
    {
        if (proposed.Count != actual.Count || proposed.Distinct().Count() != actual.Count || !proposed.ToHashSet().SetEquals(actual))
            throw new ArgumentException("排序清單必須包含該範圍所有項目且不能重複");
    }
    private static void ValidateDatabase(SqliteDb db)
    {
        if (db.Scalar("PRAGMA integrity_check", r => r.Text(0)) != "ok") throw new InvalidDataException("資料庫完整性檢查失敗");
        if (db.Scalar("PRAGMA foreign_key_check", r => r.Text(0)) is not null) throw new InvalidDataException("資料庫關聯檢查失敗");
    }
}
