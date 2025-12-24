using System.Collections.Frozen;

namespace LanDrop.Utils;

/// <summary>
/// MIMEタイプ判定
/// </summary>
public static class MimeTypes
{
    private static readonly FrozenDictionary<string, string> ExtensionToMimeType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // テキスト
        { ".txt", "text/plain" },
        { ".html", "text/html" },
        { ".htm", "text/html" },
        { ".css", "text/css" },
        { ".js", "application/javascript" },
        { ".json", "application/json" },
        { ".xml", "application/xml" },
        { ".csv", "text/csv" },
        { ".md", "text/markdown" },
        
        // 画像
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".bmp", "image/bmp" },
        { ".ico", "image/x-icon" },
        { ".svg", "image/svg+xml" },
        { ".webp", "image/webp" },
        
        // ドキュメント
        { ".pdf", "application/pdf" },
        { ".doc", "application/msword" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".xls", "application/vnd.ms-excel" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".ppt", "application/vnd.ms-powerpoint" },
        { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        
        // アーカイブ
        { ".zip", "application/zip" },
        { ".gz", "application/gzip" },
        { ".tar", "application/x-tar" },
        { ".rar", "application/vnd.rar" },
        { ".7z", "application/x-7z-compressed" },
        
        // 音声
        { ".mp3", "audio/mpeg" },
        { ".wav", "audio/wav" },
        { ".ogg", "audio/ogg" },
        
        // 動画
        { ".mp4", "video/mp4" },
        { ".avi", "video/x-msvideo" },
        { ".mov", "video/quicktime" },
        { ".webm", "video/webm" },
        
        // その他
        { ".exe", "application/octet-stream" },
        { ".dll", "application/octet-stream" },
        { ".iso", "application/x-iso9660-image" }
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ファイル名からMIMEタイプを取得
    /// </summary>
    public static string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
            return "application/octet-stream";

        return ExtensionToMimeType.TryGetValue(extension, out var mimeType)
            ? mimeType
            : "application/octet-stream";
    }

    /// <summary>
    /// テキスト系MIMEタイプか判定
    /// </summary>
    public static bool IsTextType(string mimeType)
    {
        return mimeType.StartsWith("text/") || 
               mimeType == "application/json" ||
               mimeType == "application/xml" ||
               mimeType == "application/javascript";
    }
}
