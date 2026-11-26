using System.Reflection;

namespace LanDrop;

/// <summary>
/// アプリケーション情報
/// </summary>
public static class AppInfo
{
    /// <summary>
    /// バージョン（csproj の &lt;Version&gt; から。ビルドメタデータ "+commit" は除く）
    /// </summary>
    public static string Version { get; } = GetVersion();

    private static string GetVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString(3)
                      ?? "0.0.0";
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
