using System.Net;
using System.Net.Sockets;

namespace LanDrop.Security;

/// <summary>
/// CIDRレンジを表すクラス（IPv4 / IPv6）
/// </summary>
public sealed class CidrRange
{
    private readonly AddressFamily _family;
    private readonly byte[] _networkBytes;
    private readonly int _prefixLength;
    private readonly byte[] _mask;

    public CidrRange(string cidr)
    {
        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid CIDR notation: {cidr}");

        if (!IPAddress.TryParse(parts[0], out var ip))
            throw new ArgumentException($"Invalid IP address: {parts[0]}");

        // IPv4射影アドレス（::ffff:a.b.c.d）はIPv4として扱う
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            throw new ArgumentException($"Unsupported address family: {parts[0]}");

        var maxPrefix = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (!int.TryParse(parts[1], out _prefixLength) || _prefixLength < 0 || _prefixLength > maxPrefix)
            throw new ArgumentException($"Invalid prefix length: {parts[1]}");

        _family = ip.AddressFamily;
        _networkBytes = ip.GetAddressBytes();
        _mask = CreateMask(_prefixLength, _networkBytes.Length);

        // ネットワークアドレスに正規化
        for (int i = 0; i < _networkBytes.Length; i++)
        {
            _networkBytes[i] = (byte)(_networkBytes[i] & _mask[i]);
        }
    }

    /// <summary>
    /// IPアドレスがこのCIDRレンジに含まれるか判定
    /// </summary>
    public bool Contains(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily != _family)
            return false;

        var ipBytes = ip.GetAddressBytes();

        for (int i = 0; i < ipBytes.Length; i++)
        {
            if ((ipBytes[i] & _mask[i]) != _networkBytes[i])
                return false;
        }

        return true;
    }

    private static byte[] CreateMask(int prefixLength, int byteLength)
    {
        var mask = new byte[byteLength];
        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        for (int i = 0; i < fullBytes; i++)
        {
            mask[i] = 0xFF;
        }

        if (fullBytes < byteLength && remainingBits > 0)
        {
            mask[fullBytes] = (byte)(0xFF << (8 - remainingBits));
        }

        return mask;
    }

    public override string ToString()
    {
        var ip = new IPAddress(_networkBytes);
        return $"{ip}/{_prefixLength}";
    }
}
