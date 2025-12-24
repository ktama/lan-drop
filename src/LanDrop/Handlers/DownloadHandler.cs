using LanDrop.Security;
using LanDrop.Utils;

namespace LanDrop.Handlers;

/// <summary>
/// ファイルダウンロード処理
/// </summary>
public static class DownloadHandler
{
    private const int BufferSize = 81920; // 80KB

    public static async Task<long> HandleAsync(
        HttpContext context,
        PathValidator validator,
        string area)
    {
        var path = context.Request.Query["path"].ToString();

        // パス検証
        if (string.IsNullOrEmpty(path))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Path is required", code = "INVALID_PATH" });
            return 0;
        }

        if (!validator.TryValidate(path, out var fullPath, out var errorCode))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Invalid path", code = errorCode });
            return 0;
        }

        // ファイル存在確認
        if (!File.Exists(fullPath))
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "File not found", code = "FILE_NOT_FOUND" });
            return 0;
        }

        try
        {
            var fileInfo = new FileInfo(fullPath);
            var fileName = fileInfo.Name;
            var mimeType = MimeTypes.GetMimeType(fileName);

            // レスポンスヘッダー設定
            context.Response.ContentType = mimeType;
            context.Response.ContentLength = fileInfo.Length;
            
            // Content-Disposition (RFC 5987形式で日本語対応)
            // ASCII文字のみ抽出（StringBuilderで効率化）
            var asciiBuilder = new System.Text.StringBuilder(fileName.Length);
            foreach (var c in fileName)
            {
                if (c < 128) asciiBuilder.Append(c);
            }
            var asciiName = asciiBuilder.Length > 0 ? asciiBuilder.ToString() : "download";
            var utf8Encoded = Uri.EscapeDataString(fileName);
            context.Response.Headers.ContentDisposition = 
                $"attachment; filename=\"{asciiName}\"; filename*=UTF-8''{utf8Encoded}";

            // ストリーミング転送
            await using var fileStream = new FileStream(
                fullPath, 
                FileMode.Open, 
                FileAccess.Read, 
                FileShare.Read, 
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            
            await fileStream.CopyToAsync(context.Response.Body);

            return fileInfo.Length;
        }
        catch (Exception)
        {
            // すでにレスポンスが始まっている場合はヘッダーを変更できない
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { success = false, error = "Internal error", code = "INTERNAL_ERROR" });
            }
            return 0;
        }
    }
}
