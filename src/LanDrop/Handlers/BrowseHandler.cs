using System.Text.Json;
using LanDrop.Config;
using LanDrop.Security;

namespace LanDrop.Handlers;

/// <summary>
/// ディレクトリ一覧JSON応答
/// </summary>
public static class BrowseHandler
{
    public static async Task HandleAsync(
        HttpContext context,
        AppConfig config,
        PathValidator sharedValidator,
        PathValidator uploadsValidator)
    {
        var query = context.Request.Query;
        var area = query["area"].ToString();
        var path = query["path"].ToString();

        // エリア検証
        if (area != "shared" && area != "uploads")
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Invalid area", code = "INVALID_AREA" });
            return;
        }

        // パス検証
        var validator = area == "shared" ? sharedValidator : uploadsValidator;
        if (!validator.TryValidate(path, out var fullPath, out var errorCode))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Invalid path", code = errorCode });
            return;
        }

        // ディレクトリ存在確認
        if (!Directory.Exists(fullPath))
        {
            // uploadsディレクトリが存在しない場合は空で返す
            if (area == "uploads" && string.IsNullOrEmpty(path))
            {
                await context.Response.WriteAsJsonAsync(new BrowseResponse
                {
                    Path = path,
                    Directories = Array.Empty<DirectoryItem>(),
                    Files = Array.Empty<FileItem>()
                });
                return;
            }

            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Directory not found", code = "DIR_NOT_FOUND" });
            return;
        }

        try
        {
            var dirInfo = new DirectoryInfo(fullPath);
            var rootPath = area == "shared" ? config.RootDir : config.UploadsDir;
            
            // サブディレクトリ一覧
            var directories = dirInfo.GetDirectories()
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Where(d => !(area == "shared" && d.Name == AppConfig.UploadsDirName)) // _uploadsは除外
                .Select(d => new DirectoryItem
                {
                    Name = d.Name,
                    Path = GetRelativePath(d.FullName, rootPath)
                })
                .OrderBy(d => d.Name)
                .ToArray();

            // ファイル一覧
            var files = dirInfo.GetFiles()
                .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(f => new FileItem
                {
                    Name = f.Name,
                    Path = GetRelativePath(f.FullName, rootPath),
                    Size = f.Length,
                    Modified = f.LastWriteTimeUtc.ToString("o")
                })
                .OrderBy(f => f.Name)
                .ToArray();

            var response = new BrowseResponse
            {
                Path = path,
                Directories = directories,
                Files = files
            };

            await context.Response.WriteAsJsonAsync(response);
        }
        catch (Exception)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Internal error", code = "INTERNAL_ERROR" });
        }
    }

    private static string GetRelativePath(string fullPath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}

public class BrowseResponse
{
    public string Path { get; set; } = "";
    public DirectoryItem[] Directories { get; set; } = Array.Empty<DirectoryItem>();
    public FileItem[] Files { get; set; } = Array.Empty<FileItem>();
}

public class DirectoryItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

public class FileItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Modified { get; set; } = "";
}
