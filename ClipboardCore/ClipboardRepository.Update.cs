using System.Security.Cryptography;
using System.Text.Json;

namespace ClipboardCore;

public sealed record TableFingerprint(long Count, string Sha256);

public sealed partial class ClipboardRepository
{
    public Dictionary<string, TableFingerprint> CaptureUpdateFingerprint()
        => CaptureUpdateFingerprint(DatabasePath);

    public static Dictionary<string, TableFingerprint> CaptureUpdateFingerprint(string path)
    {
        using var db = new SqliteDb(path, readOnly: true);
        db.Execute("BEGIN");
        try
        {
            ValidateDatabase(db);
            if (db.Scalar("SELECT value FROM meta WHERE key='schema_version'", r => r.Text(0)) != "1")
                throw new InvalidDataException("這個資料版本不支援自動更新，原資料已保留。");
            var result = new Dictionary<string, TableFingerprint>(StringComparer.Ordinal);
            foreach (var (table, key) in new[] { ("meta", "key"), ("categories", "id"), ("snippets", "id"),
                         ("drafts", "snippet_id"), ("history", "id"), ("settings", "key") })
            {
                var columns = db.Query($"PRAGMA table_info({table})", r => r.Text(1)!);
                // JSON arrays distinguish NULL, empty strings and embedded newlines. Stable primary-key order
                // compares logical data, independent of WAL layout, vacuuming and SQLite page allocation.
                var rows = db.Query($"SELECT * FROM {table} ORDER BY {key} COLLATE BINARY",
                    r => Enumerable.Range(0, columns.Count).Select(r.Text).ToArray());
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(columns));
                foreach (var row in rows) hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(row));
                result[table] = new(rows.Count, Convert.ToHexString(hash.GetHashAndReset()));
            }
            return result;
        }
        finally { db.Execute("ROLLBACK"); }
    }

    public static void RequireMatchingFingerprints(IReadOnlyDictionary<string, TableFingerprint> expected,
        IReadOnlyDictionary<string, TableFingerprint> actual)
    {
        if (expected.Count != 6 || actual.Count != expected.Count ||
            expected.Any(pair => !actual.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new InvalidDataException("更新前後的資料核對不一致。已保留目前資料及更新前備份，請先復原檢查。");
    }
}
