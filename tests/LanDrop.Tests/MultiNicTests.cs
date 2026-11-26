using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LanDrop.Config;
using LanDrop.Utils;

namespace LanDrop.Tests;

/// <summary>
/// 複数NIC環境での挙動のテスト
/// - 候補IPの絞り込みと順位、表示ホストの決定、--bind / --interface、ポート確認
/// </summary>
public class MultiNicTests
{
    private static AddressCandidate C(string ip, bool isVirtual = false, bool hasGateway = false, string name = "nic")
        => new(IPAddress.Parse(ip), name, isVirtual, hasGateway);

    // ── RankCandidates: 絞り込み ─────────────────────────

    [Fact]
    public void Rank_ExcludesLinkLocal_WhenOtherCandidatesExist()
    {
        var result = NetworkUtils.RankCandidates(
            [C("169.254.10.20"), C("192.168.1.10", hasGateway: true)], null);

        Assert.Equal(["192.168.1.10"], result);
    }

    [Fact]
    public void Rank_KeepsLinkLocal_WhenItIsTheOnlyCandidate()
    {
        // DHCPのないケーブル直結などでは APIPA が唯一の到達手段
        var result = NetworkUtils.RankCandidates([C("169.254.10.20")], null);

        Assert.Equal(["169.254.10.20"], result);
    }

    [Fact]
    public void Rank_ExcludesVirtualAdapters_WhenPhysicalExists()
    {
        var result = NetworkUtils.RankCandidates(
            [C("172.20.0.1", isVirtual: true, name: "vEthernet (WSL)"), C("10.0.0.5")], null);

        Assert.Equal(["10.0.0.5"], result);
    }

    [Fact]
    public void Rank_KeepsVirtualAdapters_WhenNoPhysicalExists()
    {
        var result = NetworkUtils.RankCandidates([C("172.20.0.1", isVirtual: true)], null);

        Assert.Equal(["172.20.0.1"], result);
    }

    [Fact]
    public void Rank_KeepsVirtualAdapter_WhenItIsRouteSource()
    {
        // Hyper-V 外部スイッチでは実LANのIPが vEthernet に付く
        var result = NetworkUtils.RankCandidates(
            [C("192.168.1.10", isVirtual: true, hasGateway: true), C("172.20.0.1", isVirtual: true)],
            IPAddress.Parse("192.168.1.10"));

        Assert.Equal(["192.168.1.10"], result);
    }

    [Fact]
    public void Rank_ExcludesIPv6AndLoopback()
    {
        var result = NetworkUtils.RankCandidates([C("127.0.0.1"), C("fe80::1"), C("192.168.1.10")], null);

        Assert.Equal(["192.168.1.10"], result);
    }

    [Fact]
    public void Rank_RemovesDuplicates()
    {
        var result = NetworkUtils.RankCandidates([C("192.168.1.10"), C("192.168.1.10")], null);

        Assert.Equal(["192.168.1.10"], result);
    }

    // ── RankCandidates: 順位 ─────────────────────────────

    [Fact]
    public void Rank_RouteSourceComesFirst_EvenIfOtherRangeRanksHigher()
    {
        // 有線(10.x)がOSの既定ルート、Wi-Fi(192.168.x)にもゲートウェイあり
        var result = NetworkUtils.RankCandidates(
            [C("192.168.1.10", hasGateway: true), C("10.0.0.5", hasGateway: true)],
            IPAddress.Parse("10.0.0.5"));

        Assert.Equal(["10.0.0.5", "192.168.1.10"], result);
    }

    [Fact]
    public void Rank_VirtualRouteSource_DoesNotGetTopPriority()
    {
        // 全トンネルVPNが既定ルートでも、LANのアドレスを先に出す
        var result = NetworkUtils.RankCandidates(
            [C("100.64.0.2", isVirtual: true, hasGateway: true), C("192.168.1.10", hasGateway: true)],
            IPAddress.Parse("100.64.0.2"));

        Assert.Equal("192.168.1.10", result[0]);
        Assert.Contains("100.64.0.2", result);
    }

    [Fact]
    public void Rank_GatewayBeforeNoGateway()
    {
        var result = NetworkUtils.RankCandidates(
            [C("192.168.56.1"), C("10.0.0.5", hasGateway: true)], null);

        Assert.Equal(["10.0.0.5", "192.168.56.1"], result);
    }

    [Fact]
    public void Rank_SameRank_SortedNumerically_NotLexically()
    {
        // 文字列順だと "192.168.10.5" < "192.168.2.5" になってしまう
        var result = NetworkUtils.RankCandidates([C("192.168.10.5"), C("192.168.2.5")], null);

        Assert.Equal(["192.168.2.5", "192.168.10.5"], result);
    }

    [Fact]
    public void Rank_PrivateRangeOrder()
    {
        var result = NetworkUtils.RankCandidates(
            [C("8.8.8.8"), C("172.16.0.1"), C("10.0.0.1"), C("192.168.0.1")], null);

        Assert.Equal(["192.168.0.1", "10.0.0.1", "172.16.0.1", "8.8.8.8"], result);
    }

    // ── IsVirtualInterface ───────────────────────────────

    [Theory]
    [InlineData("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet)]
    [InlineData("イーサネット 2", "VirtualBox Host-Only Ethernet Adapter", NetworkInterfaceType.Ethernet)]
    [InlineData("Tailscale", "Tailscale Tunnel", NetworkInterfaceType.Unknown)]
    [InlineData("isatap", "Microsoft ISATAP Adapter", NetworkInterfaceType.Tunnel)]
    public void IsVirtualInterface_DetectsVirtual(string name, string description, NetworkInterfaceType type)
    {
        Assert.True(NetworkUtils.IsVirtualInterface(name, description, type));
    }

    [Theory]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6E AX211 160MHz", NetworkInterfaceType.Wireless80211)]
    [InlineData("イーサネット", "Realtek PCIe GbE Family Controller", NetworkInterfaceType.Ethernet)]
    public void IsVirtualInterface_PhysicalIsNotVirtual(string name, string description, NetworkInterfaceType type)
    {
        Assert.False(NetworkUtils.IsVirtualInterface(name, description, type));
    }

    // ── ResolveDisplayHost ───────────────────────────────

    [Theory]
    [InlineData("10.0.0.5", "10.0.0.5")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData("mypc.local", "mypc.local")]
    [InlineData("[fe80::1]", "[fe80::1]")]
    public void ResolveDisplayHost_UsesRequestHost(string requestHost, string expected)
    {
        var host = NetworkUtils.ResolveDisplayHost(requestHost, IPAddress.Parse("192.168.1.10"), "192.168.1.10");

        Assert.Equal(expected, host);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public void ResolveDisplayHost_Loopback_UsesFallback(string requestHost)
    {
        // localhost のURLは他人に共有できないので、LAN側のIPを表示する
        var host = NetworkUtils.ResolveDisplayHost(requestHost, IPAddress.Loopback, "192.168.1.10");

        Assert.Equal("192.168.1.10", host);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("evil\"onmouseover=\"x")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveDisplayHost_InvalidHost_UsesLocalAddress(string? requestHost)
    {
        var host = NetworkUtils.ResolveDisplayHost(requestHost, IPAddress.Parse("10.0.0.5"), "192.168.1.10");

        Assert.Equal("10.0.0.5", host);
    }

    [Fact]
    public void ResolveDisplayHost_IPv4MappedLocalAddress_IsUnmapped()
    {
        var mapped = IPAddress.Parse("10.0.0.5").MapToIPv6();
        var host = NetworkUtils.ResolveDisplayHost(null, mapped, "192.168.1.10");

        Assert.Equal("10.0.0.5", host);
    }

    [Fact]
    public void ResolveDisplayHost_NothingUsable_UsesFallback()
    {
        var host = NetworkUtils.ResolveDisplayHost(null, null, "192.168.1.10");

        Assert.Equal("192.168.1.10", host);
    }

    // ── TryParseBindAddress ──────────────────────────────

    [Theory]
    [InlineData("0.0.0.0", "0.0.0.0")]
    [InlineData("*", "0.0.0.0")]
    [InlineData("::", "::")]
    [InlineData("[::]", "::")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData(" 127.0.0.1 ", "127.0.0.1")]
    public void TryParseBindAddress_Valid(string value, string expected)
    {
        Assert.True(NetworkUtils.TryParseBindAddress(value, out var address));
        Assert.Equal(IPAddress.Parse(expected), address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("192.168.1")]
    [InlineData("999.1.1.1")]
    public void TryParseBindAddress_Invalid(string value)
    {
        Assert.False(NetworkUtils.TryParseBindAddress(value, out _));
    }

    [Fact]
    public void IsAnyAddress()
    {
        Assert.True(NetworkUtils.IsAnyAddress(IPAddress.Any));
        Assert.True(NetworkUtils.IsAnyAddress(IPAddress.IPv6Any));
        Assert.False(NetworkUtils.IsAnyAddress(IPAddress.Loopback));
    }

    [Fact]
    public void IsAssignedToLocalInterface()
    {
        Assert.True(NetworkUtils.IsAssignedToLocalInterface(IPAddress.Loopback));
        // 文書用アドレスはどのNICにも割り当てられていない
        Assert.False(NetworkUtils.IsAssignedToLocalInterface(IPAddress.Parse("192.0.2.123")));
    }

    [Fact]
    public void IsPortAvailable_InvalidBindAddress_ReturnsFalse()
    {
        Assert.False(NetworkUtils.IsPortAvailable(9000, "not-an-ip"));
    }

    // ── ポート確認 ───────────────────────────────────────

    [Fact]
    public void IsPortAvailable_Any_DetectsPortUsedOnSpecificAddress()
    {
        // 他プロセスが特定アドレス(ここでは127.0.0.1)だけで待ち受けていても、
        // 全アドレス待ち受けの確認で「使用中」と判定できること
        using var other = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        other.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        other.Listen();
        var port = ((IPEndPoint)other.LocalEndPoint!).Port;

        Assert.False(NetworkUtils.IsPortAvailable(port, IPAddress.Any));
    }

    [Fact]
    public void IsPortAvailable_Any_DetectsPortUsedOnIPv6Any()
    {
        // Kestrel の ListenAnyIP と同じ形で使用中のポート
        using var other = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = true };
        other.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        other.Listen();
        var port = ((IPEndPoint)other.LocalEndPoint!).Port;

        Assert.False(NetworkUtils.IsPortAvailable(port, "0.0.0.0"));
    }

    [Fact]
    public void IsPortAvailable_FreePort_ReturnsTrue()
    {
        int port;
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            port = ((IPEndPoint)probe.LocalEndPoint!).Port;
        }

        Assert.True(NetworkUtils.IsPortAvailable(port, IPAddress.Any));
    }

    // ── --interface ──────────────────────────────────────

    [Fact]
    public void TryResolveInterfaceAddress_Unknown_ReturnsErrorWithAvailableNames()
    {
        var ok = NetworkUtils.TryResolveInterfaceAddress("no-such-nic-xyz", out var address, out var error);

        Assert.False(ok);
        Assert.Null(address);
        Assert.Contains("no-such-nic-xyz", error);
        Assert.Contains("Available", error);
    }

    [Fact]
    public void TryResolveInterfaceAddress_ExistingNic_ReturnsItsIPv4()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
            n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork));

        if (nic == null)
            return; // IPv4を持つNICがない環境

        var ok = NetworkUtils.TryResolveInterfaceAddress(nic.Name.ToUpperInvariant(), out var address, out var error);

        Assert.True(ok, error);
        Assert.Contains(nic.GetIPProperties().UnicastAddresses, a => a.Address.Equals(address));
    }

    // ── CliParser ────────────────────────────────────────

    private static readonly string Dir = Path.GetTempPath();

    [Fact]
    public void Cli_Bind_Invalid_ReturnsError()
    {
        var (config, error, _) = CliParser.Parse(["--dir", Dir, "--bind", "localhost"]);

        Assert.Null(config);
        Assert.Contains("--bind", error);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("::")]
    [InlineData("*")]
    public void Cli_Bind_Valid(string bind)
    {
        var (config, error, _) = CliParser.Parse(["--dir", Dir, "--bind", bind]);

        Assert.Null(error);
        Assert.Equal(bind, config!.BindAddress);
    }

    [Fact]
    public void Cli_Interface_IsStored()
    {
        var (config, error, _) = CliParser.Parse(["--dir", Dir, "--interface", "Wi-Fi"]);

        Assert.Null(error);
        Assert.Equal("Wi-Fi", config!.InterfaceName);
    }

    [Fact]
    public void Cli_Interface_MissingValue_ReturnsError()
    {
        var (config, error, _) = CliParser.Parse(["--dir", Dir, "--interface"]);

        Assert.Null(config);
        Assert.Contains("--interface", error);
    }

    [Fact]
    public void Cli_BindAndInterface_Together_ReturnsError()
    {
        var (config, error, _) = CliParser.Parse(["--dir", Dir, "--bind", "192.168.1.10", "--interface", "Wi-Fi"]);

        Assert.Null(config);
        Assert.Contains("cannot be used together", error);
    }

    [Fact]
    public void Cli_Help_MentionsInterface()
    {
        Assert.Contains("--interface", CliParser.GetHelpText());
    }
}
