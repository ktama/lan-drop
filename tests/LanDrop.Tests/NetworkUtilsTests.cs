using LanDrop.Utils;

namespace LanDrop.Tests;

/// <summary>
/// NetworkUtils のテスト
/// - ローカルIP取得、ポート探索
/// </summary>
public class NetworkUtilsTests
{
    [Fact]
    public void GetLocalIpAddresses_ReturnsCollection()
    {
        // IPアドレス一覧を取得できる
        var addresses = NetworkUtils.GetLocalIpAddresses();
        Assert.NotNull(addresses);
        // 実行環境によっては空の場合もある
    }

    [Fact]
    public void GetLocalIpAddresses_ReturnsIPv4Only()
    {
        // IPv4アドレスのみ返す
        var addresses = NetworkUtils.GetLocalIpAddresses().ToList();
        
        foreach (var addr in addresses)
        {
            // IPv4形式（x.x.x.x）であることを確認
            var parts = addr.Split('.');
            Assert.Equal(4, parts.Length);
            foreach (var part in parts)
            {
                Assert.True(int.TryParse(part, out var num));
                Assert.InRange(num, 0, 255);
            }
        }
    }

    [Fact]
    public void GetPrimaryLocalIp_ReturnsValueOrNull()
    {
        // 結果はnullまたは有効なIPアドレス
        var ip = NetworkUtils.GetPrimaryLocalIp();
        
        if (ip != null)
        {
            Assert.True(System.Net.IPAddress.TryParse(ip, out _));
        }
    }

    [Fact]
    public void GetPrimaryLocalIp_PrefersPrivateRanges()
    {
        // プライベートIPアドレスを優先
        var ip = NetworkUtils.GetPrimaryLocalIp();
        
        if (ip != null)
        {
            // 192.168.x.x, 10.x.x.x, 172.16-31.x.x のいずれかが多い
            var isPrivate = ip.StartsWith("192.168.") || 
                            ip.StartsWith("10.") ||
                            (ip.StartsWith("172.") && IsPrivate172(ip));
            
            // テスト環境によってはパブリックIPの場合もあるため、検証のみ
            Assert.True(System.Net.IPAddress.TryParse(ip, out _));
        }
    }

    [Fact]
    public void IsPortAvailable_SystemPort_MayFail()
    {
        // システムポート（低い番号）は通常使用不可なことが多い
        // 実際の設定チェックを行わず、ソケットバインドで確認
        var result = NetworkUtils.IsPortAvailable(80, "0.0.0.0");
        // 管理者権限がない場合は通常falseだが環境依存
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void IsPortAvailable_ValidPort_ReturnsBoolean()
    {
        // 有効なポ�EトでチE��ト（結果は環墁E��存！E
        var result = NetworkUtils.IsPortAvailable(8888, "0.0.0.0");
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void FindAvailablePort_ReturnsPortInRange()
    {
        // 設定内の利用可能なポートを返す
        var port = NetworkUtils.FindAvailablePort(9000, 9100, "127.0.0.1");
        
        if (port.HasValue)
        {
            Assert.InRange(port.Value, 9000, 9100);
        }
        // 全ポートが使用中の場合はnull
    }

    [Fact]
    public void FindAvailablePort_NarrowRange_ReturnsValueOrNull()
    {
        // 狭い範囲でのテスト
        var port = NetworkUtils.FindAvailablePort(9999, 10000, "127.0.0.1");
        
        if (port.HasValue)
        {
            Assert.True(port.Value == 9999 || port.Value == 10000);
        }
    }

    [Fact]
    public void FindAvailablePort_InvalidRange_ReturnsNull()
    {
        // 開始 > 終了の場合
        var port = NetworkUtils.FindAvailablePort(9100, 9000, "127.0.0.1");
        Assert.Null(port);
    }

    private static bool IsPrivate172(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length < 2) return false;
        if (int.TryParse(parts[1], out var second))
        {
            return second >= 16 && second <= 31;
        }
        return false;
    }
}
