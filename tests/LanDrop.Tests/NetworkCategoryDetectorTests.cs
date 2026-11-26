using System.Net.NetworkInformation;
using LanDrop.Utils;

namespace LanDrop.Tests;

/// <summary>
/// NetworkCategoryDetector のテスト
/// </summary>
public class NetworkCategoryDetectorTests
{
    [Fact]
    public void GetConnectedNetworks_DoesNotThrow()
    {
        var networks = NetworkCategoryDetector.GetConnectedNetworks();

        Assert.NotNull(networks);
        if (!OperatingSystem.IsWindows())
            Assert.Empty(networks);
    }

    [Fact]
    public void GetConnectedNetworks_OnWindows_WorksWhenConnected()
    {
        // Windows でゲートウェイ付きの接続がある環境では、例外なく1件以上取得できること
        if (!OperatingSystem.IsWindows())
            return;

        var connected = NetworkInterface.GetAllNetworkInterfaces().Any(n =>
            n.OperationalStatus == OperationalStatus.Up &&
            n.GetIPProperties().GatewayAddresses.Count > 0);
        if (!connected)
            return;

        var networks = NetworkCategoryDetector.GetConnectedNetworksCore();

        Assert.NotEmpty(networks);
        Assert.All(networks, n => Assert.True(Enum.IsDefined(n.Category)));
    }

    [Fact]
    public void FormatPublicNetworkWarning_NamesNetworks()
    {
        var text = NetworkCategoryDetector.FormatPublicNetworkWarning(["Cafe Wi-Fi", "ネットワーク 2"]);

        Assert.Contains("\"Cafe Wi-Fi\", \"ネットワーク 2\"", text);
        Assert.Contains("Public", text);
    }
}
