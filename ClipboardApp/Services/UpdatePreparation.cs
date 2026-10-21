using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ClipboardCore;
using Velopack;

namespace ClipboardApp.Services;

internal static class UpdatePreparation
{
    public static UpdateJournal Create(ClipboardRepository repository, string dataDirectory, UpdateManager manager,
        UpdateInfo update, PanelResumeState panel, CancellationToken cancellation)
    {
        var locator = Velopack.Locators.VelopackLocator.Current;
        var root = locator.RootAppDir ?? throw new InvalidOperationException("請使用完整的免安裝資料夾進行更新。");
        var content = locator.AppContentDir ?? throw new InvalidOperationException("找不到程式資料夾。");
        var entry = StableEntryPath();
        var release = update.TargetFullRelease;
        if (Path.GetFileName(release.FileName) != release.FileName || string.IsNullOrWhiteSpace(release.SHA256))
            throw new InvalidDataException("此版本缺少安全更新資訊，請使用發布頁的完整下載版本。");
        var package = Path.Combine(locator.PackagesDir!, release.FileName);
        using var process = Process.GetCurrentProcess();
        var journal = new UpdateJournal(Guid.NewGuid().ToString("N"), dataDirectory, root, content, entry,
            locator.UpdateExePath!, package, release.SHA256, release.Size, release.Version.ToString(),
            process.Id, process.StartTime.ToUniversalTime().Ticks, repository.CaptureUpdateFingerprint(), panel);
        if (UpdateJournal.IsWithin(journal.JobDirectory, root))
            throw new InvalidOperationException("程式資料夾不能放在使用者資料目錄內，請先移到獨立資料夾。");
        Directory.CreateDirectory(journal.JobDirectory);
        journal.VerifyPackage();
        cancellation.ThrowIfCancellationRequested();
        repository.CreateBackup(journal.BackupPath);
        ClipboardRepository.RequireMatchingFingerprints(journal.Fingerprint,
            ClipboardRepository.CaptureUpdateFingerprint(journal.BackupPath));
        CopyDirectory(content, Path.Combine(journal.RecoveryDirectory, "current"), cancellation);
        // Keep the fixed launcher, updater and portable marker, but not downloaded package caches.
        foreach (var file in Directory.EnumerateFiles(root))
        {
            if (Path.GetFileName(file).Equals(".velopack_lock", StringComparison.OrdinalIgnoreCase)) continue;
            CopyVerified(file, Path.Combine(journal.RecoveryDirectory, Path.GetFileName(file)), cancellation);
        }
        var helper = Path.Combine(content, "ClipboardUpdateHandoff.exe");
        CopyVerified(helper, Path.Combine(journal.JobDirectory, "ClipboardUpdateHandoff.exe"), cancellation);
        File.WriteAllText(Path.Combine(journal.JobDirectory, "復原說明.txt"),
            "若新版無法啟動，請先確認剪貼板工具已完全結束。\r\n" +
            "上一版完整程式在 previous 資料夾；請將整個資料夾複製到新的固定位置，再開啟最外層的程式。\r\n" +
            "資料仍保存在原本的使用者資料目錄。before-update.sqlite 是更新前的一致性備份。\r\n" +
            "不要直接用備份覆蓋使用中的資料庫；需要還原時請使用程式內的還原備份功能。\r\n");
        cancellation.ThrowIfCancellationRequested();
        ClipboardRepository.RequireMatchingFingerprints(journal.Fingerprint, repository.CaptureUpdateFingerprint());
        UpdateJournal.WriteAtomic(journal.JournalPath, journal);
        UpdateJournal.WriteAtomic(journal.ActivePath, journal.Id);
        return journal;
    }

    public static async Task StartHandoffAsync(UpdateJournal journal, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(Path.Combine(journal.JobDirectory, "ClipboardUpdateHandoff.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = journal.JobDirectory };
        start.ArgumentList.Add(journal.JournalPath);
        using var helper = Process.Start(start) ?? throw new IOException("無法啟動更新交接程序。");
        var ready = Path.Combine(journal.JobDirectory, "ready");
        for (var i = 0; i < 100; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(ready)) return;
            if (helper.HasExited) throw new IOException("更新交接未能啟動，原程式仍可使用。");
            await Task.Delay(100, cancellation);
        }
        throw new IOException("等待更新交接逾時，原程式仍可使用。");
    }

    public static string StableEntryPath()
    {
        var process = Environment.ProcessPath ?? throw new InvalidOperationException("找不到程式位置。");
        var content = Path.GetDirectoryName(process)!;
        if (File.Exists(Path.Combine(content, "sq.version")))
        {
            var root = Directory.GetParent(content)!.FullName;
            var entry = Path.Combine(root, "零零快捷剪貼板.exe");
            if (!File.Exists(entry)) throw new InvalidOperationException("找不到固定啟動入口，請重新解壓完整程式資料夾。");
            return entry;
        }
        return process;
    }

    public static (PanelResumeState? Panel, string? Notice) VerifyPending(ClipboardRepository repository, string dataDirectory)
    {
        var active = Path.Combine(dataDirectory, "updates", "active.json");
        if (!File.Exists(active)) return (null, null);
        var id = JsonSerializer.Deserialize<string>(File.ReadAllText(active));
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("更新復原資訊損壞，請保留資料目錄。");
        var journal = UpdateJournal.Read(Path.Combine(dataDirectory, "updates", id, "job.json"));
        try { ClipboardRepository.RequireMatchingFingerprints(journal.Fingerprint, repository.CaptureUpdateFingerprint()); }
        catch (Exception ex)
        {
            throw new InvalidDataException($"更新後資料驗證未通過，已停止寫入。\n備份與上一版程式：{journal.JobDirectory}\n{ex.Message}", ex);
        }
        var resultPath = Path.Combine(journal.JobDirectory, "result.json");
        var result = File.Exists(resultPath) ? JsonSerializer.Deserialize<string>(File.ReadAllText(resultPath)) : "interrupted";
        var installed = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var targetMatches = Version.TryParse(journal.TargetVersion, out var target) && installed is not null &&
            installed.Major == target.Major && installed.Minor == target.Minor && installed.Build == target.Build;
        var notice = result == "applied" && targetMatches ? null :
            $"更新未完成，已保留原有資料。\n上一版程式與備份：{journal.JobDirectory}";
        journal.ClearActive();
        return (journal.Panel, notice);
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellation)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("程式資料夾含有連結，無法安全保留上一版。");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            CopyVerified(file, Path.Combine(destination, Path.GetFileName(file)), cancellation);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), cancellation);
    }

    private static void CopyVerified(string source, string destination, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("程式檔案含有連結。");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // Deny writes while copying so a concurrent replacement cannot produce a mixed recovery copy.
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { input.CopyTo(output); output.Flush(true); }
        input.Position = 0;
        using var copied = File.OpenRead(destination);
        if (!SHA256.HashData(input).SequenceEqual(SHA256.HashData(copied))) throw new IOException("上一版程式備份核對失敗。");
    }
}
