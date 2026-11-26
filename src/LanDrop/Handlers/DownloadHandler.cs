using LanDrop.Security;
using LanDrop.Utils;
using Microsoft.Net.Http.Headers;

namespace LanDrop.Handlers;

/// <summary>
/// ファイルダウンロード処理
/// </summary>
public static class DownloadHandler
{
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
            var mimeType = MimeTypes.GetMimeType(fileInfo.Name);

            // Range（途中からの再開・動画のシーク）、If-Range / If-None-Match に対応する。
            // ファイル名は Content-Disposition の filename / filename*（RFC 5987）で日本語も扱える。
            var lastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc);
            var etag = new EntityTagHeaderValue($"\"{fileInfo.Length:x}-{fileInfo.LastWriteTimeUtc.Ticks:x}\"");
            var result = TypedResults.PhysicalFile(
                fullPath,
                mimeType,
                fileDownloadName: fileInfo.Name,
                lastModified: lastModified,
                entityTag: etag,
                enableRangeProcessing: true);

            await result.ExecuteAsync(context);

            var status = context.Response.StatusCode;
            return status is StatusCodes.Status200OK or StatusCodes.Status206PartialContent
                && !HttpMethods.IsHead(context.Request.Method)
                ? context.Response.ContentLength ?? 0
                : 0;
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
