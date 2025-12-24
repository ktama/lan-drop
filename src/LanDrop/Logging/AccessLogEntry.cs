using System.Text.Json.Serialization;

namespace LanDrop.Logging;

/// <summary>
/// アクセスログエントリ
/// </summary>
public class AccessLogEntry
{
    /// <summary>タイムスタンプ（ISO8601）</summary>
    [JsonPropertyName("ts")]
    public string Timestamp { get; set; } = DateTime.Now.ToString("o");

    /// <summary>リクエストID（8桁hex）</summary>
    [JsonPropertyName("rid")]
    public string? RequestId { get; set; }

    /// <summary>リモートIPアドレス</summary>
    [JsonPropertyName("ip")]
    public string? RemoteIp { get; set; }

    /// <summary>HTTPメソッド</summary>
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    /// <summary>エンドポイント（トークン除去後）</summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    /// <summary>クエリ文字列</summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }

    /// <summary>対象エリア（shared/uploads）</summary>
    [JsonPropertyName("area")]
    public string? Area { get; set; }

    /// <summary>対象相対パス</summary>
    [JsonPropertyName("path")]
    public string? TargetPath { get; set; }

    /// <summary>HTTPステータスコード</summary>
    [JsonPropertyName("status")]
    public int StatusCode { get; set; }

    /// <summary>送受信バイト数</summary>
    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    /// <summary>User-Agent</summary>
    [JsonPropertyName("ua")]
    public string? UserAgent { get; set; }

    /// <summary>処理時間（ms）</summary>
    [JsonPropertyName("dur")]
    public int DurationMs { get; set; }

    /// <summary>エラーメッセージ</summary>
    [JsonPropertyName("err")]
    public string? Error { get; set; }
}

/// <summary>
/// 起動ログエントリ
/// </summary>
public class StartLogEntry
{
    [JsonPropertyName("ts")]
    public string Timestamp { get; set; } = DateTime.Now.ToString("o");

    [JsonPropertyName("event")]
    public string Event { get; set; } = "start";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("root")]
    public string? Root { get; set; }

    [JsonPropertyName("bind")]
    public string? Bind { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("ttl")]
    public int TtlMinutes { get; set; }

    [JsonPropertyName("idle")]
    public int IdleMinutes { get; set; }
}

/// <summary>
/// 停止ログエントリ
/// </summary>
public class StopLogEntry
{
    [JsonPropertyName("ts")]
    public string Timestamp { get; set; } = DateTime.Now.ToString("o");

    [JsonPropertyName("event")]
    public string Event { get; set; } = "stop";

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("uptime")]
    public int UptimeSeconds { get; set; }
}
