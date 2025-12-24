namespace LanDrop.Config;

/// <summary>
/// アプリケーション設定
/// </summary>
public sealed class AppConfig
{
    /// <summary>共有ルートディレクトリ（必須）</summary>
    public required string RootDir { get; set; }

    /// <summary>ポート番号（null=自動探索 8000-8100）</summary>
    public int? Port { get; set; }

    /// <summary>バインドアドレス</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>TTL（分）</summary>
    public int TtlMinutes { get; set; } = 60;

    /// <summary>アイドルタイムアウト（分）</summary>
    public int IdleMinutes { get; set; } = 30;

    /// <summary>トークン（null=自動生成）</summary>
    public string? Token { get; set; }

    /// <summary>IP制限（CIDR表記、カンマ区切り）</summary>
    public string? AllowedCidrs { get; set; }

    /// <summary>最大アップロードサイズ（MB）</summary>
    public int MaxUploadMb { get; set; } = 200;

    /// <summary>ログファイルパス</summary>
    public string? LogPath { get; set; }

    /// <summary>起動後にブラウザを開く</summary>
    public bool OpenBrowser { get; set; }

    /// <summary>読み取り専用モード</summary>
    public bool ReadOnly { get; set; }

    /// <summary>アップロード先ディレクトリ名</summary>
    public const string UploadsDirName = "_uploads";

    /// <summary>ログディレクトリ名</summary>
    public const string LogDirName = "log";

    /// <summary>アップロード先フルパス</summary>
    public string UploadsDir => Path.Combine(RootDir, UploadsDirName);

    /// <summary>ログファイルの実効パス（実行ファイルと同じディレクトリにlogフォルダ）</summary>
    public string EffectiveLogPath => LogPath ?? Path.Combine(AppContext.BaseDirectory, LogDirName, "lan-drop.log");

    /// <summary>最大アップロードサイズ（バイト）</summary>
    public long MaxUploadBytes => MaxUploadMb * 1024L * 1024L;
}
