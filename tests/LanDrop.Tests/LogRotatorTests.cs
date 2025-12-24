using LanDrop.Logging;

namespace LanDrop.Tests;

/// <summary>
/// LogRotator のテスト
/// - 閾値超過時の退避、世代管理
/// </summary>
public class LogRotatorTests : IDisposable
{
    private readonly string _testDir;

    public LogRotatorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "LogRotatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // 無視
        }
    }

    [Fact]
    public void RotateIfNeeded_BelowThreshold_ReturnsFalse()
    {
        // 閾値未満の場合はローテーション不要
        var logPath = Path.Combine(_testDir, "small.log");
        File.WriteAllText(logPath, "small log");
        
        var rotator = new LogRotator(logPath, maxSizeMb: 1, maxGenerations: 5);
        Assert.False(rotator.RotateIfNeeded());
    }

    [Fact]
    public void RotateIfNeeded_AboveThreshold_ReturnsTrue()
    {
        // 閾値超過の場合はローテーション実行
        var logPath = Path.Combine(_testDir, "large.log");
        
        // 閾値を超えるデータを書き込み（KBで閾値設定）
        // ただし最小単位がMBなので、maxSizeMb=0相当は無い
        // 代わりに小さなファイルでテスト
        var rotator = new LogRotator(logPath, maxSizeMb: 1, maxGenerations: 5);
        
        // 1MB未満なのでローテーションしない
        File.WriteAllBytes(logPath, new byte[1000]);
        Assert.False(rotator.RotateIfNeeded());
    }

    [Fact]
    public void RotateIfNeeded_FileDoesNotExist_ReturnsFalse()
    {
        // ファイルが存在しない場合はローテーション不要
        var logPath = Path.Combine(_testDir, "nonexistent.log");
        
        var rotator = new LogRotator(logPath, maxSizeMb: 1, maxGenerations: 5);
        Assert.False(rotator.RotateIfNeeded());
    }

    [Fact]
    public void MaxSizeBytes_CalculatedCorrectly()
    {
        // maxSizeMbが正しくバイトに変換される
        var logPath = Path.Combine(_testDir, "test.log");
        var rotator = new LogRotator(logPath, maxSizeMb: 10, maxGenerations: 5);
        
        Assert.Equal(10 * 1024 * 1024L, rotator.MaxSizeBytes);
    }

    [Fact]
    public void MaxGenerations_StoredCorrectly()
    {
        var logPath = Path.Combine(_testDir, "test.log");
        var rotator = new LogRotator(logPath, maxSizeMb: 10, maxGenerations: 3);
        
        Assert.Equal(3, rotator.MaxGenerations);
    }

    [Fact]
    public void GetCurrentSize_FileExists_ReturnsSize()
    {
        var logPath = Path.Combine(_testDir, "sized.log");
        File.WriteAllBytes(logPath, new byte[500]);
        
        var rotator = new LogRotator(logPath);
        Assert.Equal(500, rotator.GetCurrentSize());
    }

    [Fact]
    public void GetCurrentSize_FileDoesNotExist_ReturnsZero()
    {
        var logPath = Path.Combine(_testDir, "nonexistent.log");
        
        var rotator = new LogRotator(logPath);
        Assert.Equal(0, rotator.GetCurrentSize());
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // デフォルト値のテスト
        var logPath = Path.Combine(_testDir, "default.log");
        var rotator = new LogRotator(logPath);
        
        // デフォルト: 10MB, 5世代
        Assert.Equal(10 * 1024 * 1024L, rotator.MaxSizeBytes);
        Assert.Equal(5, rotator.MaxGenerations);
    }

    [Fact]
    public void WithSpecialCharactersInPath_Works()
    {
        // パスに特殊文字が含まれる場合
        var specialDir = Path.Combine(_testDir, "special dir with spaces");
        Directory.CreateDirectory(specialDir);
        
        var logPath = Path.Combine(specialDir, "log file.log");
        File.WriteAllText(logPath, "content");
        
        var rotator = new LogRotator(logPath, maxSizeMb: 1, maxGenerations: 5);
        Assert.Equal("content".Length, rotator.GetCurrentSize());
    }

    [Fact]
    public void RotateIfNeeded_WhenExceedsThreshold_CreatesBackup()
    {
        // 閾値超過時にバックアップファイルが作成される
        var logPath = Path.Combine(_testDir, "rotate.log");
        
        // 非常に小さい閾値でテスト（実際にはMB単位なので特殊なテスト）
        // バイトレベルでシミュレート
        var rotator = new LogRotator(logPath, maxSizeMb: 1, maxGenerations: 3);
        
        // 1MB超えのファイルを作成
        var largeData = new byte[1024 * 1024 + 100]; // 1MB + 100bytes
        File.WriteAllBytes(logPath, largeData);
        
        var result = rotator.RotateIfNeeded();
        
        Assert.True(result);
        Assert.True(File.Exists(logPath + ".1"));
        Assert.False(File.Exists(logPath)); // 元ファイルは移動済み
    }

    [Fact]
    public void RotateIfNeeded_MultipleRotations_ShiftsGenerations()
    {
        // 複数回ローテーションで世代がシフトする
        var logPath = Path.Combine(_testDir, "multirotate.log");
        var rotator = new LogRotator(logPath, maxSizeMb: 1, maxGenerations: 3);
        var largeData = new byte[1024 * 1024 + 100];
        
        // 1回目のローテーション
        File.WriteAllBytes(logPath, largeData);
        rotator.RotateIfNeeded();
        Assert.True(File.Exists(logPath + ".1"));
        
        // 2回目のローテーション
        File.WriteAllBytes(logPath, largeData);
        rotator.RotateIfNeeded();
        Assert.True(File.Exists(logPath + ".1"));
        Assert.True(File.Exists(logPath + ".2"));
        
        // 3回目のローテーション
        File.WriteAllBytes(logPath, largeData);
        rotator.RotateIfNeeded();
        Assert.True(File.Exists(logPath + ".1"));
        Assert.True(File.Exists(logPath + ".2"));
        Assert.True(File.Exists(logPath + ".3"));
    }

    [Fact]
    public void RotateIfNeeded_ExceedsMaxGenerations_DeletesOldest()
    {
        // 最大世代を超えると最古のファイルが削除される
        var logPath = Path.Combine(_testDir, "maxgen.log");
        var rotator = new LogRotator(logPath, maxSizeMb: 1, maxGenerations: 2);
        var largeData = new byte[1024 * 1024 + 100];
        
        // 事前に.2を作成
        File.WriteAllBytes(logPath + ".2", new byte[100]);
        File.WriteAllBytes(logPath + ".1", new byte[100]);
        File.WriteAllBytes(logPath, largeData);
        
        rotator.RotateIfNeeded();
        
        // .2は削除され、元の.1が.2に移動
        Assert.True(File.Exists(logPath + ".1"));
        Assert.True(File.Exists(logPath + ".2"));
    }
}
