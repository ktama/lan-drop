using LanDrop.Config;
using LanDrop.Handlers;

namespace LanDrop.Tests;

/// <summary>
/// IndexHandler のテスト
/// - HTML生成、displayHost反映、ReadOnlyモード
/// </summary>
public class IndexHandlerTests
{
    private static AppConfig CreateConfig(bool readOnly = false, int maxUploadMb = 200)
    {
        return new AppConfig
        {
            RootDir = Path.Combine(Path.GetTempPath(), "lan-drop-test"),
            ReadOnly = readOnly,
            MaxUploadMb = maxUploadMb
        };
    }

    // ── displayHost 反映 ────────────────────────────────

    [Fact]
    public void GenerateHtml_ContainsDisplayHost_InBaseUrl()
    {
        var config = CreateConfig();
        var html = IndexHandler.GenerateHtml(config, "abc123", 8080, 60, "192.168.1.100");

        Assert.Contains("http://192.168.1.100:8080/abc123", html);
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("localhost")]
    public void GenerateHtml_UsesGivenDisplayHost(string displayHost)
    {
        var config = CreateConfig();
        var html = IndexHandler.GenerateHtml(config, "tok", 9000, 30, displayHost);

        Assert.Contains($"http://{displayHost}:9000/tok", html);
    }

    // ── 基本構造 ────────────────────────────────────────

    [Fact]
    public void GenerateHtml_ReturnsValidHtml()
    {
        var config = CreateConfig();
        var html = IndexHandler.GenerateHtml(config, "mytoken", 8080, 45, "192.168.1.1");

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<title>LAN Drop</title>", html);
        Assert.Contains("</html>", html);
    }

    [Fact]
    public void GenerateHtml_ContainsToken_InScript()
    {
        var config = CreateConfig();
        var html = IndexHandler.GenerateHtml(config, "secretToken", 8080, 60, "192.168.1.1");

        Assert.Contains("const TOKEN = 'secretToken'", html);
    }

    [Fact]
    public void GenerateHtml_ContainsTtl()
    {
        var config = CreateConfig();
        var html = IndexHandler.GenerateHtml(config, "tok", 8080, 42, "192.168.1.1");

        Assert.Contains("TTL: 42m", html);
    }

    // ── ReadOnly モード ─────────────────────────────────

    [Fact]
    public void GenerateHtml_ReadOnly_ShowsBadge()
    {
        var config = CreateConfig(readOnly: true);
        var html = IndexHandler.GenerateHtml(config, "tok", 8080, 60, "192.168.1.1");

        Assert.Contains("READ ONLY", html);
    }

    [Fact]
    public void GenerateHtml_ReadOnly_NoUploadForm()
    {
        var config = CreateConfig(readOnly: true);
        var html = IndexHandler.GenerateHtml(config, "tok", 8080, 60, "192.168.1.1");

        // CSSにクラス名は残るが、<form> 要素自体が生成されないことを確認
        Assert.DoesNotContain("<form", html);
        Assert.DoesNotContain("id=\"file-input\"", html);
    }

    [Fact]
    public void GenerateHtml_NotReadOnly_HasUploadForm()
    {
        var config = CreateConfig(readOnly: false);
        var html = IndexHandler.GenerateHtml(config, "tok", 8080, 60, "192.168.1.1");

        Assert.Contains("<form", html);
        Assert.Contains("id=\"file-input\"", html);
    }

    [Fact]
    public void GenerateHtml_NotReadOnly_ShowsMaxUploadSize()
    {
        var config = CreateConfig(readOnly: false, maxUploadMb: 500);
        var html = IndexHandler.GenerateHtml(config, "tok", 8080, 60, "192.168.1.1");

        Assert.Contains("Max file size: 500 MB", html);
    }

    // ── READONLY JavaScript変数 ─────────────────────────

    [Fact]
    public void GenerateHtml_ReadOnly_SetsReadonlyTrue()
    {
        var config = CreateConfig(readOnly: true);
        var html = IndexHandler.GenerateHtml(config, "tok", 8080, 60, "192.168.1.1");

        Assert.Contains("const READONLY = true", html);
    }

    [Fact]
    public void GenerateHtml_NotReadOnly_SetsReadonlyFalse()
    {
        var config = CreateConfig(readOnly: false);
        var html = IndexHandler.GenerateHtml(config, "tok", 8080, 60, "192.168.1.1");

        Assert.Contains("const READONLY = false", html);
    }
}
