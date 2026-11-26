using LanDrop.Config;
using LanDrop.Security;
using LanDrop.Utils;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace LanDrop.Handlers;

/// <summary>
/// ファイルアップロード処理
/// </summary>
/// <remarks>
/// multipart をストリームのまま読み、保存先へ直接書き込む（一時ファイルを経由しない）。
/// 1リクエスト内のファイルは「全部保存」か「何も残さない」のどちらかにする。
/// 途中で失敗した場合は、そのリクエストで作成したファイルをすべて削除する。
/// </remarks>
public static class UploadHandler
{
    public static async Task<long> HandleAsync(
        HttpContext context,
        AppConfig config,
        PathValidator uploadsValidator)
    {
        // 読み取り専用チェック
        if (config.ReadOnly)
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Uploads disabled (readonly mode)", code = "READONLY_MODE" });
            return 0;
        }

        // Content-Lengthチェック（事前検証）
        if (context.Request.ContentLength > config.MaxUploadBytes)
        {
            await WriteTooLargeAsync(context, config);
            return 0;
        }

        // multipart/form-data チェック
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var mediaType) ||
            !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Invalid content type", code = "INVALID_CONTENT_TYPE" });
            return 0;
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Invalid content type", code = "INVALID_CONTENT_TYPE" });
            return 0;
        }

        // アップロード先ディレクトリ
        var relativePath = context.Request.Query["path"].ToString();
        if (!uploadsValidator.TryValidate(relativePath, out var uploadDir, out var errorCode))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Invalid path", code = errorCode });
            return 0;
        }

        // 保存したファイル（失敗時の削除用）
        var savedPaths = new List<string>();
        var uploadedFiles = new List<UploadedFileInfo>();
        long totalBytes = 0;

        try
        {
            Directory.CreateDirectory(config.UploadsDir);
            Directory.CreateDirectory(uploadDir);

            var reader = new MultipartReader(boundary, context.Request.Body);
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(context.RequestAborted)) != null)
            {
                // "file" フィールドのファイル以外は読み飛ばす（次の ReadNextSectionAsync で破棄される）
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                    !disposition.IsFileDisposition() ||
                    !string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, "file", StringComparison.Ordinal))
                {
                    continue;
                }

                var originalName = HeaderUtilities.RemoveQuotes(
                    disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? "";

                long size;
                string savedName;
                await using (var stream = FileNameSanitizer.CreateUniqueFile(uploadDir, originalName, out savedName))
                {
                    savedPaths.Add(stream.Name);
                    await section.Body.CopyToAsync(stream, context.RequestAborted);
                    size = stream.Length;
                }

                uploadedFiles.Add(new UploadedFileInfo
                {
                    Original = originalName,
                    Saved = savedName,
                    Size = size
                });
                totalBytes += size;
            }

            if (uploadedFiles.Count == 0)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { success = false, error = "No files uploaded", code = "NO_FILES" });
                return 0;
            }

            await context.Response.WriteAsJsonAsync(new UploadResponse
            {
                Success = true,
                Files = uploadedFiles.ToArray()
            });

            return totalBytes;
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // チャンク転送などで Content-Length が無く、読み込み中に上限を超えた
            DeleteAll(savedPaths);
            await WriteTooLargeAsync(context, config);
            return 0;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // クライアントが中断した（応答は返せない）
            DeleteAll(savedPaths);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            // multipart の形式不正
            DeleteAll(savedPaths);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { success = false, error = "Invalid multipart body", code = "INVALID_MULTIPART" });
            }
            return 0;
        }
        catch (Exception)
        {
            DeleteAll(savedPaths);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { success = false, error = "Upload failed", code = "INTERNAL_ERROR" });
            }
            return 0;
        }
    }

    private static async Task WriteTooLargeAsync(HttpContext context, AppConfig config)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = 413;
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = $"File too large. Max size: {config.MaxUploadMb} MB",
            code = "PAYLOAD_TOO_LARGE"
        });
    }

    private static void DeleteAll(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // 削除できなくても応答は返す
            }
        }
    }
}

public class UploadResponse
{
    public bool Success { get; set; }
    public UploadedFileInfo[] Files { get; set; } = Array.Empty<UploadedFileInfo>();
}

public class UploadedFileInfo
{
    public string Original { get; set; } = "";
    public string Saved { get; set; } = "";
    public long Size { get; set; }
}
