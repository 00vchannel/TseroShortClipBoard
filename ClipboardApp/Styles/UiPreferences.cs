using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ClipboardApp.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Control = System.Windows.Controls.Control;

namespace ClipboardApp.Styles;

internal static class UiPreferences
{
    private sealed class BaseSize(double value) { public double Value { get; } = value; }
    private static readonly ConditionalWeakTable<FrameworkElement, BaseSize> Sizes = new();

    public static void Apply(Window window, ClipboardSettings settings)
    {
        var light = settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase);
        SetBrush(window, "PageBrush", light ? "#F0F2EC" : "#0E0F10");
        SetBrush(window, "CardBrush", light ? "#FFFFFF" : "#1B1D1E");
        SetBrush(window, "RaisedBrush", light ? "#E5E9DE" : "#26292A");
        SetBrush(window, "AccentBrush", light ? "#B8D91A" : "#D7FF2F");
        SetBrush(window, "MutedBrush", light ? "#58635B" : "#A6ACAA");
        SetBrush(window, "TextBrush", light ? "#18201A" : "#F5F7F2");
        SetBrush(window, "SelectedRowBrush", light ? "#DDECB1" : "#39442B");
        SetBrush(window, "HoverRowBrush", light ? "#E5E9DE" : "#303535");
        window.Resources["ListRowHeight"] = 72 * Math.Max(1, settings.FontScale);
        window.Resources["ListTextHeight"] = 42 * settings.FontScale;
        window.Resources["ListTitleHeight"] = 20 * settings.FontScale;
        window.Resources["ListPreviewHeight"] = 17 * settings.FontScale;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            var darkCaption = light ? 0 : 1;
            DwmSetWindowAttribute(handle, 20, ref darkCaption, sizeof(int));
        }
        window.FontSize = 14 * settings.FontScale;
        if (window.Content is DependencyObject content) ScaleChildren(content, settings.FontScale);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    private static void SetBrush(Window window, string key, string hex)
    {
        var replacement = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        foreach (var dictionary in window.Resources.MergedDictionaries)
        {
            if (!dictionary.Contains(key)) continue;
            dictionary[key] = replacement;
            return;
        }
        window.Resources[key] = replacement;
    }

    private static void ScaleChildren(DependencyObject parent, double scale)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock text)
            {
                if (DependencyPropertyHelper.GetValueSource(text, TextBlock.FontSizeProperty).BaseValueSource != BaseValueSource.Inherited)
                {
                    var baseline = Sizes.GetValue(text, e => new BaseSize(((TextBlock)e).FontSize)).Value;
                    text.FontSize = baseline * scale;
                }
            }
            else if (child is Control control)
            {
                if (DependencyPropertyHelper.GetValueSource(control, Control.FontSizeProperty).BaseValueSource != BaseValueSource.Inherited)
                {
                    var baseline = Sizes.GetValue(control, e => new BaseSize(((Control)e).FontSize)).Value;
                    control.FontSize = baseline * scale;
                }
            }
            ScaleChildren(child, scale);
        }
    }
}
