using LanDrop.Security;

namespace LanDrop.Tests;

/// <summary>
/// TokenValidator のテスト
/// - トークン生成、パス検証
/// </summary>
public class TokenValidatorTests
{
    [Fact]
    public void GenerateToken_Returns24Characters()
    {
        // 生成されるトークンは24文字
        var token = TokenValidator.GenerateToken();
        Assert.Equal(24, token.Length);
    }

    [Fact]
    public void GenerateToken_UrlSafe()
    {
        // URL安全な文字のみ含む
        var token = TokenValidator.GenerateToken();
        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.DoesNotContain("=", token);
    }

    [Fact]
    public void GenerateToken_Unique()
    {
        // 毎回異なるトークンが生成される
        var tokens = Enumerable.Range(0, 100)
            .Select(_ => TokenValidator.GenerateToken())
            .ToHashSet();
        
        Assert.Equal(100, tokens.Count);
    }

    [Fact]
    public void Constructor_WithToken_UsesProvidedToken()
    {
        // 指定したトークンを使用
        var validator = new TokenValidator("my-custom-token");
        Assert.Equal("my-custom-token", validator.Token);
    }

    [Fact]
    public void Constructor_WithNull_GeneratesToken()
    {
        // nullの場合は自動生成
        var validator = new TokenValidator(null);
        Assert.NotNull(validator.Token);
        Assert.Equal(24, validator.Token.Length);
    }

    [Fact]
    public void ValidatePath_ValidToken_ReturnsTrue()
    {
        var validator = new TokenValidator("abc123");
        
        Assert.True(validator.ValidatePath("/abc123/", out var remaining));
        Assert.Equal("/", remaining);
    }

    [Fact]
    public void ValidatePath_ValidToken_WithSubpath()
    {
        var validator = new TokenValidator("abc123");
        
        Assert.True(validator.ValidatePath("/abc123/browse", out var remaining));
        Assert.Equal("/browse", remaining);
    }

    [Fact]
    public void ValidatePath_ValidToken_WithDeepPath()
    {
        var validator = new TokenValidator("abc123");
        
        Assert.True(validator.ValidatePath("/abc123/dl?path=file.txt", out var remaining));
        Assert.Equal("/dl?path=file.txt", remaining);
    }

    [Fact]
    public void ValidatePath_InvalidToken_ReturnsFalse()
    {
        var validator = new TokenValidator("abc123");
        
        Assert.False(validator.ValidatePath("/wrongtoken/", out _));
        Assert.False(validator.ValidatePath("/xyz789/browse", out _));
    }

    [Fact]
    public void ValidatePath_NoLeadingSlash_ReturnsFalse()
    {
        var validator = new TokenValidator("abc123");
        
        Assert.False(validator.ValidatePath("abc123/", out _));
    }

    [Fact]
    public void ValidatePath_EmptyPath_ReturnsFalse()
    {
        var validator = new TokenValidator("abc123");
        
        Assert.False(validator.ValidatePath("", out _));
        Assert.False(validator.ValidatePath(null!, out _));
    }

    [Fact]
    public void ValidatePath_TokenOnly_NoTrailingSlash()
    {
        var validator = new TokenValidator("abc123");
        
        Assert.True(validator.ValidatePath("/abc123", out var remaining));
        Assert.Equal("/", remaining);
    }

    [Fact]
    public void ValidatePath_CaseSensitive()
    {
        // トークンは大文字小文字を区別
        var validator = new TokenValidator("AbC123");
        
        Assert.True(validator.ValidatePath("/AbC123/", out _));
        Assert.False(validator.ValidatePath("/abc123/", out _));
        Assert.False(validator.ValidatePath("/ABC123/", out _));
    }

    [Fact]
    public void ValidatePath_SpecialCharactersInToken()
    {
        // URL安全な特殊文字を含むトークン
        var validator = new TokenValidator("abc-123_xyz");
        
        Assert.True(validator.ValidatePath("/abc-123_xyz/", out _));
    }
}
