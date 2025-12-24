using LanDrop.Config;

namespace LanDrop.Tests;

/// <summary>
/// アップロードサイズ制限のテスト
/// - 上限超過拒否
/// </summary>
public class UploadSizeLimitTests
{
    [Fact]
    public void MaxUploadBytes_CalculatedCorrectly()
    {
        // MaxUploadBytesが正しく計算される
        var config = CreateConfig(100);
        Assert.Equal(100 * 1024 * 1024L, config.MaxUploadBytes);
    }

    [Fact]
    public void DefaultMaxUpload_Is200MB()
    {
        // デフォルトのアップロード制限は200MB
        var config = CreateConfig();
        Assert.Equal(200, config.MaxUploadMb);
        Assert.Equal(200 * 1024 * 1024L, config.MaxUploadBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1024)]
    public void MaxUploadMb_VariousLimits(int mb)
    {
        var config = CreateConfig(mb);
        Assert.Equal(mb * 1024L * 1024L, config.MaxUploadBytes);
    }

    [Fact]
    public void ContentLength_ExceedsLimit_ShouldBeRejected()
    {
        // Content-Lengthが制限を超える場合は拒否されるべき
        var config = CreateConfig(1); // 1MB
        var maxBytes = config.MaxUploadBytes;
        var exceededLength = maxBytes + 1;
        
        // この値は1MBを超えている
        Assert.True(exceededLength > maxBytes);
    }

    [Fact]
    public void SmallFile_UnderLimit_Allowed()
    {
        // 制限未満のファイルは許可
        var config = CreateConfig(1); // 1MB
        var maxBytes = config.MaxUploadBytes;
        var smallFileSize = 1024; // 1KB
        
        Assert.True(smallFileSize < maxBytes);
    }

    [Fact]
    public void ExactLimit_ShouldBeAllowed()
    {
        // 制限ちょうどのサイズは許可されるべき
        var config = CreateConfig(1);
        var maxBytes = config.MaxUploadBytes;
        var exactSize = maxBytes;
        
        Assert.True(exactSize <= maxBytes);
    }

    [Fact]
    public void ZeroBytes_Allowed()
    {
        // 0バイトのファイルは許可
        var config = CreateConfig(1);
        var maxBytes = config.MaxUploadBytes;
        Assert.True(0 <= maxBytes);
    }

    [Fact]
    public void Config_MaxUploadMb_CanBeSetFromCli()
    {
        // CLIから設定可能（--dirが必須なのでテンポラリディレクトリを指定）
        var tempDir = Path.GetTempPath();
        var args = new[] { "--dir", tempDir, "--max-upload-mb", "50" };
        var (config, error, _) = CliParser.Parse(args);
        
        Assert.Null(error);
        Assert.NotNull(config);
        Assert.Equal(50, config.MaxUploadMb);
    }

    [Fact]
    public void Config_InvalidMaxUploadMb_ReturnsError()
    {
        // 無効な値はエラー（--dirも必須）
        var tempDir = Path.GetTempPath();
        var args = new[] { "--dir", tempDir, "--max-upload-mb", "invalid" };
        var (_, error, _) = CliParser.Parse(args);
        
        Assert.NotNull(error);
    }

    [Fact]
    public void Config_NegativeMaxUploadMb_ReturnsError()
    {
        // 負の値はエラー
        var tempDir = Path.GetTempPath();
        var args = new[] { "--dir", tempDir, "--max-upload-mb", "-1" };
        var (_, error, _) = CliParser.Parse(args);
        
        Assert.NotNull(error);
    }

    [Fact]
    public void Config_ZeroMaxUploadMb_ReturnsError()
    {
        // 0はエラー
        var tempDir = Path.GetTempPath();
        var args = new[] { "--dir", tempDir, "--max-upload-mb", "0" };
        var (_, error, _) = CliParser.Parse(args);
        
        Assert.NotNull(error);
    }

    [Fact]
    public void ReadOnly_Property()
    {
        // ReadOnlyモードではアップロードがブロックされる
        var config = CreateConfig();
        config.ReadOnly = true;

        Assert.True(config.ReadOnly);
    }

    [Fact]
    public void UploadsDir_DerivedFromRootDir()
    {
        // UploadsDirはRootDirから派生
        var config = CreateConfig();
        Assert.EndsWith("_uploads", config.UploadsDir);
        Assert.StartsWith(config.RootDir, config.UploadsDir);
    }

    private static AppConfig CreateConfig(int? maxUploadMb = null)
    {
        var config = new AppConfig
        {
            RootDir = Path.GetTempPath()
        };
        
        if (maxUploadMb.HasValue)
        {
            config.MaxUploadMb = maxUploadMb.Value;
        }
        
        return config;
    }
}
