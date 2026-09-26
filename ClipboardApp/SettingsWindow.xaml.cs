using System.Windows;
using System.Windows.Controls;
using ClipboardApp.Services;
using ClipboardApp.Styles;
using Forms = System.Windows.Forms;

namespace ClipboardApp;

public partial class SettingsWindow : Window
{
    private sealed record Choice(string Value, string Name);

    private readonly IClipboardController controller;
    private bool loading;

    public SettingsWindow(IClipboardController controller)
    {
        this.controller = controller;
        InitializeComponent();
        loading = true;
        var hotkeys = new List<Choice>
        {
            new("right alt", "右 Alt"),
            new("right ctrl", "右 Ctrl")
        };
        hotkeys.AddRange(Enumerable.Range(1, 12).Select(i => new Choice($"f{i}", $"F{i}")));
        HotkeyCombo.ItemsSource = hotkeys;
        HotkeyCombo.DisplayMemberPath = nameof(Choice.Name);
        ThemeCombo.ItemsSource = new List<Choice> { new("dark", "深色"), new("light", "淺色") };
        ThemeCombo.DisplayMemberPath = nameof(Choice.Name);
        var settings = controller.GetSettings();
        HotkeyCombo.SelectedItem = hotkeys.FirstOrDefault(c => c.Value.Equals(settings.Hotkey, StringComparison.OrdinalIgnoreCase)) ?? hotkeys[0];
        ThemeCombo.SelectedItem = ((IEnumerable<Choice>)ThemeCombo.ItemsSource).FirstOrDefault(c => c.Value.Equals(settings.Theme, StringComparison.OrdinalIgnoreCase));
        AutostartCheck.IsChecked = settings.Autostart;
        FontScaleSlider.Value = settings.FontScale;
        FontScaleLabel.Text = $"{settings.FontScale:P0}";
        BackupPathText.Text = settings.BackupDirectory;
        loading = false;
        Loaded += (_, _) => UiPreferences.Apply(this, controller.GetSettings());
        controller.SettingsChanged += Controller_SettingsChanged;
        Closed += (_, _) => controller.SettingsChanged -= Controller_SettingsChanged;
    }

    private void Controller_SettingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) UiPreferences.Apply(this, controller.GetSettings());
        else Dispatcher.BeginInvoke(() => UiPreferences.Apply(this, controller.GetSettings()));
    }

    private void SetSetting(Action action, string success)
    {
        if (loading) return;
        try { action(); StatusText.Text = success; }
        catch (Exception ex)
        {
            StatusText.Text = "設定失敗：" + ex.Message;
            UiPrompt.Notify(this, "設定失敗", ex.Message);
        }
    }

    private void HotkeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotkeyCombo.SelectedItem is Choice choice) SetSetting(() => controller.SetHotkey(choice.Value), "快捷鍵已更新");
    }

    private void AutostartCheck_Changed(object sender, RoutedEventArgs e)
        => SetSetting(() => controller.SetAutostart(AutostartCheck.IsChecked == true), "開機啟動設定已更新");

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is Choice choice) SetSetting(() => controller.SetTheme(choice.Value), "主題已更新");
    }

    private void FontScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FontScaleLabel is null) return;
        FontScaleLabel.Text = $"{FontScaleSlider.Value:P0}";
        if (!loading) SetSetting(() => controller.SetFontScale(FontScaleSlider.Value), "文字大小已更新");
    }

    private void ChooseBackupDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var picker = new Forms.FolderBrowserDialog
        {
            Description = "選擇本機備份資料夾",
            UseDescriptionForTitle = true,
            SelectedPath = BackupPathText.Text
        };
        if (picker.ShowDialog() != Forms.DialogResult.OK) return;
        SetSetting(() => controller.SetBackupDirectory(picker.SelectedPath), "備份位置已更新");
        BackupPathText.Text = controller.GetSettings().BackupDirectory;
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
