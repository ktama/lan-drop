using LanDrop.Utils;

namespace LanDrop.Tests;

/// <summary>
/// FileNameSanitizer のテスト
/// - 既存ファイルがある場合に name (1).ext となること
/// </summary>
public class FileNameSanitizerTests : IDisposable
{
    private readonly string _testDir;

    public FileNameSanitizerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "FileNameSanitizerTests_" + Guid.NewGuid().ToString("N"));
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
    public void Sanitize_RemovesInvalidCharacters()
    {
        // 不正な文字を削除（実際は置換ではなく削除）
        Assert.Equal("filename.txt", FileNameSanitizer.Sanitize("file<>name.txt"));
        Assert.Equal("filename.txt", FileNameSanitizer.Sanitize("file|name.txt"));
        Assert.Equal("filename.txt", FileNameSanitizer.Sanitize("file:name.txt"));
        Assert.Equal("filename.txt", FileNameSanitizer.Sanitize("file\"name.txt"));
        Assert.Equal("filename.txt", FileNameSanitizer.Sanitize("file?name.txt"));
        Assert.Equal("filename.txt", FileNameSanitizer.Sanitize("file*name.txt"));
    }

    [Fact]
    public void Sanitize_RemovesPathSeparators()
    {
        // パスセパレータを削除（実際はファイル名の最後の部分を取得するのではなく、文字を削除）
        var result = FileNameSanitizer.Sanitize("path/filename.txt");
        // 実際はパス区切りを不正文字として削除し、残りを結合
        Assert.Equal("pathfilename.txt", result);
    }

    [Fact]
    public void Sanitize_HandlesEmpty()
    {
        // 空文字列やnullはデフォルト名
        Assert.Equal("unnamed", FileNameSanitizer.Sanitize(""));
        Assert.Equal("unnamed", FileNameSanitizer.Sanitize("   "));
        Assert.Equal("unnamed", FileNameSanitizer.Sanitize(null!));
    }

    [Fact]
    public void Sanitize_TruncatesLongNames()
    {
        // 長すぎる名前は切り詰め
        var longName = new string('a', 300) + ".txt";
        var result = FileNameSanitizer.Sanitize(longName);
        Assert.True(result.Length <= 255);
        Assert.EndsWith(".txt", result);
    }

    [Fact]
    public void GetUniqueFileName_NoConflict_ReturnsSameName()
    {
        // 競合なしの場合はそのまま
        var result = FileNameSanitizer.GetUniqueFileName(_testDir, "newfile.txt");
        Assert.Equal("newfile.txt", result);
    }

    [Fact]
    public void GetUniqueFileName_OneConflict_ReturnsNumbered()
    {
        // 既存ファイルがある場合は (1) を付加
        File.WriteAllText(Path.Combine(_testDir, "test.txt"), "existing");
        
        var result = FileNameSanitizer.GetUniqueFileName(_testDir, "test.txt");
        Assert.Equal("test (1).txt", result);
    }

    [Fact]
    public void GetUniqueFileName_MultipleConflicts_IncrementsNumber()
    {
        // 複数の競合がある場合は番号を増加
        File.WriteAllText(Path.Combine(_testDir, "test.txt"), "existing");
        File.WriteAllText(Path.Combine(_testDir, "test (1).txt"), "existing");
        File.WriteAllText(Path.Combine(_testDir, "test (2).txt"), "existing");
        
        var result = FileNameSanitizer.GetUniqueFileName(_testDir, "test.txt");
        Assert.Equal("test (3).txt", result);
    }

    [Fact]
    public void GetUniqueFileName_NoExtension_Works()
    {
        // 拡張子なしのファイル
        File.WriteAllText(Path.Combine(_testDir, "README"), "existing");
        
        var result = FileNameSanitizer.GetUniqueFileName(_testDir, "README");
        Assert.Equal("README (1)", result);
    }

    [Fact]
    public void GetUniqueFileName_HiddenFile_Works()
    {
        // ドットで始まるファイル（Path.GetFileNameWithoutExtensionは空文字列を返す）
        File.WriteAllText(Path.Combine(_testDir, ".gitignore"), "existing");
        
        var result = FileNameSanitizer.GetUniqueFileName(_testDir, ".gitignore");
        // 実際は baseName="" となり、" (1).gitignore" となる
        Assert.Equal(" (1).gitignore", result);
    }

    [Fact]
    public void GetUniqueFileName_MultipleExtensions_Works()
    {
        // 複数拡張子
        File.WriteAllText(Path.Combine(_testDir, "archive.tar.gz"), "existing");
        
        var result = FileNameSanitizer.GetUniqueFileName(_testDir, "archive.tar.gz");
        // Path.GetExtension は ".gz" を返す
        Assert.Equal("archive.tar (1).gz", result);
    }

    [Fact]
    public void GetUniqueFileName_UnicodeFileName_Works()
    {
        // Unicode文字を含むファイル名
        File.WriteAllText(Path.Combine(_testDir, "日本語ファイル.txt"), "existing");
        
        var result = FileNameSanitizer.GetUniqueFileName(_testDir, "日本語ファイル.txt");
        Assert.Equal("日本語ファイル (1).txt", result);
    }

    [Fact]
    public void GetUniqueFileName_DirectoryDoesNotExist_ReturnsSameName()
    {
        // ディレクトリが存在しない場合はそのまま（File.Existsがfalseを返すため）
        var nonExistentDir = Path.Combine(_testDir, "nonexistent");
        var result = FileNameSanitizer.GetUniqueFileName(nonExistentDir, "test.txt");
        Assert.Equal("test.txt", result);
    }

    [Fact]
    public void GetUniqueFileName_MaxIterations_DoesNotHang()
    {
        // 大量の競合がある場合でもハングしない
        for (int i = 0; i < 100; i++)
        {
            var name = i == 0 ? "stress.txt" : $"stress ({i}).txt";
            File.WriteAllText(Path.Combine(_testDir, name), "existing");
        }
        
        var result = FileNameSanitizer.GetUniqueFileName(_testDir, "stress.txt");
        Assert.Equal("stress (100).txt", result);
    }

    [Fact]
    public void Sanitize_ReservedNames_AddPrefix()
    {
        // Windows予約名にはプレフィックスが付く
        Assert.Equal("_CON", FileNameSanitizer.Sanitize("CON"));
        Assert.Equal("_NUL", FileNameSanitizer.Sanitize("NUL"));
        Assert.Equal("_COM1.txt", FileNameSanitizer.Sanitize("COM1.txt"));
    }
}
