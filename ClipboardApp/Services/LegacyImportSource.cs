using System.Security.Cryptography;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ClipboardApp.Services;

internal static class LegacyImportSource
{
    public static (string Data, string? Settings)? Acquire(string destination, bool forceSelect = false, string? legacyDirectory = null)
    {
        legacyDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "零零快捷剪貼板");
        var data = Path.Combine(legacyDirectory, "clipboard_data.json");
        var settings = Path.Combine(legacyDirectory, "settings.json");

        if (forceSelect || !File.Exists(data))
        {
            UiPrompt.Notify(null, "匯入舊版文字", "請選擇舊版 clipboard_data.json 備份。取消後不會匯入或變更舊資料。");
            using var dialog = new Forms.OpenFileDialog
            {
                Title = "選擇舊版 clipboard_data.json",
                Filter = "JSON 資料|*.json",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return null;
            data = dialog.FileName;
            settings = Path.Combine(Path.GetDirectoryName(data)!, "settings.json");
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.CreateDirectory(destination);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                var dataSnapshot = Path.Combine(destination, $"legacy-data-{stamp}.json");
                var settingsSnapshot = Path.Combine(destination, $"legacy-settings-{stamp}.json");
                var before = Sha256(data);
                File.Copy(data, dataSnapshot);
                var after = Sha256(data);
                if (before != after || before != Sha256(dataSnapshot))
                {
                    File.Delete(dataSnapshot);
                    continue;
                }
                string? settingsPath = null;
                if (File.Exists(settings))
                {
                    var settingHash = Sha256(settings);
                    File.Copy(settings, settingsSnapshot);
                    if (settingHash != Sha256(settings) || settingHash != Sha256(settingsSnapshot))
                    {
                        File.Delete(dataSnapshot);
                        File.Delete(settingsSnapshot);
                        continue;
                    }
                    settingsPath = settingsSnapshot;
                }
                return (dataSnapshot, settingsPath);
            }
            catch (IOException) when (attempt < 2) { Thread.Sleep(150); }
        }
        throw new IOException("無法取得一致的舊版資料快照。請確認舊版已結束，然後重試。");
    }

    private static string Sha256(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
