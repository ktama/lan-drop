using System.Net;
using LanDrop.Security;
using Microsoft.AspNetCore.Http;

namespace LanDrop.Tests;

/// <summary>
/// IpFilter のテスト
/// - 複数CIDR設定でのフィルタリング
/// </summary>
public class IpFilterTests
{
    [Fact]
    public void IsAllowed_NoRestrictions_AlwaysTrue()
    {
        // 制限なしの場合は常に許可
        var filter = new IpFilter(null);
        
        Assert.True(filter.IsAllowed(IPAddress.Parse("192.168.1.1")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("10.0.0.1")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void IsAllowed_EmptyString_AlwaysTrue()
    {
        // 空文字列の場合も制限なし
        var filter = new IpFilter("");
        Assert.True(filter.IsAllowed(IPAddress.Parse("192.168.1.1")));
    }

    [Fact]
    public void IsAllowed_SingleCidr_Works()
    {
        // 単一CIDRでのフィルタリング
        var filter = new IpFilter("192.168.1.0/24");
        
        Assert.True(filter.IsAllowed(IPAddress.Parse("192.168.1.1")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("192.168.1.254")));
        Assert.False(filter.IsAllowed(IPAddress.Parse("192.168.2.1")));
        Assert.False(filter.IsAllowed(IPAddress.Parse("10.0.0.1")));
    }

    [Fact]
    public void IsAllowed_MultipleCidrs_Works()
    {
        // 複数CIDRでのフィルタリング（カンマ区切り）
        var filter = new IpFilter("192.168.1.0/24,10.0.0.0/8");
        
        Assert.True(filter.IsAllowed(IPAddress.Parse("192.168.1.1")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("10.20.30.40")));
        Assert.False(filter.IsAllowed(IPAddress.Parse("172.16.0.1")));
    }

    [Fact]
    public void IsAllowed_WithSpaces_Works()
    {
        // スペースを含むCIDR指定
        var filter = new IpFilter("192.168.1.0/24, 10.0.0.0/8 , 172.16.0.0/12");
        
        Assert.True(filter.IsAllowed(IPAddress.Parse("192.168.1.1")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("10.0.0.1")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("172.16.0.1")));
        Assert.False(filter.IsAllowed(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void IsAllowed_Localhost_Always()
    {
        // localhostは常に許可（特別扱い）
        var filter = new IpFilter("192.168.1.0/24");
        
        Assert.True(filter.IsAllowed(IPAddress.Loopback));
        Assert.True(filter.IsAllowed(IPAddress.Parse("127.0.0.1")));
    }

    [Fact]
    public void IsAllowed_IPv6Loopback_Always()
    {
        // IPv6 loopbackも許可
        var filter = new IpFilter("192.168.1.0/24");
        
        Assert.True(filter.IsAllowed(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void HasRestrictions_NoFilter_False()
    {
        var filter = new IpFilter(null);
        Assert.False(filter.HasRestrictions);
    }

    [Fact]
    public void HasRestrictions_WithFilter_True()
    {
        var filter = new IpFilter("192.168.0.0/16");
        Assert.True(filter.HasRestrictions);
    }

    [Fact]
    public void Constructor_InvalidCidr_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => new IpFilter("invalid/cidr"));
        Assert.Throws<ArgumentException>(() => new IpFilter("192.168.1.0/33"));
    }

    [Fact]
    public void IsAllowed_PrivateNetworks_CommonPattern()
    {
        // よくある内部ネットワークパターン
        var filter = new IpFilter("10.0.0.0/8,172.16.0.0/12,192.168.0.0/16");
        
        // RFC1918プライベートアドレス
        Assert.True(filter.IsAllowed(IPAddress.Parse("10.0.0.1")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("10.255.255.255")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("172.16.0.1")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("172.31.255.255")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("192.168.0.1")));
        Assert.True(filter.IsAllowed(IPAddress.Parse("192.168.255.255")));
        
        // パブリックアドレスは拒否
        Assert.False(filter.IsAllowed(IPAddress.Parse("8.8.8.8")));
        Assert.False(filter.IsAllowed(IPAddress.Parse("1.1.1.1")));
    }

    [Fact]
    public void IsAllowed_HttpContext_Works()
    {
        // HttpContextを使用したテスト
        var filter = new IpFilter("192.168.1.0/24");
        
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        
        Assert.True(filter.IsAllowed(context));
    }

    [Fact]
    public void IsAllowed_HttpContext_Denied()
    {
        var filter = new IpFilter("192.168.1.0/24");
        
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        
        Assert.False(filter.IsAllowed(context));
    }

    [Fact]
    public void IsAllowed_HttpContext_NullRemoteIp_Denied()
    {
        // RemoteIpAddressがnullの場合は拒否
        var filter = new IpFilter("192.168.1.0/24");
        
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = null;
        
        Assert.False(filter.IsAllowed(context));
    }

    [Fact]
    public void IsAllowed_SingleHost_Works()
    {
        // 単一ホスト指定（/32）
        var filter = new IpFilter("192.168.1.100/32");
        
        Assert.True(filter.IsAllowed(IPAddress.Parse("192.168.1.100")));
        Assert.False(filter.IsAllowed(IPAddress.Parse("192.168.1.101")));
        Assert.False(filter.IsAllowed(IPAddress.Parse("192.168.1.99")));
    }

    [Fact]
    public void IsAllowed_IPv4MappedIPv6_Works()
    {
        // IPv4マップドIPv6アドレス（::ffff:192.168.1.1形式）
        var filter = new IpFilter("192.168.1.0/24");
        
        // IPv4マップドIPv6は内部でIPv4に変換される
        var mappedIp = IPAddress.Parse("192.168.1.50").MapToIPv6();
        Assert.True(mappedIp.IsIPv4MappedToIPv6);
        Assert.True(filter.IsAllowed(mappedIp));
    }

    [Fact]
    public void IsAllowed_IPv4MappedIPv6_DeniedWhenOutOfRange()
    {
        var filter = new IpFilter("192.168.1.0/24");
        
        var mappedIp = IPAddress.Parse("10.0.0.1").MapToIPv6();
        Assert.True(mappedIp.IsIPv4MappedToIPv6);
        Assert.False(filter.IsAllowed(mappedIp));
    }

    [Fact]
    public void AllowedCidrs_ReturnsConfiguredRanges()
    {
        var filter = new IpFilter("192.168.1.0/24,10.0.0.0/8");
        
        Assert.Equal(2, filter.AllowedCidrs.Count);
    }

    [Fact]
    public void IsAllowed_NullIpAddress_ReturnsFalse()
    {
        var filter = new IpFilter("192.168.1.0/24");
        
        Assert.False(filter.IsAllowed((IPAddress?)null));
    }
}
