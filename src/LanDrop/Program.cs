using System.Diagnostics;
using System.Text;
using LanDrop.Config;
using LanDrop.Handlers;
using LanDrop.Lifecycle;
using LanDrop.Logging;
using LanDrop.Security;
using LanDrop.Utils;

// CLI解析
var (config, error, showHelp) = CliParser.Parse(args);

if (showHelp)
{
    Console.WriteLine(CliParser.GetHelpText());
    return 0;
}

if (error != null || config == null)
{
    Console.Error.WriteLine($"Error: {error}");
    Console.Error.WriteLine("Use --help for usage information.");
    return 1;
}

// トークン生成
var token = config.Token ?? TokenValidator.GenerateToken();
var tokenValidator = new TokenValidator(token);

// IP制限
IpFilter ipFilter;
try
{
    ipFilter = new IpFilter(config.AllowedCidrs);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

// パス検証器
var sharedValidator = new PathValidator(config.RootDir);
var uploadsValidator = new PathValidator(config.UploadsDir);

// ログ
var logger = new AccessLogger(config.EffectiveLogPath);

// ポート決定
int port;
if (config.Port.HasValue)
{
    port = config.Port.Value;
    if (!NetworkUtils.IsPortAvailable(port, config.BindAddress))
    {
        Console.Error.WriteLine($"Error: Port {port} is not available.");
        return 1;
    }
}
else
{
    var foundPort = NetworkUtils.FindAvailablePort(8000, 8100, config.BindAddress);
    if (!foundPort.HasValue)
    {
        Console.Error.WriteLine("Error: No available port found in range 8000-8100.");
        return 1;
    }
    port = foundPort.Value;
}

// ASP.NET Core Minimal API セットアップ
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = config.RootDir
});

// Kestrel設定
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port);
});

// ログを最小限に
builder.Logging.ClearProviders();

// リクエストサイズ制限
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = config.MaxUploadBytes;
});

var app = builder.Build();

// ライフサイクル管理
LifecycleManager? lifecycleManager = null;
StopReason? stopReason = null;

app.Lifetime.ApplicationStarted.Register(() =>
{
    lifecycleManager = new LifecycleManager(
        config.TtlMinutes,
        config.IdleMinutes,
        app.Lifetime,
        reason =>
        {
            stopReason = reason;
            Console.WriteLine($"\nShutting down: {LifecycleManager.GetReasonDescription(reason)}");
        });
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    // 停止ログ
    logger.LogStop(new StopLogEntry
    {
        Reason = stopReason.HasValue
            ? LifecycleManager.GetReasonDescription(stopReason.Value)
            : "unknown",
        UptimeSeconds = lifecycleManager?.UptimeSeconds ?? 0
    });

    lifecycleManager?.Dispose();
    logger.Dispose();

    Console.WriteLine("Goodbye.");
});

// ミドルウェア: IP制限
app.Use(async (context, next) =>
{
    if (!ipFilter.IsAllowed(context))
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new { success = false, error = "Access denied", code = "IP_DENIED" });
        return;
    }
    await next();
});

// ミドルウェア: トークン検証
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "/";

    if (!tokenValidator.ValidatePath(path, out var remainingPath))
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync("Not Found");
        return;
    }

    // 残りのパスを保存
    context.Items["RemainingPath"] = remainingPath;
    context.Items["Token"] = token;

    // アクティビティ記録
    lifecycleManager?.RecordActivity();

    await next();
});

// ルーティング
app.Map($"/{token}/", async context =>
{
    var sw = Stopwatch.StartNew();
    var html = IndexHandler.GenerateHtml(
        config,
        token,
        port,
        lifecycleManager?.RemainingTtlMinutes ?? config.TtlMinutes);

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(html);

    sw.Stop();
    logger.Log(AccessLogger.CreateEntry(
        context, "/", null, null, null, 200, Encoding.UTF8.GetByteCount(html), (int)sw.ElapsedMilliseconds));
});

app.Map($"/{token}/browse", async context =>
{
    var sw = Stopwatch.StartNew();
    var area = context.Request.Query["area"].ToString();
    var path = context.Request.Query["path"].ToString();

    await BrowseHandler.HandleAsync(context, config, sharedValidator, uploadsValidator);

    sw.Stop();
    logger.Log(AccessLogger.CreateEntry(
        context, "/browse", $"area={area}&path={path}", area, path,
        context.Response.StatusCode, 0, (int)sw.ElapsedMilliseconds));
});

app.Map($"/{token}/dl", async context =>
{
    var sw = Stopwatch.StartNew();
    var path = context.Request.Query["path"].ToString();

    var bytes = await DownloadHandler.HandleAsync(context, sharedValidator, "shared");

    sw.Stop();
    logger.Log(AccessLogger.CreateEntry(
        context, "/dl", $"path={path}", "shared", path,
        context.Response.StatusCode, bytes, (int)sw.ElapsedMilliseconds));
});

app.Map($"/{token}/udl", async context =>
{
    var sw = Stopwatch.StartNew();
    var path = context.Request.Query["path"].ToString();

    // uploadsディレクトリが存在しない場合
    if (!Directory.Exists(config.UploadsDir))
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsJsonAsync(new { success = false, error = "File not found", code = "FILE_NOT_FOUND" });
        sw.Stop();
        logger.Log(AccessLogger.CreateEntry(
            context, "/udl", $"path={path}", "uploads", path, 404, 0, (int)sw.ElapsedMilliseconds));
        return;
    }

    var bytes = await DownloadHandler.HandleAsync(context, uploadsValidator, "uploads");

    sw.Stop();
    logger.Log(AccessLogger.CreateEntry(
        context, "/udl", $"path={path}", "uploads", path,
        context.Response.StatusCode, bytes, (int)sw.ElapsedMilliseconds));
});

app.Map($"/{token}/upload", async context =>
{
    if (context.Request.Method != "POST")
    {
        context.Response.StatusCode = 405;
        await context.Response.WriteAsJsonAsync(new { success = false, error = "Method not allowed", code = "METHOD_NOT_ALLOWED" });
        return;
    }

    var sw = Stopwatch.StartNew();
    var path = context.Request.Query["path"].ToString();

    var bytes = await UploadHandler.HandleAsync(context, config, uploadsValidator);

    sw.Stop();
    logger.Log(AccessLogger.CreateEntry(
        context, "/upload", $"path={path}", "uploads", path,
        context.Response.StatusCode, bytes, (int)sw.ElapsedMilliseconds));
});

// その他のパスは404
app.MapFallback(async context =>
{
    context.Response.StatusCode = 404;
    await context.Response.WriteAsync("Not Found");
});

// 起動情報表示
var localIp = NetworkUtils.GetPrimaryLocalIp() ?? "localhost";
var url = $"http://{localIp}:{port}/{token}/";

Console.WriteLine("""

    LAN Drop v1.0.0
    ========================================================================================================================
    """);
Console.WriteLine($"  Root:     {config.RootDir}");
Console.WriteLine($"  URL:      {url}");
Console.WriteLine($"  Token:    {token} {(config.Token == null ? "(auto-generated)" : "(user-specified)")}");
Console.WriteLine($"  TTL:      {config.TtlMinutes} minutes");
Console.WriteLine($"  Idle:     {config.IdleMinutes} minutes");
Console.WriteLine($"  Log:      {config.EffectiveLogPath}");
if (ipFilter.HasRestrictions)
{
    Console.WriteLine($"  IP Allow: {config.AllowedCidrs}");
}
if (config.ReadOnly)
{
    Console.WriteLine("  Mode:     READ ONLY (uploads disabled)");
}
Console.WriteLine("""
    ========================================================================================================================
      Press Ctrl+C to stop.
    
    """);

// 起動ログ
logger.LogStart(new StartLogEntry
{
    Root = config.RootDir,
    Bind = config.BindAddress,
    Port = port,
    Token = token,
    TtlMinutes = config.TtlMinutes,
    IdleMinutes = config.IdleMinutes
});

// ブラウザを開く
if (config.OpenBrowser)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch
    {
        // 失敗しても続行
    }
}

// 実行
await app.RunAsync();

return 0;
