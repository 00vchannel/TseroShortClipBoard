using System.IO;
using ClipboardCore;
using Velopack;

namespace ClipboardApp;

internal static class Program
{
    internal static string DataDirectory =>
#if UPDATE_TEST_BUILD
        UpdateTestHarness.DataDirectory ??
#endif
        Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "零零快捷剪貼板 2.0");

    [STAThread]
    public static void Main(string[] args)
    {
#if UPDATE_TEST_BUILD
        if (args.Any(argument => argument.StartsWith("--velo", StringComparison.Ordinal)))
        { VelopackApp.Build().SetAutoApplyOnStartup(false).Run(); return; }
        UpdateTestHarness.Configure();
#endif
        // Do not allow SDK startup cleanup or a second app to race with replacement.
        var lockPath = UpdateJournal.LockPath(DataDirectory);
        if (File.Exists(lockPath))
        {
            try { using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return; }
        }
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
