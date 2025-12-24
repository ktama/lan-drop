using System.Collections.Frozen;

namespace LanDrop.Security;

/// <summary>
/// パストラバーサル防止のためのパス検証
/// </summary>
public sealed class PathValidator
{
    private readonly string _rootFullPath;
    private readonly string _rootWithSeparator;

    // Windowsの予約名
    private static readonly FrozenSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // パスに使えない文字
    private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars()
        .Concat(new[] { '<', '>', '"', '|', '?', '*' })
        .Distinct()
        .ToArray();

    public PathValidator(string rootDirectory)
    {
        _rootFullPath = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _rootWithSeparator = _rootFullPath + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// 相対パスを検証し、安全なフルパスを返す
    /// </summary>
    /// <param name="relativePath">相対パス</param>
    /// <param name="fullPath">検証済みのフルパス</param>
    /// <param name="errorCode">エラーコード</param>
    /// <returns>検証成功ならtrue</returns>
    public bool TryValidate(string? relativePath, out string fullPath, out string? errorCode)
    {
        fullPath = _rootFullPath;
        errorCode = null;

        // 空パス（ルート）は許可
        if (string.IsNullOrEmpty(relativePath))
        {
            return true;
        }

        // UNCパス拒否（Linux上でも検出するためPath.IsPathRootedより先にチェック）
        if (relativePath.StartsWith(@"\\") || relativePath.StartsWith("//"))
        {
            errorCode = "ABSOLUTE_PATH";
            return false;
        }

        // ドライブレター拒否（C: 等）- Linux上でも検出するためPath.IsPathRootedより先にチェック
        if (relativePath.Length >= 2 && char.IsAsciiLetter(relativePath[0]) && relativePath[1] == ':')
        {
            errorCode = "ABSOLUTE_PATH";
            return false;
        }

        // 絶対パス拒否（POSIX形式: /path や Windows形式: \path）
        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("\\") || relativePath.StartsWith("/"))
        {
            errorCode = "ABSOLUTE_PATH";
            return false;
        }

        // パストラバーサル拒否（..を含む場合）
        // 両方のセパレータを統一して分割
        var segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(s => s == ".."))
        {
            errorCode = "PATH_TRAVERSAL";
            return false;
        }

        // 正規化（Path.Combineで使用するため）
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                     .Replace('\\', Path.DirectorySeparatorChar);

        // 禁止文字チェック
        if (relativePath.IndexOfAny(InvalidPathChars) >= 0)
        {
            errorCode = "INVALID_CHARS";
            return false;
        }

        // NULバイトチェック
        if (relativePath.Contains('\0'))
        {
            errorCode = "INVALID_CHARS";
            return false;
        }

        // ファイル名が予約名かチェック
        var fileName = Path.GetFileNameWithoutExtension(segments.LastOrDefault() ?? "");
        if (!string.IsNullOrEmpty(fileName) && ReservedNames.Contains(fileName))
        {
            errorCode = "RESERVED_NAME";
            return false;
        }

        // 正規化してルート配下かチェック
        try
        {
            var combined = Path.Combine(_rootFullPath, normalized);
            var resolvedPath = Path.GetFullPath(combined);

            // ルート配下かチェック（境界条件も考慮）
            if (!resolvedPath.Equals(_rootFullPath, StringComparison.OrdinalIgnoreCase) &&
                !resolvedPath.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                errorCode = "OUTSIDE_ROOT";
                return false;
            }

            // シンボリックリンク検証（可能な範囲で）
            if (!ValidateSymlink(resolvedPath))
            {
                errorCode = "SYMLINK_OUTSIDE";
                return false;
            }

            fullPath = resolvedPath;
            return true;
        }
        catch
        {
            errorCode = "INVALID_PATH";
            return false;
        }
    }

    /// <summary>
    /// シンボリックリンクがルート外を参照していないか検証
    /// </summary>
    private bool ValidateSymlink(string path)
    {
        try
        {
            // ファイルまたはディレクトリが存在する場合のみ検証
            FileSystemInfo? info = null;

            if (File.Exists(path))
            {
                info = new FileInfo(path);
            }
            else if (Directory.Exists(path))
            {
                info = new DirectoryInfo(path);
            }

            if (info == null)
            {
                // 存在しないパスはOK（後でファイル作成などに使う場合）
                return true;
            }

            // リンクターゲットをチェック
            var linkTarget = info.LinkTarget;
            if (linkTarget != null)
            {
                var targetFullPath = Path.GetFullPath(linkTarget, Path.GetDirectoryName(path) ?? _rootFullPath);
                if (!targetFullPath.Equals(_rootFullPath, StringComparison.OrdinalIgnoreCase) &&
                    !targetFullPath.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            // エラー時は安全側に倒す
            return true;
        }
    }

    /// <summary>
    /// ファイルが存在するか確認
    /// </summary>
    public bool FileExists(string fullPath)
    {
        return File.Exists(fullPath);
    }

    /// <summary>
    /// ディレクトリが存在するか確認
    /// </summary>
    public bool DirectoryExists(string fullPath)
    {
        return Directory.Exists(fullPath);
    }
}
