using System.Reflection;

namespace ClipboardApp;

public static class AppVersion
{
    public static string Display { get; } = "v" +
        (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
         ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3)
         ?? "未知版本");

    public static string QuickPanelTitle => $"快捷取用 {Display}";
    public static string ManagerTitle => $"零零快捷剪貼板 {Display}";
}
