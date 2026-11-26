using System.Collections.Frozen;

namespace LanDrop.Security;

/// <summary>
/// パストラバーサル防止のためのパス検証
/// </summary>
public sealed class PathValidator
{
    private readonly string _rootFullPath;
    private readonly string _rootWithSeparator;
    private readonly string _rootRealPath;
    private readonly string? _hiddenLogPath;

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

    /// <param name="rootDirectory">公開するルートディレクトリ</param>
    /// <param name="hiddenLogPath">公開しないログファイルのパス（ローテーションされた "&lt;path&gt;.N" も含む）</param>
    public PathValidator(string rootDirectory, string? hiddenLogPath = null)
    {
        _rootFullPath = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _rootWithSeparator = _rootFullPath + Path.DirectorySeparatorChar;
        _rootRealPath = ResolveRealPath(_rootFullPath);
        _hiddenLogPath = hiddenLogPath == null ? null : Path.GetFullPath(hiddenLogPath);
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

            // ログファイルは公開しない
            if (IsHiddenLogFile(resolvedPath))
            {
                errorCode = "FORBIDDEN_PATH";
                return false;
            }

            // シンボリックリンク・ジャンクション検証（途中の要素も含む）
            if (!ValidateLinks(resolvedPath))
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
    /// ルートから対象までの各要素について、リンク（シンボリックリンク・ジャンクション）が
    /// ルートの外を指していないか検証する
    /// </summary>
    /// <remarks>
    /// 最後の要素だけを見ると、途中のディレクトリがルート外へのジャンクションの場合に
    /// その先のファイルへ到達できてしまうため、存在する全要素を確認する。
    /// 判定できない場合は拒否する。
    /// </remarks>
    private bool ValidateLinks(string resolvedPath)
    {
        try
        {
            var relative = Path.GetRelativePath(_rootFullPath, resolvedPath);
            if (relative == ".")
                return true;

            var current = _rootFullPath;
            foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);

                // FileInfo はリンク自体を参照する（ディレクトリへのリンクやリンク切れでも LinkTarget を返す）
                var info = new FileInfo(current);
                if (info.LinkTarget != null)
                {
                    var target = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (target == null || !IsUnderRoot(Path.GetFullPath(target.FullName)))
                        return false;
                    continue;
                }

                if (!info.Exists && !Directory.Exists(current))
                {
                    // ここから先は存在しない（アップロード先の新規作成など）
                    return true;
                }
            }

            return true;
        }
        catch
        {
            // 判定できない場合は安全側（拒否）に倒す
            return false;
        }
    }

    private bool IsUnderRoot(string fullPath)
        => IsUnder(fullPath, _rootFullPath) || IsUnder(fullPath, _rootRealPath);

    private static bool IsUnder(string fullPath, string root)
    {
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ルート自体がリンクの場合はリンク先の実パスを返す
    /// </summary>
    private static string ResolveRealPath(string path)
    {
        try
        {
            var target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            return target == null
                ? path
                : Path.GetFullPath(target.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }

    /// <summary>
    /// ログファイル（ローテーション後の "&lt;path&gt;.N" を含む）か
    /// </summary>
    private bool IsHiddenLogFile(string fullPath)
    {
        if (_hiddenLogPath == null)
            return false;

        if (fullPath.Equals(_hiddenLogPath, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = _hiddenLogPath + ".";
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               fullPath.Length > prefix.Length &&
               fullPath[prefix.Length..].All(char.IsAsciiDigit);
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
