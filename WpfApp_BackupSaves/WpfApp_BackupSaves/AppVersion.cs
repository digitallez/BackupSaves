using System.Reflection;

namespace WpfApp_BackupSaves;

public static class AppVersion
{
    /// <summary>Display version, e.g. 1.0.3 or 1.0.3-debug</summary>
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // strip possible "+git" suffix
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
