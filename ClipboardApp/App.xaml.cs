using System.Windows;
using System.IO;
using ClipboardApp.Services;
using ClipboardCore;
using System.Windows.Input;
using Velopack;

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
    private AppUpdateService? _updates;
    private UpdateJournal? _updateJournal;
    private bool _updateFrozen;
    private bool _shuttingDown;

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
            var dataDirectory = Program.DataDirectory;
            if (File.Exists(Path.Combine(dataDirectory, "updates", "active.json")) &&
                !File.Exists(Path.Combine(dataDirectory, "clipboard.db")))
                throw new InvalidDataException("更新後找不到原有資料庫，已停止啟動，請保留 updates 資料夾中的備份並先復原資料。");
            var repository = PrepareRepository(dataDirectory);
            var resume = UpdatePreparation.VerifyPending(repository, dataDirectory);
            var controller = new ClipboardController(repository, dataDirectory);
#if !UPDATE_TEST_BUILD
            controller.MigrateAutostartEntry();
#endif
            controller.BackupFailed += (_, error) => Dispatcher.BeginInvoke(() =>
                UiPrompt.Notify(_manager, "備份提醒", $"自動備份未完成：{error}\n舊備份仍保留。"));
            controller.HistoryFailed += (_, error) => Dispatcher.BeginInvoke(() =>
                UiPrompt.Notify(_manager, "複製歷史提醒", $"這次的文字未能記錄：{error}\n請檢查資料位置或磁碟空間。既有資料仍保留。"));
            controller.SettingsChanged += (_, _) => RefreshHotkey(controller);

            _manager = new MainWindow(controller);
            _updates = new AppUpdateService(Dispatcher, (manager, update, cancellation) =>
                PrepareUpdateAsync(repository, dataDirectory, manager, update, cancellation));
            _panel = new QuickPanel(controller, _updates);
            _manager.QuickPanelRequested += (_, _) => ShowPanel();
            _panel.ManagerRequested += (_, _) => ShowManager();
            MainWindow = _manager;
#if !UPDATE_TEST_BUILD
            _tray = new TrayIcon(ShowManager, ShowPanel, ShutdownSafely);
            _clipboardWatcher = new ClipboardWatcher();
            _clipboardWatcher.Changed += (_, _) => { if (!_updateFrozen) controller.OnClipboardChanged(); };
            RefreshHotkey(controller);
#endif
            if (resume.Panel is { } panelState) _panel.ShowAfterUpdate(panelState);
            else _manager.Show();
            if (resume.Notice is { } notice) UiPrompt.Notify(_panel.IsVisible ? _panel : _manager.IsVisible ? _manager : null, "更新提醒", notice);
            _updates.Start();
#if UPDATE_TEST_BUILD
            UpdateTestHarness.Drive(this, _updates, _manager, _panel, repository, resume.Panel is not null);
#endif
        }
        catch (OperationCanceledException)
        {
            Shutdown();
        }
        catch (Exception ex)
        {
#if UPDATE_TEST_BUILD
            UpdateTestHarness.RecordStartupFailure(ex);
            Shutdown(1);
#else
            UiPrompt.Notify(null, "零零快捷剪貼板 2.0", $"新版無法啟動：{ex.Message}\n舊版資料未變更。");
            Shutdown();
#endif
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
        if (_updateFrozen) return;
        if (_manager?.TryPrepareToClose() == false) return;
        _shuttingDown = true;
        _updates?.Dispose();
        _manager?.AllowClose();
        _panel?.AllowClose();
        Shutdown();
    }

    private async Task PrepareUpdateAsync(ClipboardRepository repository, string dataDirectory,
        UpdateManager manager, UpdateInfo update, CancellationToken cancellation)
    {
        while (_manager!.IsBusyForUpdate || _panel!.IsBusyForUpdate || Mouse.Captured is not null ||
               Windows.OfType<Window>().Any(w => w.IsVisible && w != _manager && w != _panel))
            await Task.Delay(150, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (_shuttingDown) throw new OperationCanceledException();
        var panelState = _panel.CaptureResumeState();
        _updateFrozen = true;
        var frozenWindows = Windows.OfType<Window>().Where(w => w.IsEnabled).ToArray();
        foreach (var window in frozenWindows) window.IsEnabled = false;
        try
        {
            if (!_manager.TryPrepareToClose())
                throw new InvalidOperationException("草稿未能保存，已取消重啟。請在管理視窗保存後再重試。");
            _updateJournal = await Task.Run(() => UpdatePreparation.Create(repository, dataDirectory, manager,
                update, panelState, cancellation), cancellation);
            await UpdatePreparation.StartHandoffAsync(_updateJournal, cancellation);
            cancellation.ThrowIfCancellationRequested();
            _manager.AllowClose();
            _panel.AllowClose();
            _shuttingDown = true;
            Shutdown();
        }
        catch
        {
            if (_updateJournal is { } journal)
            {
                // Failure cleanup must not leave the UI frozen when a full disk or denied write
                // is the very reason the update failed. Without exit-ready the helper cannot apply.
                try { UpdateJournal.WriteAtomic(Path.Combine(journal.JobDirectory, "abort"), true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                try { journal.ClearActive(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                _updateJournal = null;
            }
            _updateFrozen = false;
            foreach (var window in frozenWindows) window.IsEnabled = true;
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updates?.Dispose();
        _keyboard?.Dispose();
        _clipboardWatcher?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
        if (_updateJournal is { } journal && e.ApplicationExitCode == 0)
            UpdateJournal.WriteAtomic(Path.Combine(journal.JobDirectory, "exit-ready"), true);
    }
}
