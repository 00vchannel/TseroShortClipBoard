using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace ClipboardApp.Services;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Local\\ZeroZeroClipboardV2_SingleInstance";
    private const string SignalName = "Local\\ZeroZeroClipboardV2_ShowWindow";
    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _signal;
    private readonly CancellationTokenSource _cancellation = new();

    public bool IsPrimary { get; }

    public SingleInstanceGuard(Action showExisting)
    {
        _mutex = new Mutex(true, MutexName, out var created);
        IsPrimary = created;
        if (!created)
        {
            try { using var signal = EventWaitHandle.OpenExisting(SignalName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return;
        }

        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        _ = Task.Run(() =>
        {
            while (!_cancellation.IsCancellationRequested)
            {
                if (_signal.WaitOne(500))
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(showExisting);
            }
        });
    }

    public static bool IsLegacyRunning()
    {
        try
        {
            using var previous = Mutex.OpenExisting("Global\\ZeroZeroClipboard_SingleInstance");
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _signal?.Set();
        _signal?.Dispose();
        if (IsPrimary) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _cancellation.Dispose();
    }
}

internal sealed class RightAltHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int VkEscape = 0x1B;
    private readonly HookProc _callback;
    private readonly Action _toggle;
    private readonly Action _escape;
    private readonly int _activationKey;
    private nint _hook;
    private bool _activationDown;

    public RightAltHook(string hotkey, Action toggle, Action escape)
    {
        _activationKey = ResolveKey(hotkey);
        _toggle = toggle;
        _escape = escape;
        _callback = OnKeyboard;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
        if (_hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var key = Marshal.ReadInt32(data);
            var down = message == WmKeyDown || message == 0x0104;
            var up = message == 0x0101 || message == 0x0105;
            if (key == _activationKey)
            {
                if (down && !_activationDown)
                {
                    _activationDown = true;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(_toggle);
                }
                else if (up) _activationDown = false;
            }
            else if (key == VkEscape && down)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(_escape);
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (_hook != 0) UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    public static bool IsSupportedHotkey(string value)
    {
        try { ResolveKey(value); return true; }
        catch (ArgumentException) { return false; }
    }

    private static int ResolveKey(string value) => value.ToLowerInvariant() switch
    {
        "right alt" => 0xA5,
        "right ctrl" => 0xA3,
        "right shift" => 0xA1,
        "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
        "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
        "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
        "scroll lock" => 0x91, "pause" => 0x13, "insert" => 0x2D, "home" => 0x24,
        _ => throw new ArgumentException("不支援的快捷鍵", nameof(value))
    };

    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
}

internal sealed class ClipboardWatcher : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly HwndSource _window;
    public event EventHandler? Changed;

    public ClipboardWatcher()
    {
        _window = new HwndSource(new HwndSourceParameters("ZeroZeroClipboardV2Listener")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000)
        });
        _window.AddHook(OnMessage);
        if (!AddClipboardFormatListener(_window.Handle))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmClipboardUpdate) Changed?.Invoke(this, EventArgs.Empty);
        return 0;
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(_window.Handle);
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(nint hwnd);
}

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon? _ownedIcon;

    public TrayIcon(Action showWindow, Action showPanel, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("開啟管理視窗", null, (_, _) => showWindow());
        menu.Items.Add("開啟快捷面板", null, (_, _) => showPanel());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("結束", null, (_, _) => exit());
        try
        {
            if (Environment.ProcessPath is { } path)
                _ownedIcon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        }
        catch (Exception) when (_ownedIcon is null) { /* Keep a usable tray icon. */ }
        _icon = new Forms.NotifyIcon
        {
            Text = "零零快捷剪貼板 2.0",
            Icon = _ownedIcon ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => showWindow();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _ownedIcon?.Dispose();
    }
}
