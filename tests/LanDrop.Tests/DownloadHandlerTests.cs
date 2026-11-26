using System.Text;
using LanDrop.Handlers;
using LanDrop.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LanDrop.Tests;

/// <summary>
/// DownloadHandler のテスト
/// - Range（部分取得）、条件付きリクエスト、日本語ファイル名
/// </summary>
public sealed class DownloadHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly PathValidator _validator;

    public DownloadHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lan-drop-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "abc.txt"), "abcdefghij");
        File.WriteAllText(Path.Combine(_root, "テスト.txt"), "jp");
        _validator = new PathValidator(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static DefaultHttpContext CreateContext(string path, Action<HttpRequest>? configure = null)
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        ctx.Request.Method = "GET";
        ctx.Request.QueryString = QueryString.Create("path", path);
        ctx.Response.Body = new MemoryStream();
        configure?.Invoke(ctx.Request);
        return ctx;
    }

    private static string Body(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return new StreamReader(ctx.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    [Fact]
    public async Task FullDownload_Returns200WithBody()
    {
        var ctx = CreateContext("abc.txt");

        var bytes = await DownloadHandler.HandleAsync(ctx, _validator, "shared");

        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Equal("abcdefghij", Body(ctx));
        Assert.Equal(10, bytes);
        Assert.Equal("bytes", ctx.Response.Headers.AcceptRanges.ToString());
        Assert.False(string.IsNullOrEmpty(ctx.Response.Headers.ETag.ToString()));
    }

    [Fact]
    public async Task RangeRequest_Returns206WithPart()
    {
        var ctx = CreateContext("abc.txt", r => r.Headers.Range = "bytes=2-4");

        var bytes = await DownloadHandler.HandleAsync(ctx, _validator, "shared");

        Assert.Equal(206, ctx.Response.StatusCode);
        Assert.Equal("cde", Body(ctx));
        Assert.Equal(3, bytes);
        Assert.Equal("bytes 2-4/10", ctx.Response.Headers.ContentRange.ToString());
    }

    [Fact]
    public async Task UnsatisfiableRange_Returns416()
    {
        var ctx = CreateContext("abc.txt", r => r.Headers.Range = "bytes=100-200");

        await DownloadHandler.HandleAsync(ctx, _validator, "shared");

        Assert.Equal(416, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task IfNoneMatch_SameEtag_Returns304()
    {
        var first = CreateContext("abc.txt");
        await DownloadHandler.HandleAsync(first, _validator, "shared");
        var etag = first.Response.Headers.ETag.ToString();

        var ctx = CreateContext("abc.txt", r => r.Headers.IfNoneMatch = etag);
        var bytes = await DownloadHandler.HandleAsync(ctx, _validator, "shared");

        Assert.Equal(304, ctx.Response.StatusCode);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public async Task JapaneseFileName_UsesRfc5987()
    {
        var ctx = CreateContext("テスト.txt");

        await DownloadHandler.HandleAsync(ctx, _validator, "shared");

        var disposition = ctx.Response.Headers.ContentDisposition.ToString();
        Assert.StartsWith("attachment", disposition);
        Assert.Contains("filename*=UTF-8''%E3%83%86%E3%82%B9%E3%83%88.txt", disposition);
    }

    [Fact]
    public async Task MissingFile_Returns404()
    {
        var ctx = CreateContext("nope.txt");

        await DownloadHandler.HandleAsync(ctx, _validator, "shared");

        Assert.Equal(404, ctx.Response.StatusCode);
    }
}
