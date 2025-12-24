using LanDrop.Security;

namespace LanDrop.Tests;

/// <summary>
/// PathValidator のテスト
/// - ../や絶対パス、UNC、ドライブ指定が拒否されること
/// </summary>
public class PathValidatorTests : IDisposable
{
    private readonly string _testRoot;
    private readonly PathValidator _validator;

    public PathValidatorTests()
    {
        // テスト用の一時ディレクトリ
        _testRoot = Path.Combine(Path.GetTempPath(), "PathValidatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        
        // サブディレクトリとファイルを作成
        Directory.CreateDirectory(Path.Combine(_testRoot, "subdir"));
        File.WriteAllText(Path.Combine(_testRoot, "test.txt"), "test content");
        File.WriteAllText(Path.Combine(_testRoot, "subdir", "nested.txt"), "nested content");
        
        _validator = new PathValidator(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
            // 無視
        }
    }

    [Fact]
    public void ValidPath_ReturnsTrue()
    {
        // 正常なパス
        Assert.True(_validator.TryValidate("test.txt", out _, out _));
        Assert.True(_validator.TryValidate("subdir", out _, out _));
        Assert.True(_validator.TryValidate("subdir/nested.txt", out _, out _));
    }

    [Fact]
    public void EmptyPath_RootAllowed()
    {
        // 空パス（ルート）は許可
        Assert.True(_validator.TryValidate("", out var fullPath, out _));
        Assert.Equal(_testRoot, fullPath);
    }

    [Theory]
    [InlineData("../")]
    [InlineData("..\\")]
    [InlineData("../secret.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("subdir/../../../etc/passwd")]
    [InlineData("subdir\\..\\..\\..\\Windows\\System32")]
    public void PathTraversal_ReturnsFalse(string maliciousPath)
    {
        // ディレクトリトラバーサルは拒否
        Assert.False(_validator.TryValidate(maliciousPath, out _, out var errorCode));
        Assert.Equal("PATH_TRAVERSAL", errorCode);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/var/log")]
    [InlineData("\\Windows\\System32")]
    public void AbsolutePath_ReturnsFalse(string absolutePath)
    {
        // 絶対パスは拒否（ABSOLUTE_PATH エラー）
        Assert.False(_validator.TryValidate(absolutePath, out _, out var errorCode));
        Assert.Equal("ABSOLUTE_PATH", errorCode);
    }

    [Theory]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("c:\\Windows\\System32")]
    [InlineData("D:\\secret.txt")]
    [InlineData("Z:\\data")]
    public void DriveLetterPath_ReturnsFalse(string drivePath)
    {
        // ドライブレター付きパスは拒否（Path.IsPathRootedが先にABSOLUTE_PATHを返す）
        Assert.False(_validator.TryValidate(drivePath, out _, out var errorCode));
        // 実際は Path.IsPathRooted が先に評価されるため ABSOLUTE_PATH
        Assert.Equal("ABSOLUTE_PATH", errorCode);
    }

    [Theory]
    [InlineData("\\\\server\\share")]
    [InlineData("\\\\192.168.1.1\\c$")]
    [InlineData("\\\\server\\share\\secret.txt")]
    public void UncPath_ReturnsFalse(string uncPath)
    {
        // UNCパスは拒否（Path.IsPathRootedが先にABSOLUTE_PATHを返す）
        Assert.False(_validator.TryValidate(uncPath, out _, out var errorCode));
        // 実際は Path.IsPathRooted が先に評価されるため ABSOLUTE_PATH
        Assert.Equal("ABSOLUTE_PATH", errorCode);
    }

    [Fact]
    public void NullPath_ReturnsRoot()
    {
        // nullはルートとして扱われる
        Assert.True(_validator.TryValidate(null, out var fullPath, out _));
        Assert.Equal(_testRoot, fullPath);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT1")]
    public void WindowsReservedNames_ReturnsFalse(string reservedName)
    {
        // Windows予約名は拒否
        Assert.False(_validator.TryValidate(reservedName, out _, out var errorCode));
        Assert.Equal("RESERVED_NAME", errorCode);
    }

    [Fact]
    public void ValidPath_ReturnsCorrectFullPath()
    {
        // 正常なパスでfullPathが正しく返される
        Assert.True(_validator.TryValidate("test.txt", out var fullPath, out _));
        Assert.NotNull(fullPath);
        Assert.Equal(Path.Combine(_testRoot, "test.txt"), fullPath);
    }

    [Fact]
    public void NestedPath_ReturnsCorrectFullPath()
    {
        // ネストしたパスでもfullPathが正しく返される
        Assert.True(_validator.TryValidate("subdir/nested.txt", out var fullPath, out _));
        Assert.NotNull(fullPath);
        
        var expected = Path.Combine(_testRoot, "subdir", "nested.txt");
        Assert.Equal(expected, fullPath);
    }

    [Fact]
    public void DangerousPathsAreRejected()
    {
        // 危険なパスが確実に拒否されることを確認
        var dangerousPaths = new[]
        {
            "..",
            "../..",
            "foo/../..",
            "..\\secret",
            "/etc/passwd",
            "C:\\Windows",
            "\\\\server\\share"
        };

        foreach (var path in dangerousPaths)
        {
            Assert.False(_validator.TryValidate(path, out _, out _), $"Path should be rejected: {path}");
        }
    }
}
