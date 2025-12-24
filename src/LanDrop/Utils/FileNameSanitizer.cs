using System.Collections.Frozen;

namespace LanDrop.Utils;

/// <summary>
/// ファイル名のサニタイズと重複採番
/// </summary>
public static class FileNameSanitizer
{
    // Windowsの予約名
    private static readonly FrozenSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // ファイル名に使えない文字（FrozenSetでO(1)検索）
    private static readonly FrozenSet<char> InvalidChars = Path.GetInvalidFileNameChars().ToFrozenSet();

    /// <summary>
    /// ファイル名をサニタイズ
    /// </summary>
    public static string Sanitize(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed";

        // 禁止文字を除去
        var sanitized = new string(fileName.Where(c => !InvalidChars.Contains(c)).ToArray());

        // 空白のみになった場合
        if (string.IsNullOrWhiteSpace(sanitized))
            return "unnamed";

        // 末尾の空白とドットを除去（Windowsの制限）
        sanitized = sanitized.TrimEnd(' ', '.');

        if (string.IsNullOrEmpty(sanitized))
            return "unnamed";

        // 予約名チェック
        var baseName = Path.GetFileNameWithoutExtension(sanitized);
        if (ReservedNames.Contains(baseName))
        {
            sanitized = "_" + sanitized;
        }

        // 長すぎる場合は切り詰め（255文字制限）
        if (sanitized.Length > 255)
        {
            var ext = Path.GetExtension(sanitized);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
            var maxNameLength = 255 - ext.Length;
            sanitized = nameWithoutExt.Substring(0, Math.Max(1, maxNameLength)) + ext;
        }

        return sanitized;
    }

    /// <summary>
    /// 重複ファイルがある場合に採番する
    /// </summary>
    /// <param name="directory">対象ディレクトリ</param>
    /// <param name="fileName">元のファイル名</param>
    /// <returns>採番済みファイル名</returns>
    public static string GetUniqueFileName(string directory, string fileName)
    {
        var sanitized = Sanitize(fileName);
        var candidate = sanitized;
        var counter = 1;

        while (File.Exists(Path.Combine(directory, candidate)))
        {
            var baseName = Path.GetFileNameWithoutExtension(sanitized);
            var ext = Path.GetExtension(sanitized);
            candidate = $"{baseName} ({counter}){ext}";
            counter++;

            // 無限ループ防止
            if (counter > 10000)
            {
                throw new InvalidOperationException("Too many duplicate files");
            }
        }

        return candidate;
    }

    /// <summary>
    /// ファイル名が有効か判宁E
    /// </summary>
    public static bool IsValidFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (fileName.Any(c => InvalidChars.Contains(c)))
            return false;

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (ReservedNames.Contains(baseName))
            return false;

        return true;
    }
}
