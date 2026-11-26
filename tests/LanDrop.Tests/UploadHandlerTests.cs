using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LanDrop.Config;
using LanDrop.Handlers;
using LanDrop.Security;
using LanDrop.Utils;
using Microsoft.AspNetCore.Http;

namespace LanDrop.Tests;

/// <summary>
/// UploadHandler のテスト
/// - ストリーム保存、同名の同時保存、失敗時の後始末
/// </summary>
public sealed class UploadHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly AppConfig _config;
    private readonly PathValidator _validator;

    public UploadHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lan-drop-upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _config = new AppConfig { RootDir = _root, MaxUploadMb = 1 };
        _validator = new PathValidator(_config.UploadsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static (Stream Body, string ContentType) BuildMultipart(params (string Name, byte[] Content)[] files)
    {
        var content = new MultipartFormDataContent("test-boundary");
        foreach (var (name, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, "file", name);
        }
        var ms = new MemoryStream();
        content.CopyTo(ms, null, default);
        ms.Position = 0;
        return (ms, content.Headers.ContentType!.ToString());
    }

    private static DefaultHttpContext CreateContext(Stream body, string contentType, long? contentLength = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = contentType;
        ctx.Request.Body = body;
        ctx.Request.ContentLength = contentLength ?? (body.CanSeek ? body.Length : null);
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static JsonElement ReadJson(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return JsonDocument.Parse(ctx.Response.Body).RootElement;
    }

    private string[] UploadedFiles()
        => Directory.Exists(_config.UploadsDir)
            ? Directory.GetFiles(_config.UploadsDir).Select(p => Path.GetFileName(p)).OrderBy(n => n).ToArray()
            : [];

    [Fact]
    public async Task MultipleFiles_AreSaved()
    {
        var (body, type) = BuildMultipart(("a.txt", "AAA"u8.ToArray()), ("b.txt", "BB"u8.ToArray()));
        var ctx = CreateContext(body, type);

        var bytes = await UploadHandler.HandleAsync(ctx, _config, _validator);

        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Equal(5, bytes);
        Assert.Equal(new[] { "a.txt", "b.txt" }, UploadedFiles());
        Assert.Equal("AAA", File.ReadAllText(Path.Combine(_config.UploadsDir, "a.txt")));
    }

    [Fact]
    public async Task SameNameInOneRequest_IsNumbered()
    {
        var (body, type) = BuildMultipart(("a.txt", "1"u8.ToArray()), ("a.txt", "2"u8.ToArray()));
        var ctx = CreateContext(body, type);

        await UploadHandler.HandleAsync(ctx, _config, _validator);

        Assert.Equal(new[] { "a (1).txt", "a.txt" }, UploadedFiles());
        var json = ReadJson(ctx);
        Assert.Equal("a (1).txt", json.GetProperty("files")[1].GetProperty("saved").GetString());
    }

    [Fact]
    public async Task ConcurrentSameNameUploads_DoNotOverwrite()
    {
        // 再現: 存在確認と作成が別だったため、同時に同名で送ると後の方が上書きしていた
        const int count = 16;
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () =>
        {
            var (body, type) = BuildMultipart(("same.txt", Encoding.UTF8.GetBytes($"content-{i}")));
            var ctx = CreateContext(body, type);
            await UploadHandler.HandleAsync(ctx, _config, _validator);
            return ctx.Response.StatusCode;
        }));

        var statuses = await Task.WhenAll(tasks);

        Assert.All(statuses, s => Assert.Equal(200, s));
        var contents = UploadedFiles().Select(n => File.ReadAllText(Path.Combine(_config.UploadsDir, n))).ToHashSet();
        Assert.Equal(count, contents.Count);
    }

    [Fact]
    public void CreateUniqueFile_ConcurrentCalls_ProduceDistinctFiles()
    {
        Directory.CreateDirectory(_config.UploadsDir);
        var names = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 32, _ =>
        {
            using var fs = FileNameSanitizer.CreateUniqueFile(_config.UploadsDir, "x.bin", out var name);
            names.Add(name);
        });

        Assert.Equal(32, names.Distinct().Count());
        Assert.Equal(32, UploadedFiles().Length);
    }

    [Fact]
    public async Task FailureMidway_RemovesAllFilesOfTheRequest()
    {
        // 1つ目のファイルを保存した後、2つ目の途中で通信が切れる
        var (body, type) = BuildMultipart(("first.txt", new byte[1000]), ("second.txt", new byte[50_000]));
        var failing = new FailingStream(body, failAfterBytes: 20_000, new IOException("connection reset"));
        var ctx = CreateContext(failing, type, contentLength: body.Length);

        await UploadHandler.HandleAsync(ctx, _config, _validator);

        Assert.Equal(500, ctx.Response.StatusCode);
        Assert.Empty(UploadedFiles());
    }

    [Fact]
    public async Task TooLargeWhileStreaming_Returns413AndRemovesFiles()
    {
        // Content-Length なし（チャンク転送）で、読み込み中に Kestrel が上限超過を通知する
        var (body, type) = BuildMultipart(("first.txt", new byte[1000]), ("big.bin", new byte[50_000]));
        var failing = new FailingStream(body, failAfterBytes: 20_000,
            new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));
        var ctx = CreateContext(failing, type, contentLength: null);

        await UploadHandler.HandleAsync(ctx, _config, _validator);

        Assert.Equal(413, ctx.Response.StatusCode);
        Assert.Empty(UploadedFiles());
    }

    [Fact]
    public async Task ContentLengthOverLimit_Returns413()
    {
        var (body, type) = BuildMultipart(("a.txt", "A"u8.ToArray()));
        var ctx = CreateContext(body, type, contentLength: _config.MaxUploadBytes + 1);

        await UploadHandler.HandleAsync(ctx, _config, _validator);

        Assert.Equal(413, ctx.Response.StatusCode);
        Assert.Empty(UploadedFiles());
    }

    [Fact]
    public async Task NotMultipart_Returns400()
    {
        var ctx = CreateContext(new MemoryStream("x"u8.ToArray()), "text/plain");

        await UploadHandler.HandleAsync(ctx, _config, _validator);

        Assert.Equal(400, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task NoFileField_Returns400()
    {
        var content = new MultipartFormDataContent("b") { { new StringContent("v"), "other" } };
        var ms = new MemoryStream();
        content.CopyTo(ms, null, default);
        ms.Position = 0;
        var ctx = CreateContext(ms, content.Headers.ContentType!.ToString());

        await UploadHandler.HandleAsync(ctx, _config, _validator);

        Assert.Equal(400, ctx.Response.StatusCode);
        Assert.Equal("NO_FILES", ReadJson(ctx).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReadOnly_Returns403()
    {
        _config.ReadOnly = true;
        var (body, type) = BuildMultipart(("a.txt", "A"u8.ToArray()));
        var ctx = CreateContext(body, type);

        await UploadHandler.HandleAsync(ctx, _config, _validator);

        Assert.Equal(403, ctx.Response.StatusCode);
    }

    /// <summary>
    /// 指定バイト数を読んだところで例外を投げるストリーム
    /// </summary>
    private sealed class FailingStream(Stream inner, long failAfterBytes, Exception error) : Stream
    {
        private long _read;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read >= failAfterBytes)
                throw error;
            var max = (int)Math.Min(buffer.Length, failAfterBytes - _read);
            var n = await inner.ReadAsync(buffer[..max], cancellationToken);
            _read += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
