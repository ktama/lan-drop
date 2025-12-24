using System.Net;
using LanDrop.Security;

namespace LanDrop.Tests;

/// <summary>
/// CidrRange のテスト
/// - 192.168.0.0/16等で許可/拒否判定
/// </summary>
public class CidrRangeTests
{
    [Theory]
    [InlineData("192.168.1.1", "192.168.0.0/16", true)]
    [InlineData("192.168.255.255", "192.168.0.0/16", true)]
    [InlineData("192.167.1.1", "192.168.0.0/16", false)]
    [InlineData("192.169.1.1", "192.168.0.0/16", false)]
    public void Contains_Class_B_Network(string ip, string cidr, bool expected)
    {
        var range = new CidrRange(cidr);
        var ipAddress = IPAddress.Parse(ip);
        Assert.Equal(expected, range.Contains(ipAddress));
    }

    [Theory]
    [InlineData("10.0.0.1", "10.0.0.0/8", true)]
    [InlineData("10.255.255.255", "10.0.0.0/8", true)]
    [InlineData("11.0.0.1", "10.0.0.0/8", false)]
    [InlineData("9.255.255.255", "10.0.0.0/8", false)]
    public void Contains_Class_A_Network(string ip, string cidr, bool expected)
    {
        var range = new CidrRange(cidr);
        var ipAddress = IPAddress.Parse(ip);
        Assert.Equal(expected, range.Contains(ipAddress));
    }

    [Theory]
    [InlineData("192.168.1.1", "192.168.1.0/24", true)]
    [InlineData("192.168.1.254", "192.168.1.0/24", true)]
    [InlineData("192.168.2.1", "192.168.1.0/24", false)]
    [InlineData("192.168.0.255", "192.168.1.0/24", false)]
    public void Contains_Class_C_Network(string ip, string cidr, bool expected)
    {
        var range = new CidrRange(cidr);
        var ipAddress = IPAddress.Parse(ip);
        Assert.Equal(expected, range.Contains(ipAddress));
    }

    [Theory]
    [InlineData("192.168.1.100", "192.168.1.100/32", true)]
    [InlineData("192.168.1.101", "192.168.1.100/32", false)]
    [InlineData("192.168.1.99", "192.168.1.100/32", false)]
    public void Contains_SingleHost(string ip, string cidr, bool expected)
    {
        // /32 は単一ホスト
        var range = new CidrRange(cidr);
        var ipAddress = IPAddress.Parse(ip);
        Assert.Equal(expected, range.Contains(ipAddress));
    }

    [Theory]
    [InlineData("0.0.0.0", "0.0.0.0/0", true)]
    [InlineData("255.255.255.255", "0.0.0.0/0", true)]
    [InlineData("192.168.1.1", "0.0.0.0/0", true)]
    [InlineData("10.20.30.40", "0.0.0.0/0", true)]
    public void Contains_AllIPs(string ip, string cidr, bool expected)
    {
        // /0 は全IP
        var range = new CidrRange(cidr);
        var ipAddress = IPAddress.Parse(ip);
        Assert.Equal(expected, range.Contains(ipAddress));
    }

    [Theory]
    [InlineData("172.16.0.1", "172.16.0.0/12", true)]
    [InlineData("172.31.255.255", "172.16.0.0/12", true)]
    [InlineData("172.32.0.1", "172.16.0.0/12", false)]
    [InlineData("172.15.255.255", "172.16.0.0/12", false)]
    public void Contains_Class_B_Private(string ip, string cidr, bool expected)
    {
        // 172.16.0.0/12 プライベートネットワーク
        var range = new CidrRange(cidr);
        var ipAddress = IPAddress.Parse(ip);
        Assert.Equal(expected, range.Contains(ipAddress));
    }

    [Theory]
    [InlineData("192.168.1.0", "192.168.1.0/30", true)]
    [InlineData("192.168.1.1", "192.168.1.0/30", true)]
    [InlineData("192.168.1.2", "192.168.1.0/30", true)]
    [InlineData("192.168.1.3", "192.168.1.0/30", true)]
    [InlineData("192.168.1.4", "192.168.1.0/30", false)]
    public void Contains_SmallSubnet(string ip, string cidr, bool expected)
    {
        // /30 は4アドレス
        var range = new CidrRange(cidr);
        var ipAddress = IPAddress.Parse(ip);
        Assert.Equal(expected, range.Contains(ipAddress));
    }

    [Fact]
    public void Constructor_ValidCidr_Succeeds()
    {
        var range = new CidrRange("192.168.1.0/24");
        Assert.NotNull(range);
    }

    [Theory]
    [InlineData("192.168.1.0")]  // CIDRプレフィックスなし
    [InlineData("192.168.1.0/")]  // プレフィックス値なし
    [InlineData("192.168.1.0/33")]  // 無効なプレフィックス（32超過）
    [InlineData("192.168.1.0/-1")]  // 負のプレフィックス
    [InlineData("192.168.1.0/abc")]  // 非数値プレフィックス
    [InlineData("invalid/24")]  // 無効なIP
    [InlineData("256.256.256.256/24")]  // 範囲外のIP
    public void Constructor_InvalidCidr_ThrowsException(string invalidCidr)
    {
        Assert.ThrowsAny<Exception>(() => new CidrRange(invalidCidr));
    }

    [Theory]
    [InlineData("192.168.1.128/25", "192.168.1.128", true)]
    [InlineData("192.168.1.128/25", "192.168.1.255", true)]
    [InlineData("192.168.1.128/25", "192.168.1.127", false)]
    [InlineData("192.168.1.128/25", "192.168.1.0", false)]
    public void Contains_HalfNetwork(string cidr, string ip, bool expected)
    {
        // /25 は半分のネットワーク
        var range = new CidrRange(cidr);
        var ipAddress = IPAddress.Parse(ip);
        Assert.Equal(expected, range.Contains(ipAddress));
    }

    [Fact]
    public void Contains_LoopbackAddress()
    {
        var range = new CidrRange("127.0.0.0/8");
        Assert.True(range.Contains(IPAddress.Loopback));
        Assert.True(range.Contains(IPAddress.Parse("127.0.0.1")));
        Assert.True(range.Contains(IPAddress.Parse("127.255.255.255")));
    }

    [Fact]
    public void ToString_ReturnsNormalizedCidr()
    {
        var range = new CidrRange("192.168.1.0/24");
        var str = range.ToString();
        Assert.Contains("192.168.1.0", str);
        Assert.Contains("/24", str);
    }
}
