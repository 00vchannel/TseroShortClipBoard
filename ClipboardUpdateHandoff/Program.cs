using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ClipboardCore;

if (args.Length != 1) return 2;
UpdateJournal? journal = null;
FileStream? gate = null;
try
{
    journal = UpdateJournal.Read(args[0]);
    gate = new FileStream(UpdateJournal.LockPath(journal.DataDirectory), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    using var parent = Process.GetProcessById(journal.ParentPid);
    if (parent.StartTime.ToUniversalTime().Ticks != journal.ParentStartTicks) throw new IOException("啟動來源已變更。");
    // Keep a process handle opened while the parent is alive. ExitCode on a late GetProcessById object
    // is not available on every .NET/Windows combination.
    var parentHandle = parent.SafeHandle;
    UpdateJournal.WriteAtomic(Path.Combine(journal.JobDirectory, "ready"), true);
    // Never terminate the app. An explicit permit plus exit code 0 proves its shutdown path completed.
    var deadline = DateTime.UtcNow.AddSeconds(90);
    while (!parent.HasExited)
    {
        if (File.Exists(Path.Combine(journal.JobDirectory, "abort"))) return 1;
        if (DateTime.UtcNow >= deadline) throw new TimeoutException();
        await Task.Delay(100);
    }
    if (File.Exists(Path.Combine(journal.JobDirectory, "abort"))) return 1;
    if (!Native.GetExitCodeProcess(parentHandle, out var exitCode) || exitCode != 0 ||
        !File.Exists(Path.Combine(journal.JobDirectory, "exit-ready")))
        throw new IOException("程式未正常結束，未替換檔案。");
    journal.VerifyPackage();
    ClipboardRepository.RequireMatchingFingerprints(journal.Fingerprint,
        ClipboardRepository.CaptureUpdateFingerprint(Path.Combine(journal.DataDirectory, "clipboard.db")));
    var start = new ProcessStartInfo(journal.UpdaterPath)
    { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = journal.JobDirectory };
    foreach (var argument in new[] { "--silent", "--log", Path.Combine(journal.JobDirectory, "installer.log"),
                 "apply", "--norestart", "--package", journal.PackagePath })
        start.ArgumentList.Add(argument);
    using var updater = Process.Start(start) ?? throw new IOException("更新工具無法啟動。");
    // The updater owns replacement. Do not time out and relaunch an app while it is still replacing files.
    await updater.WaitForExitAsync();
    if (updater.ExitCode != 0) throw new IOException("更新工具未完成替換。");
    UpdateJournal.WriteAtomic(Path.Combine(journal.JobDirectory, "result.json"), "applied");
    gate.Dispose(); gate = null;
    StartApp(journal);
    return 0;
}
catch (Exception error)
{
    if (journal is not null)
    {
        try
        {
            File.WriteAllText(Path.Combine(journal.JobDirectory, "handoff-error.txt"), error.ToString());
            UpdateJournal.WriteAtomic(Path.Combine(journal.JobDirectory, "result.json"), "failed");
            // If the parent is still alive, leave it in control. It will clear the active marker on failure.
            var parentAlive = false;
            try
            {
                using var parent = Process.GetProcessById(journal.ParentPid);
                parentAlive = parent.StartTime.ToUniversalTime().Ticks == journal.ParentStartTicks && !parent.HasExited;
            }
            catch (ArgumentException) { }
            gate?.Dispose(); gate = null;
            if (!parentAlive) StartApp(journal);
        }
        catch { /* The durable job directory contains the previous app, database backup and recovery instructions. */ }
    }
    return 1;
}
finally { gate?.Dispose(); }

static void StartApp(UpdateJournal journal)
{
    var entry = File.Exists(journal.EntryPath) ? journal.EntryPath :
        Path.Combine(journal.RecoveryDirectory, Path.GetFileName(journal.EntryPath));
    Process.Start(new ProcessStartInfo(entry) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(entry)! });
}

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
}
