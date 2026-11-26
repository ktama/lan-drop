using System.Net;
using LanDrop.Config;
using LanDrop.Security;

namespace LanDrop.Tests;

/// <summary>
/// CIDR の IPv6 対応、トークン・引数の検証、バージョン取得のテスト
/// </summary>
public class HardeningTests
{
    private static readonly string Dir = Path.GetTempPath();

    // ── CidrRange IPv6 ───────────────────────────────────

    [Theory]
    [InlineData("fe80::/10", "fe80::1234:5678", true)]
    [InlineData("fe80::/10", "fec0::1", false)]
    [InlineData("2001:db8::/32", "2001:db8:ffff::1", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    [InlineData("2001:db8::1/128", "2001:db8::1", true)]
    [InlineData("::/0", "2001:db8::1", true)]
    public void Cidr_IPv6(string cidr, string ip, bool expected)
    {
        Assert.Equal(expected, new CidrRange(cidr).Contains(IPAddress.Parse(ip)));
    }

    [Fact]
    public void Cidr_FamilyMismatch_IsNotContained()
    {
        Assert.False(new CidrRange("192.168.1.0/24").Contains(IPAddress.Parse("fe80::1")));
        Assert.False(new CidrRange("::/0").Contains(IPAddress.Parse("192.168.1.1")));
    }

    [Fact]
    public void Cidr_IPv4Mapped_MatchesIPv4Range()
    {
        Assert.True(new CidrRange("192.168.1.0/24").Contains(IPAddress.Parse("::ffff:192.168.1.5")));
        Assert.True(new CidrRange("::ffff:192.168.1.0/24").Contains(IPAddress.Parse("192.168.1.5")));
    }

    [Theory]
    [InlineData("fe80::/129")]
    [InlineData("192.168.1.0/33")]
    public void Cidr_PrefixTooLong_Throws(string cidr)
    {
        Assert.Throws<ArgumentException>(() => new CidrRange(cidr));
    }

    [Fact]
    public void Cidr_IPv6_ToString_IsNormalized()
    {
        Assert.Equal("2001:db8::/32", new CidrRange("2001:db8:1:2::5/32").ToString());
    }

    [Fact]
    public void IpFilter_MixedFamilies()
    {
        var filter = new IpFilter("192.168.1.0/24,fe80::/10");

        Assert.True(filter.IsAllowed(IPAddress.Parse("192.168.1.10")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("fe80::abcd")));
        Assert.False(filter.IsAllowed(IPAddress.Parse("2001:db8::1")));
    }

    // ── トークン ─────────────────────────────────────────

    [Theory]
    [InlineData("abcDEF123_-xyz")]
    [InlineData("auto")]
    public void Cli_Token_Valid(string token)
    {
        var (config, error, _) = CliParser.Parse(["--dir", Dir, "--token", token]);

        Assert.Null(error);
        Assert.NotNull(config);
    }

    [Theory]
    [InlineData("abc'def")]
    [InlineData("a/b")]
    [InlineData("<script>")]
    [InlineData("トークン")]
    public void Cli_Token_InvalidChars_ReturnsError(string token)
    {
        var (config, error, _) = CliParser.Parse(["--dir", Dir, "--token", token]);

        Assert.Null(config);
        Assert.Contains("--token", error);
    }

    [Theory]
    [InlineData("abcdefghijkl", "abcd********")]
    [InlineData("abcd", "****")]
    [InlineData("ab", "**")]
    public void Token_Mask(string token, string expected)
    {
        Assert.Equal(expected, TokenValidator.Mask(token));
    }

    [Fact]
    public void GeneratedToken_IsValidAndLongEnough()
    {
        var token = TokenValidator.GenerateToken();

        Assert.True(TokenValidator.IsValidFormat(token));
        Assert.True(token.Length >= TokenValidator.RecommendedMinLength);
    }

    // ── 位置引数 ─────────────────────────────────────────

    [Fact]
    public void Cli_UnexpectedPositionalArgument_ReturnsError()
    {
        // --dir C:\My Files の引用符忘れで "Files" が残るケース
        var (config, error, _) = CliParser.Parse(["--dir", Dir, "Files"]);

        Assert.Null(config);
        Assert.Contains("Unexpected argument: Files", error);
    }

    // ── バージョン ───────────────────────────────────────

    [Fact]
    public void Version_ComesFromAssembly_WithoutBuildMetadata()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", AppInfo.Version);
        Assert.DoesNotContain("+", AppInfo.Version);
        Assert.Contains($"v{AppInfo.Version}", CliParser.GetHelpText());
        Assert.Equal(AppInfo.Version, new LanDrop.Logging.StartLogEntry().Version);
    }
}
