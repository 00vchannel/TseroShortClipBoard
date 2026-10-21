using System.Security.Cryptography;
using System.Text.Json;

namespace ClipboardCore;

public sealed record PanelResumeState(string Search, Guid? CategoryId, bool History, Guid? SelectedId);

// Shared by the app and the small handoff process. Contains local state only; never upload this file.
public sealed record UpdateJournal(string Id, string DataDirectory, string InstallRoot, string ContentDirectory,
    string EntryPath, string UpdaterPath, string PackagePath, string PackageSha256, long PackageSize,
    string TargetVersion, int ParentPid, long ParentStartTicks, Dictionary<string, TableFingerprint> Fingerprint,
    PanelResumeState Panel)
{
    public string JobDirectory => Path.Combine(DataDirectory, "updates", Id);
    public string RecoveryDirectory => Path.Combine(JobDirectory, "previous");
    public string BackupPath => Path.Combine(JobDirectory, "before-update.sqlite");
    public string JournalPath => Path.Combine(JobDirectory, "job.json");
    public string ActivePath => Path.Combine(DataDirectory, "updates", "active.json");
    public static string LockPath(string dataDirectory) => Path.Combine(dataDirectory, "updates", "update.lock");

    public static void WriteAtomic<T>(string path, T value)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value);
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static UpdateJournal Read(string path)
    {
        var journal = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(path))
            ?? throw new InvalidDataException("更新交接資訊不完整。");
        if (!Guid.TryParseExact(journal.Id, "N", out _) ||
            !Path.GetFullPath(path).Equals(Path.GetFullPath(journal.JournalPath), StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathFullyQualified(journal.InstallRoot) ||
            !IsWithin(journal.ContentDirectory, journal.InstallRoot) ||
            !IsWithin(journal.EntryPath, journal.InstallRoot) || !IsWithin(journal.UpdaterPath, journal.InstallRoot) ||
            !IsWithin(journal.PackagePath, Path.Combine(journal.InstallRoot, "packages")))
            throw new InvalidDataException("更新路徑不正確，未替換程式。");
        return journal;
    }

    public static bool IsWithin(string path, string directory) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public void VerifyPackage()
    {
        using var stream = File.OpenRead(PackagePath);
        if (stream.Length != PackageSize ||
            !Convert.ToHexString(SHA256.HashData(stream)).Equals(PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("下載檔案不完整，請重新下載。");
    }

    public void ClearActive()
    {
        if (File.Exists(ActivePath) && JsonSerializer.Deserialize<string>(File.ReadAllText(ActivePath)) == Id)
            File.Delete(ActivePath);
    }
}
