using System.Diagnostics;
using LanDrop.Security;

namespace LanDrop.Tests;

/// <summary>
/// リンク（ジャンクション・シンボリックリンク）経由で共有ルートの外に出られないことのテスト
/// </summary>
public sealed class LinkEscapeTests : IDisposable
{
    private readonly string _base;
    private readonly string _root;
    private readonly string _outside;

    public LinkEscapeTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "lan-drop-link-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_base, "root");
        _outside = Path.Combine(_base, "outside");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "inner"));
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "SECRET");
        File.WriteAllText(Path.Combine(_root, "inner", "ok.txt"), "OK");
    }

    public void Dispose()
    {
        try
        {
            // リンクを先に消す（リンク先を再帰削除しないため）
            foreach (var dir in Directory.GetDirectories(_root))
            {
                if (new DirectoryInfo(dir).LinkTarget != null)
                    Directory.Delete(dir);
            }
            Directory.Delete(_base, recursive: true);
        }
        catch
        {
            // 後始末の失敗は無視
        }
    }

    /// <summary>
    /// ディレクトリへのリンクを作る（Windowsは権限不要のジャンクション、それ以外はシンボリックリンク）
    /// </summary>
    private static void CreateDirectoryLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            Assert.Equal(0, p.ExitCode);
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }
    }

    [Fact]
    public void FileThroughIntermediateLinkToOutside_IsRejected()
    {
        // 再現: share/link -> outside のとき link/secret.txt が取得できていた
        CreateDirectoryLink(Path.Combine(_root, "link"), _outside);
        var validator = new PathValidator(_root);

        Assert.False(validator.TryValidate("link/secret.txt", out _, out var code));
        Assert.Equal("SYMLINK_OUTSIDE", code);
    }

    [Fact]
    public void LinkToOutside_ItselfIsRejected()
    {
        CreateDirectoryLink(Path.Combine(_root, "link"), _outside);
        var validator = new PathValidator(_root);

        Assert.False(validator.TryValidate("link", out _, out _));
    }

    [Fact]
    public void NewPathUnderLinkToOutside_IsRejected()
    {
        // アップロード先の新規作成でもリンクを経由して外に書けないこと
        CreateDirectoryLink(Path.Combine(_root, "link"), _outside);
        var validator = new PathValidator(_root);

        Assert.False(validator.TryValidate("link/newdir/file.txt", out _, out _));
    }

    [Fact]
    public void LinkInsideRoot_IsAllowed()
    {
        CreateDirectoryLink(Path.Combine(_root, "alias"), Path.Combine(_root, "inner"));
        var validator = new PathValidator(_root);

        Assert.True(validator.TryValidate("alias/ok.txt", out var full, out _));
        Assert.Equal("OK", File.ReadAllText(full));
    }

    [Fact]
    public void RootItselfIsLink_FilesUnderItAreAllowed()
    {
        var rootLink = Path.Combine(_base, "rootlink");
        CreateDirectoryLink(rootLink, _root);
        try
        {
            var validator = new PathValidator(rootLink);
            Assert.True(validator.TryValidate("inner/ok.txt", out _, out _));
        }
        finally
        {
            Directory.Delete(rootLink);
        }
    }

    [Fact]
    public void NormalNestedPath_IsAllowed()
    {
        var validator = new PathValidator(_root);

        Assert.True(validator.TryValidate("inner/ok.txt", out _, out _));
        Assert.True(validator.TryValidate("inner/not-yet-created.txt", out _, out _));
    }

    // ── ログファイルの非公開 ─────────────────────────────

    [Theory]
    [InlineData("lan-drop.log")]
    [InlineData("lan-drop.log.1")]
    [InlineData("lan-drop.log.5")]
    public void LogFilesInsideRoot_AreRejected(string name)
    {
        var validator = new PathValidator(_root, Path.Combine(_root, "lan-drop.log"));

        Assert.False(validator.TryValidate(name, out _, out var code));
        Assert.Equal("FORBIDDEN_PATH", code);
    }

    [Theory]
    [InlineData("lan-drop.log.txt")]
    [InlineData("lan-drop.logs")]
    [InlineData("other.log")]
    public void SimilarNames_AreAllowed(string name)
    {
        var validator = new PathValidator(_root, Path.Combine(_root, "lan-drop.log"));

        Assert.True(validator.TryValidate(name, out _, out _));
    }
}
