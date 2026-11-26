using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanDrop.Utils;

/// <summary>
/// ローカルアドレスの候補（順位付け用）
/// </summary>
/// <param name="Address">IPアドレス</param>
/// <param name="InterfaceName">NIC名（例: "Wi-Fi"）</param>
/// <param name="IsVirtual">仮想NIC・VPN・トンネルなど、LAN共有には通常使わないNICか</param>
/// <param name="HasGateway">NICにIPv4デフォルトゲートウェイがあるか</param>
public sealed record AddressCandidate(IPAddress Address, string InterfaceName, bool IsVirtual, bool HasGateway);

/// <summary>
/// ネットワーク関連ユーティリティ
/// </summary>
public static class NetworkUtils
{
    /// <summary>
    /// 仮想NICとみなすNIC名・説明のキーワード
    /// </summary>
    private static readonly string[] VirtualAdapterKeywords =
    [
        "Hyper-V", "vEthernet", "VirtualBox", "VMware", "WSL", "Docker",
        "TAP-Windows", "WireGuard", "Tailscale", "ZeroTier", "Npcap", "Loopback",
        "Virtual", "VPN"
    ];

    /// <summary>
    /// ローカルIPアドレス一覧を取得（フィルタなし）
    /// </summary>
    public static IEnumerable<string> GetLocalIpAddresses()
    {
        var addresses = new List<string>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = ni.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        addresses.Add(addr.Address.ToString());
                    }
                }
            }
        }
        catch
        {
            // エラー時は空リスト
        }

        return addresses;
    }

    /// <summary>
    /// 共有URLとして表示するローカルIPv4アドレスを、優先度の高い順に取得
    /// </summary>
    public static IReadOnlyList<string> GetPreferredLocalIps()
    {
        return RankCandidates(EnumerateCandidates(), GetRouteSourceAddress());
    }

    /// <summary>
    /// 最も可能性の高いローカルIPを取得
    /// </summary>
    public static string? GetPrimaryLocalIp()
    {
        var addresses = GetPreferredLocalIps();
        return addresses.FirstOrDefault();
    }

    /// <summary>
    /// 候補アドレスを絞り込み、優先度順に並べる
    /// </summary>
    /// <remarks>
    /// 絞り込み: IPv4以外とループバックを除く。APIPA(169.254/16)と仮想NICは「ノイズ」とし、
    /// ノイズ以外の候補が1つでもあれば除外する（全部ノイズなら残す）。
    /// OSのルーティングが選ぶ送信元アドレスはノイズ扱いしない。
    /// 順位: OSルートの送信元（仮想NICでないもの） → ゲートウェイあり → プライベート帯 → アドレスの数値順。
    /// </remarks>
    public static IReadOnlyList<string> RankCandidates(IEnumerable<AddressCandidate> candidates, IPAddress? routeSource)
    {
        var list = candidates
            .Where(c => c.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(c.Address))
            .ToList();

        bool IsRouteSource(AddressCandidate c) => routeSource != null && c.Address.Equals(routeSource);
        bool IsNoise(AddressCandidate c) => !IsRouteSource(c) && (c.IsVirtual || IsLinkLocal(c.Address));

        if (list.Any(c => !IsNoise(c)))
        {
            list = list.Where(c => !IsNoise(c)).ToList();
        }

        return list
            .OrderByDescending(c => IsRouteSource(c) && !c.IsVirtual)
            .ThenByDescending(c => c.HasGateway)
            .ThenBy(c => GetPrivateRangeRank(c.Address))
            .ThenBy(c => ToUInt32(c.Address))
            .Select(c => c.Address.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// OSのルーティングテーブルでデフォルトルートに使われる送信元IPv4アドレスを取得
    /// </summary>
    /// <remarks>UDPソケットの Connect は経路を決めるだけでパケットを送らない。</remarks>
    public static IPAddress? GetRouteSourceAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // 192.0.2.0/24 は文書用アドレス（TEST-NET-1）。デフォルトルートの経路を引くためだけに使う
            socket.Connect(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9));
            var address = (socket.LocalEndPoint as IPEndPoint)?.Address;
            return address == null || IPAddress.Any.Equals(address) ? null : address;
        }
        catch
        {
            // デフォルトルートがない（孤立したLANなど）
            return null;
        }
    }

    /// <summary>
    /// 稼働中NICのIPv4アドレスを候補として列挙
    /// </summary>
    private static List<AddressCandidate> EnumerateCandidates()
    {
        var candidates = new List<AddressCandidate>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = ni.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(g.Address) &&
                    !IPAddress.Any.Equals(g.Address));
                var isVirtual = IsVirtualInterface(ni);

                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (!IsUsableAddressState(addr))
                        continue;

                    candidates.Add(new AddressCandidate(addr.Address, ni.Name, isVirtual, hasGateway));
                }
            }
        }
        catch
        {
            // Best-effort; fall back to empty list.
        }

        return candidates;
    }

    /// <summary>
    /// 仮想NIC・トンネルかどうか
    /// </summary>
    public static bool IsVirtualInterface(string name, string description, NetworkInterfaceType type)
    {
        if (type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Loopback)
            return true;

        return VirtualAdapterKeywords.Any(k =>
            name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
            description.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsVirtualInterface(NetworkInterface ni)
        => IsVirtualInterface(ni.Name, ni.Description, ni.NetworkInterfaceType);

    /// <summary>
    /// 重複アドレス検出中（Tentative）や重複（Duplicate）のアドレスを除く
    /// </summary>
    private static bool IsUsableAddressState(UnicastIPAddressInformation addr)
    {
        try
        {
            return addr.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred;
        }
        catch (PlatformNotSupportedException)
        {
            return true;
        }
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private static int GetPrivateRangeRank(IPAddress address)
    {
        var b = address.GetAddressBytes();
        if (b[0] == 192 && b[1] == 168) return 0;
        if (b[0] == 10) return 1;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return 2;
        if (b[0] == 169 && b[1] == 254) return 4;
        return 3;
    }

    private static uint ToUInt32(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    // ── 画面に出すホスト ─────────────────────────────────

    /// <summary>
    /// リクエストを受けたホスト名から、画面に表示するホスト部分を決める
    /// </summary>
    /// <param name="requestHost">Hostヘッダのホスト部（ポートなし。IPv6は角括弧付き）</param>
    /// <param name="localAddress">接続を受けたローカルアドレス</param>
    /// <param name="fallback">決められない場合やループバックの場合に使うホスト</param>
    /// <remarks>
    /// クライアントが実際に到達できたアドレスを返すため、複数NICでも接続元ごとに正しいURLになる。
    /// ループバック（localhost）で開いた場合は他の人に共有できないので fallback（LAN側のIP）を返す。
    /// Hostヘッダは利用者が自由に送れるため、ホスト名として妥当な文字列だけを受け付ける。
    /// </remarks>
    public static string ResolveDisplayHost(string? requestHost, IPAddress? localAddress, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requestHost))
        {
            var bare = requestHost.Trim();
            var unbracketed = bare.StartsWith('[') && bare.EndsWith(']') ? bare[1..^1] : bare;
            var kind = Uri.CheckHostName(unbracketed);

            if (kind is UriHostNameType.IPv4 or UriHostNameType.IPv6 or UriHostNameType.Dns)
            {
                var isLoopback = string.Equals(unbracketed, "localhost", StringComparison.OrdinalIgnoreCase) ||
                                 (IPAddress.TryParse(unbracketed, out var parsed) && IPAddress.IsLoopback(parsed));
                if (!isLoopback)
                {
                    return kind == UriHostNameType.IPv6 ? $"[{unbracketed}]" : unbracketed;
                }
                return fallback;
            }
        }

        if (localAddress != null)
        {
            if (localAddress.IsIPv4MappedToIPv6)
                localAddress = localAddress.MapToIPv4();

            if (!IPAddress.IsLoopback(localAddress))
            {
                return localAddress.AddressFamily == AddressFamily.InterNetworkV6
                    ? $"[{localAddress}]"
                    : localAddress.ToString();
            }
        }

        return fallback;
    }

    /// <summary>
    /// URLのホスト部として使える文字列にする（IPv6は角括弧で囲む）
    /// </summary>
    public static string FormatHostForUrl(IPAddress address)
        => address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

    // ── バインドアドレス ─────────────────────────────────

    /// <summary>
    /// --bind の値を解析する。"0.0.0.0" と "*" は IPv4 全アドレス、"::" は IPv6 全アドレス
    /// </summary>
    public static bool TryParseBindAddress(string? value, out IPAddress address)
    {
        address = IPAddress.Any;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var v = value.Trim();
        if (v == "*")
        {
            address = IPAddress.Any;
            return true;
        }

        if (v.StartsWith('[') && v.EndsWith(']'))
            v = v[1..^1];

        if (IPAddress.TryParse(v, out var parsed))
        {
            // IPAddress.TryParse は "192.168.1" を 192.168.0.1 と解釈するため、IPv4 は4オクテット表記に限る
            if (parsed.AddressFamily == AddressFamily.InterNetwork && v.Count(ch => ch == '.') != 3)
                return false;

            address = parsed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 全アドレスで待ち受けるバインドアドレスか
    /// </summary>
    public static bool IsAnyAddress(IPAddress address)
        => IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address);

    /// <summary>
    /// このPCのいずれかのNIC（ループバック含む）に割り当てられたアドレスか
    /// </summary>
    public static bool IsAssignedToLocalInterface(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Any(a => a.Address.Equals(address));
        }
        catch
        {
            // 判定できない場合はKestrelのバインド結果に任せる
            return true;
        }
    }

    /// <summary>
    /// NIC名（例: "Wi-Fi"）または説明に一致するNICのIPv4アドレスを取得
    /// </summary>
    /// <param name="interfaceName">NIC名または説明（大文字小文字を区別しない完全一致）</param>
    /// <param name="address">見つかったアドレス</param>
    /// <param name="error">失敗時のメッセージ（利用可能なNIC名を含む）</param>
    public static bool TryResolveInterfaceAddress(string interfaceName, out IPAddress? address, out string? error)
    {
        address = null;
        error = null;

        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception ex)
        {
            error = $"Failed to enumerate network interfaces: {ex.Message}";
            return false;
        }

        var ni = interfaces.FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase))
                 ?? interfaces.FirstOrDefault(n => string.Equals(n.Description, interfaceName, StringComparison.OrdinalIgnoreCase));

        if (ni == null)
        {
            // フィルタドライバの疑似NICなどを避けるため、IPv4を持つ稼働中のNICだけを示す
            var names = interfaces
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => (n.Name, Ip: n.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address))
                .Where(x => x.Ip != null)
                .Select(x => $"\"{x.Name}\" ({x.Ip})")
                .ToList();
            var available = names.Count > 0 ? string.Join(", ", names) : "(none)";
            error = $"Network interface not found: \"{interfaceName}\". Available: {available}";
            return false;
        }

        if (ni.OperationalStatus != OperationalStatus.Up)
        {
            error = $"Network interface \"{ni.Name}\" is not up.";
            return false;
        }

        var ipv4 = ni.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && IsUsableAddressState(a))
            .Select(a => a.Address)
            .OrderBy(a => IsLinkLocal(a))
            .FirstOrDefault();

        if (ipv4 == null)
        {
            error = $"Network interface \"{ni.Name}\" has no IPv4 address.";
            return false;
        }

        address = ipv4;
        return true;
    }

    // ── ポート ───────────────────────────────────────────

    /// <summary>
    /// ポートが使用可能かチェック
    /// </summary>
    /// <remarks>
    /// Kestrel の ListenAnyIP と同じく、全アドレス指定時は IPv6 デュアルスタックで確認する。
    /// ExclusiveAddressUse を付けることで、他プロセスが特定NICのアドレスで同じポートを
    /// 使っている場合も「使用中」と判定する（Windowsではワイルドカードとの共存バインドが成功してしまうため）。
    /// </remarks>
    public static bool IsPortAvailable(int port, string bindAddress = "0.0.0.0")
    {
        if (!TryParseBindAddress(bindAddress, out var address))
            return false;

        return IsPortAvailable(port, address);
    }

    /// <summary>
    /// ポートが使用可能かチェック
    /// </summary>
    public static bool IsPortAvailable(int port, IPAddress bindAddress)
    {
        if (IsAnyAddress(bindAddress) && Socket.OSSupportsIPv6)
        {
            return TryBind(new IPEndPoint(IPAddress.IPv6Any, port), dualMode: true);
        }

        return TryBind(new IPEndPoint(bindAddress, port), dualMode: false);
    }

    private static bool TryBind(IPEndPoint endpoint, bool dualMode)
    {
        try
        {
            using var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            if (OperatingSystem.IsWindows())
            {
                socket.ExclusiveAddressUse = true;
            }
            if (dualMode)
            {
                socket.DualMode = true;
            }
            socket.Bind(endpoint);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 利用可能なポートを探索
    /// </summary>
    public static int? FindAvailablePort(int startPort = 8000, int endPort = 8100, string bindAddress = "0.0.0.0")
    {
        if (!TryParseBindAddress(bindAddress, out var address))
            return null;

        return FindAvailablePort(startPort, endPort, address);
    }

    /// <summary>
    /// 利用可能なポートを探索
    /// </summary>
    public static int? FindAvailablePort(int startPort, int endPort, IPAddress bindAddress)
    {
        for (int port = startPort; port <= endPort; port++)
        {
            if (IsPortAvailable(port, bindAddress))
            {
                return port;
            }
        }
        return null;
    }
}
