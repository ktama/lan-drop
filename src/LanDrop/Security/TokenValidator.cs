using System.Security.Cryptography;

namespace LanDrop.Security;

/// <summary>
/// Capability URLトークンの生成と検証
/// </summary>
public sealed class TokenValidator
{
    private readonly string _token;
    
    public string Token => _token;

    public TokenValidator(string? providedToken = null)
    {
        _token = providedToken ?? GenerateToken();
    }

    /// <summary>
    /// 24文字のURL-safe Base64トークンを生成
    /// </summary>
    public static string GenerateToken()
    {
        var bytes = new byte[18]; // 18 bytes = 24 Base64 chars
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    /// <summary>
    /// URLパスからトークンを抽出して検証
    /// </summary>
    /// <param name="path">URLパス（例: /abc123/dl）</param>
    /// <param name="remainingPath">トークン以降のパス（例: /dl）</param>
    /// <returns>トークンが一致すればtrue</returns>
    public bool ValidatePath(string path, out string remainingPath)
    {
        remainingPath = string.Empty;

        if (string.IsNullOrEmpty(path) || !path.StartsWith('/'))
            return false;

        // パスから最初のセグメントを抽出
        var withoutLeadingSlash = path.Substring(1);
        var slashIndex = withoutLeadingSlash.IndexOf('/');
        
        string tokenFromPath;
        if (slashIndex == -1)
        {
            tokenFromPath = withoutLeadingSlash;
            remainingPath = "/";
        }
        else
        {
            tokenFromPath = withoutLeadingSlash.Substring(0, slashIndex);
            remainingPath = withoutLeadingSlash.Substring(slashIndex);
        }

        // 定数時間比較（タイミング攻撃対策）
        return ConstantTimeEquals(tokenFromPath, _token);
    }

    /// <summary>
    /// 定数時間での文字列比較（タイミング攻撃対策）
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        // 長さ不一致でも同じ時間かけて比較（タイミング攻撃対策）
        int lengthDiff = a.Length ^ b.Length;
        int diff = lengthDiff;
        int minLength = Math.Min(a.Length, b.Length);
        
        for (int i = 0; i < minLength; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }
}
