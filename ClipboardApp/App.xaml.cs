using System.Windows;
using System.IO;
using ClipboardApp.Services;
using ClipboardCore;

namespace ClipboardApp;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstance;
    private GlobalHotkeyHook? _keyboard;
    private ClipboardWatcher? _clipboardWatcher;
    private TrayIcon? _tray;
    private MainWindow? _manager;
    private QuickPanel? _panel;
    private string? _activeHotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new SingleInstanceGuard(ShowManager);
        if (!_singleInstance.IsPrimary) { Shutdown(); return; }

        // The two versions use distinct mutexes and data directories. Never close the legacy process.
        if (SingleInstanceGuard.IsLegacyRunning())
        {
            UiPrompt.Notify(null, "零零快捷剪貼板 2.0", "舊版剪貼板工具仍在執行。請先從舊版系統匣正常結束，再開啟新版。");
            Shutdown();
            return;
        }

        try
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "零零快捷剪貼板 2.0");
            var repository = PrepareRepository(dataDirectory);
            var controller = new ClipboardController(repository, dataDirectory);
            controller.BackupFailed += (_, error) => Dispatcher.BeginInvoke(() =>
                UiPrompt.Notify(_manager, "備份提醒", $"自動備份未完成：{error}\n舊備份仍保留。"));
            controller.HistoryFailed += (_, error) => Dispatcher.BeginInvoke(() =>
                UiPrompt.Notify(_manager, "複製歷史提醒", $"這次的文字未能記錄：{error}\n請檢查資料位置或磁碟空間。既有資料仍保留。"));
            controller.SettingsChanged += (_, _) => RefreshHotkey(controller);

            _manager = new MainWindow(controller);
            _panel = new QuickPanel(controller);
            _manager.QuickPanelRequested += (_, _) => ShowPanel();
            _panel.ManagerRequested += (_, _) => ShowManager();
            MainWindow = _manager;
            _tray = new TrayIcon(ShowManager, ShowPanel, ShutdownSafely);
            _clipboardWatcher = new ClipboardWatcher();
            _clipboardWatcher.Changed += (_, _) => controller.OnClipboardChanged();
            RefreshHotkey(controller);
            _manager.Show();
        }
        catch (OperationCanceledException)
        {
            Shutdown();
        }
        catch (Exception ex)
        {
            UiPrompt.Notify(null, "零零快捷剪貼板 2.0", $"新版無法啟動：{ex.Message}\n舊版資料未變更。");
            Shutdown();
        }
    }

    private static ClipboardRepository PrepareRepository(string dataDirectory, string? legacyDirectory = null)
    {
        legacyDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "零零快捷剪貼板");
        var marker = Path.Combine(dataDirectory, "import-complete");
        var database = Path.Combine(dataDirectory, "clipboard.db");
        if (File.Exists(database))
        {
            var existing = new ClipboardRepository(dataDirectory);
            existing.ValidateExisting();
            if (!existing.IsSetupComplete() && !File.Exists(marker))
                throw new InvalidDataException("偵測到尚未完成設定的資料庫。原資料已保留，請先檢查或從備份復原。");
            return existing;
        }
        if (File.Exists(marker))
            throw new InvalidDataException("找不到原有資料庫。為避免遺失資料，程式不會建立空白庫；請先從備份復原。");

        var legacyData = Path.Combine(legacyDirectory, "clipboard_data.json");
        if (!File.Exists(legacyData))
        {
            var choice = UiPrompt.ChooseStartup();
            if (choice is null) throw new OperationCanceledException("已取消首次設定。");
            if (choice == "new")
            {
                var fresh = new ClipboardRepository(dataDirectory);
                fresh.Initialize();
                fresh.CompleteSetup();
                return fresh;
            }
        }

        var importDirectory = Path.Combine(dataDirectory, "original-import");
        (string Data, string? Settings)? source;
        try { source = LegacyImportSource.Acquire(importDirectory, legacyDirectory: legacyDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!UiPrompt.Confirm(null, "匯入來源", $"無法讀取舊版正式資料：{ex.Message}\n要手動選擇已備份的 clipboard_data.json 嗎？", "選擇備份")) throw;
            source = LegacyImportSource.Acquire(importDirectory, forceSelect: true, legacyDirectory: legacyDirectory);
        }
        if (source is null) throw new OperationCanceledException("尚未選擇舊版資料，首次匯入已取消。");
        var repository = new ClipboardRepository(dataDirectory);
        try
        {
            repository.Initialize();
            try
            {
                repository.ImportLegacy(source.Value.Data, source.Value.Settings);
            }
            catch (Exception firstError)
            {
                if (!UiPrompt.Confirm(null, "匯入失敗", $"舊版資料無法解析：{firstError.Message}\n要手動選擇另一份 clipboard_data.json 嗎？", "選擇檔案")) throw;
                source = LegacyImportSource.Acquire(Path.Combine(dataDirectory, "original-import"), forceSelect: true, legacyDirectory: legacyDirectory);
                if (source is null) throw new OperationCanceledException("手動選檔已取消。");
                repository.ImportLegacy(source.Value.Data, source.Value.Settings);
            }
            // The imported flag is committed together with the data. A separate file is unnecessary.
            return repository;
        }
        catch
        {
            // Only remove the empty database from this failed first setup, never a committed import.
            if (repository.IsSetupComplete()) throw;
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var file = database + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
            throw;
        }
    }

    private void ShowManager()
    {
        if (_manager is null) return;
        _manager.Show();
        if (_manager.WindowState == WindowState.Minimized) _manager.WindowState = WindowState.Normal;
        _manager.Activate();
    }

    private void ShowPanel() => _panel?.ShowPanel();

    private void RefreshHotkey(ClipboardController controller)
    {
        try
        {
            var hotkey = controller.GetSettings().Hotkey;
            if (_keyboard is not null && _activeHotkey == hotkey) return;
            var next = new GlobalHotkeyHook(hotkey,
                () => _panel?.TogglePanel(), () => _panel?.Hide());
            var previous = _keyboard;
            _keyboard = next;
            _activeHotkey = hotkey;
            previous?.Dispose();
        }
        catch (Exception ex)
        {
            UiPrompt.Notify(_manager, "快捷鍵設定", $"快捷鍵無法啟用：{ex.Message}");
        }
    }

    private void ShutdownSafely()
    {
        if (_manager?.TryPrepareToClose() == false) return;
        _manager?.AllowClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _keyboard?.Dispose();
        _clipboardWatcher?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
