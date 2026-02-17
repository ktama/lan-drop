using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanDrop.Utils;

/// <summary>
/// ネットワーク関連ユーティリティ
/// </summary>
public static class NetworkUtils
{
    /// <summary>
    /// ローカルIPアドレス一覧を取得
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

    public static IReadOnlyList<string> GetPreferredLocalIps()
    {
        var candidates = new List<(string Address, bool HasGateway)>();

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

                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        candidates.Add((addr.Address.ToString(), hasGateway));
                    }
                }
            }
        }
        catch
        {
            // Best-effort; fall back to empty list.
        }

        return candidates
            .OrderByDescending(c => c.HasGateway)
            .ThenBy(c => GetPrivateRangeRank(c.Address))
            .ThenBy(c => c.Address, StringComparer.Ordinal)
            .Select(c => c.Address)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 最も可能性の高いローカルIPを取得
    /// </summary>
    public static string? GetPrimaryLocalIp()
    {
        var addresses = GetPreferredLocalIps();
        return addresses.FirstOrDefault();
    }

    private static int GetPrivateRangeRank(string address)
    {
        if (address.StartsWith("192.168.", StringComparison.Ordinal)) return 0;
        if (address.StartsWith("10.", StringComparison.Ordinal)) return 1;
        if (address.StartsWith("172.", StringComparison.Ordinal))
        {
            var parts = address.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var second))
            {
                if (second >= 16 && second <= 31) return 2;
            }
        }
        if (address.StartsWith("169.254.", StringComparison.Ordinal)) return 4;
        return 3;
    }

    /// <summary>
    /// ポートが使用可能かチェック
    /// </summary>
    public static bool IsPortAvailable(int port, string bindAddress = "0.0.0.0")
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var endpoint = new IPEndPoint(
                bindAddress == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(bindAddress), 
                port);
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
