namespace LanDrop.Utils;

/// <summary>
/// Windows のネットワークの種類（パブリック／プライベート／ドメイン）の検出
/// </summary>
/// <remarks>
/// パブリックネットワークでは Windows ファイアウォールが受信接続を既定でブロックするため、
/// 「起動しているのに他のPCからつながらない」原因になりやすい。起動時に注意を表示するために使う。
/// NetworkListManager（COM）を名前ベース（IDispatch）で呼ぶ。
/// IDispatch では GUID（アダプターID）を受け取れないため、NIC ではなくネットワーク名単位で判定する。
/// 取得できない環境では空を返す。
/// </remarks>
public static class NetworkCategoryDetector
{
    /// <summary>NLM_NETWORK_CATEGORY</summary>
    public enum Category
    {
        Public = 0,
        Private = 1,
        DomainAuthenticated = 2,
    }

    /// <summary>接続中のネットワーク</summary>
    public sealed record ConnectedNetwork(string Name, Category Category);

    private static readonly Guid NetworkListManagerClsid = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
    private const int NlmEnumNetworkConnected = 0x01;

    /// <summary>
    /// 接続中のネットワークとその種類を取得する（Windows以外・取得失敗時は空）
    /// </summary>
    public static IReadOnlyList<ConnectedNetwork> GetConnectedNetworks()
    {
        try
        {
            return GetConnectedNetworksCore();
        }
        catch
        {
            // 検出は補助的な情報なので、失敗しても起動を妨げない
            return [];
        }
    }

    /// <summary>
    /// 例外をそのまま投げる版（テスト・診断用）
    /// </summary>
    internal static List<ConnectedNetwork> GetConnectedNetworksCore()
    {
        var result = new List<ConnectedNetwork>();
        if (!OperatingSystem.IsWindows())
            return result;

        var type = Type.GetTypeFromCLSID(NetworkListManagerClsid, throwOnError: false);
        if (type == null)
            return result;

        dynamic manager = Activator.CreateInstance(type)!;
        foreach (dynamic network in manager.GetNetworks(NlmEnumNetworkConnected))
        {
            string name = network.GetName();
            int category = network.GetCategory();
            result.Add(new ConnectedNetwork(name, (Category)category));
        }

        return result;
    }

    /// <summary>
    /// 接続中のパブリックネットワーク名
    /// </summary>
    public static IReadOnlyList<string> FindPublicNetworks()
        => GetConnectedNetworks().Where(n => n.Category == Category.Public).Select(n => n.Name).ToList();

    /// <summary>
    /// パブリックネットワークに対する注意文
    /// </summary>
    public static string FormatPublicNetworkWarning(IReadOnlyList<string> networkNames)
        => $"Network \"{string.Join("\", \"", networkNames)}\" is set to Public. Windows Firewall may block " +
           "incoming connections; switch it to Private or allow lan-drop.exe in the firewall.";
}
