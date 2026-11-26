using LanDrop.Config;
using LanDrop.Handlers;

namespace LanDrop.Tests;

/// <summary>
/// トップページの安全性・機能のテスト
/// - ファイル名をHTML/JS文字列に連結しない、CSP nonce、QRコード、アップロードUI
/// </summary>
public class IndexHandlerSecurityTests
{
    private static AppConfig Config(bool readOnly = false, string? logPath = null) => new()
    {
        RootDir = Path.Combine(Path.GetTempPath(), "lan-drop-test"),
        ReadOnly = readOnly,
        LogPath = logPath
    };

    [Fact]
    public void Html_DoesNotBuildInlineEventHandlers()
    {
        // 再現: onclick="loadDir('…', '名前')" の連結で ' を含むフォルダ名が壊れ、スクリプトも注入できた
        var html = IndexHandler.GenerateHtml(Config(), "tok", 8000, 60, "192.168.1.10");

        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("innerHTML", html);
        Assert.Contains("textContent", html);
        Assert.Contains("addEventListener", html);
    }

    [Fact]
    public void Nonce_IsAppliedToScriptAndStyle()
    {
        var nonce = IndexHandler.CreateNonce();
        var html = IndexHandler.GenerateHtml(Config(), "tok", 8000, 60, "192.168.1.10", nonce);

        Assert.Contains($"<script nonce=\"{nonce}\">", html);
        Assert.Contains($"<style nonce=\"{nonce}\">", html);
        // インラインの style 属性は CSP で拒否されるので使わない
        Assert.DoesNotContain(" style=\"", html);
    }

    [Fact]
    public void Csp_AllowsOnlyNoncedScripts()
    {
        var csp = IndexHandler.ContentSecurityPolicy("abc");

        Assert.Contains("script-src 'nonce-abc'", csp);
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
    }

    [Fact]
    public void Nonce_IsRandom()
    {
        Assert.NotEqual(IndexHandler.CreateNonce(), IndexHandler.CreateNonce());
    }

    [Fact]
    public void LogPath_IsHtmlEncoded()
    {
        var html = IndexHandler.GenerateHtml(Config(logPath: "C:\\a<b>&c\\x.log"), "tok", 8000, 60, "192.168.1.10");

        Assert.Contains("C:\\a&lt;b&gt;&amp;c\\x.log", html);
        Assert.DoesNotContain("a<b>", html);
    }

    [Fact]
    public void Footer_ContainsQrCodeOfShareUrl()
    {
        var html = IndexHandler.GenerateHtml(Config(), "tok", 8000, 60, "192.168.1.10");

        Assert.Contains("<svg", html);
        Assert.Contains("aria-label=\"QR code\"", html);
        Assert.Contains("http://192.168.1.10:8000/tok/", html);
    }

    [Fact]
    public void Upload_HasDropZoneAndProgress()
    {
        var html = IndexHandler.GenerateHtml(Config(), "tok", 8000, 60, "192.168.1.10");

        Assert.Contains("id=\"drop-zone\"", html);
        Assert.Contains("<progress", html);
        Assert.Contains("xhr.upload.addEventListener('progress'", html);
    }

    [Fact]
    public void ReadOnly_HasNoDropZone()
    {
        var html = IndexHandler.GenerateHtml(Config(readOnly: true), "tok", 8000, 60, "192.168.1.10");

        Assert.DoesNotContain("id=\"drop-zone\"", html);
    }
}
