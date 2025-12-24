using System.Net;

namespace LanDrop.Security;

/// <summary>
/// IPアドレスフィルター（CIDR制限）
/// </summary>
public sealed class IpFilter
{
    private readonly List<CidrRange> _allowedCidrs = new();
    private readonly bool _hasRestrictions;

    public IpFilter(string? allowedCidrsString)
    {
        if (string.IsNullOrWhiteSpace(allowedCidrsString))
        {
            _hasRestrictions = false;
            return;
        }

        var cidrs = allowedCidrsString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var cidr in cidrs)
        {
            try
            {
                _allowedCidrs.Add(new CidrRange(cidr));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Invalid CIDR in --allow: {ex.Message}");
            }
        }

        _hasRestrictions = _allowedCidrs.Count > 0;
    }

    /// <summary>
    /// IPアドレスが許可されているか判定
    /// </summary>
    public bool IsAllowed(IPAddress? remoteIp)
    {
        if (!_hasRestrictions)
            return true;

        if (remoteIp == null)
            return false;

        // IPv6マップドIPv4アドレスの処理
        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        // ループバックは常に許可
        if (IPAddress.IsLoopback(remoteIp))
            return true;

        // forループで効率的に検索（LINQ Anyより高速）
        for (int i = 0; i < _allowedCidrs.Count; i++)
        {
            if (_allowedCidrs[i].Contains(remoteIp))
                return true;
        }
        return false;
    }

    /// <summary>
    /// HttpContextからIPアドレスを取得して許可判定
    /// </summary>
    public bool IsAllowed(HttpContext context)
    {
        return IsAllowed(context.Connection.RemoteIpAddress);
    }

    /// <summary>
    /// 制限が設定されているか
    /// </summary>
    public bool HasRestrictions => _hasRestrictions;

    /// <summary>
    /// 許可されているCIDR一覧
    /// </summary>
    public IReadOnlyList<CidrRange> AllowedCidrs => _allowedCidrs.AsReadOnly();
}
