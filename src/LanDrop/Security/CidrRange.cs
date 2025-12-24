using System.Net;

namespace LanDrop.Security;

/// <summary>
/// CIDRレンジを表すクラス
/// </summary>
public sealed class CidrRange
{
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

        if (!int.TryParse(parts[1], out _prefixLength) || _prefixLength < 0 || _prefixLength > 32)
            throw new ArgumentException($"Invalid prefix length: {parts[1]}");

        // IPv4のみサポート
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("Only IPv4 is supported");

        _networkBytes = ip.GetAddressBytes();
        _mask = CreateMask(_prefixLength);
        
        // ネットワークアドレスに正規化
        for (int i = 0; i < 4; i++)
        {
            _networkBytes[i] = (byte)(_networkBytes[i] & _mask[i]);
        }
    }

    /// <summary>
    /// IPアドレスがこのCIDRレンジに含まれるか判定
    /// </summary>
    public bool Contains(IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;

        var ipBytes = ip.GetAddressBytes();
        
        for (int i = 0; i < 4; i++)
        {
            if ((ipBytes[i] & _mask[i]) != _networkBytes[i])
                return false;
        }
        
        return true;
    }

    private static byte[] CreateMask(int prefixLength)
    {
        var mask = new byte[4];
        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        for (int i = 0; i < fullBytes; i++)
        {
            mask[i] = 0xFF;
        }

        if (fullBytes < 4 && remainingBits > 0)
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
