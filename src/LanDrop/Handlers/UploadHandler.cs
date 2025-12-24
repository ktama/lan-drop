using LanDrop.Config;
using LanDrop.Security;
using LanDrop.Utils;

namespace LanDrop.Handlers;

/// <summary>
/// ファイルアップロード処理
/// </summary>
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
            context.Response.StatusCode = 413;
            await context.Response.WriteAsJsonAsync(new { 
                success = false, 
                error = $"File too large. Max size: {config.MaxUploadMb} MB", 
                code = "PAYLOAD_TOO_LARGE" 
            });
            return 0;
        }

        // multipart/form-data チェック
        if (!context.Request.HasFormContentType)
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

        // uploadsディレクトリ作成
        if (!Directory.Exists(config.UploadsDir))
        {
            Directory.CreateDirectory(config.UploadsDir);
        }
        if (!Directory.Exists(uploadDir))
        {
            Directory.CreateDirectory(uploadDir);
        }

        try
        {
            var form = await context.Request.ReadFormAsync();
            var files = form.Files.GetFiles("file");

            if (files.Count == 0)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { success = false, error = "No files uploaded", code = "NO_FILES" });
                return 0;
            }

            var uploadedFiles = new List<UploadedFileInfo>();
            long totalBytes = 0;

            foreach (var file in files)
            {
                // サイズチェック（個別ファイル）
                if (file.Length > config.MaxUploadBytes)
                {
                    context.Response.StatusCode = 413;
                    await context.Response.WriteAsJsonAsync(new { 
                        success = false, 
                        error = $"File '{file.FileName}' too large. Max size: {config.MaxUploadMb} MB", 
                        code = "PAYLOAD_TOO_LARGE" 
                    });
                    return totalBytes;
                }

                // ファイル名サニタイズと採番
                var originalName = file.FileName;
                var safeName = FileNameSanitizer.GetUniqueFileName(uploadDir, originalName);
                var savePath = Path.Combine(uploadDir, safeName);

                // ファイル保存
                await using var stream = new FileStream(savePath, FileMode.Create, FileAccess.Write);
                await file.CopyToAsync(stream);

                uploadedFiles.Add(new UploadedFileInfo
                {
                    Original = originalName,
                    Saved = safeName,
                    Size = file.Length
                });

                totalBytes += file.Length;
            }

            await context.Response.WriteAsJsonAsync(new UploadResponse
            {
                Success = true,
                Files = uploadedFiles.ToArray()
            });

            return totalBytes;
        }
        catch (Exception)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Upload failed", code = "INTERNAL_ERROR" });
            return 0;
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
