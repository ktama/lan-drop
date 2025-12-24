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

    /// <summary>
    /// 最も可能性の高いローカルIPを取得
    /// </summary>
    public static string? GetPrimaryLocalIp()
    {
        var addresses = GetLocalIpAddresses().ToList();
        
        // 192.168.x.x を優先
        var preferred = addresses.FirstOrDefault(a => a.StartsWith("192.168."));
        if (preferred != null) return preferred;
        
        // 10.x.x.x を次に優先
        preferred = addresses.FirstOrDefault(a => a.StartsWith("10."));
        if (preferred != null) return preferred;
        
        // 172.16-31.x.x を次に優先
        preferred = addresses.FirstOrDefault(a => 
        {
            if (!a.StartsWith("172.")) return false;
            var parts = a.Split('.');
            if (parts.Length < 2) return false;
            if (int.TryParse(parts[1], out var second))
            {
                return second >= 16 && second <= 31;
            }
            return false;
        });
        if (preferred != null) return preferred;

        // それ以外は最初のもの
        return addresses.FirstOrDefault();
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
