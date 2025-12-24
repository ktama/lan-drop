using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanDrop.Logging;

/// <summary>
/// JSONLアクセスログ出力
/// </summary>
public sealed class AccessLogger : IDisposable
{
    private readonly string _logPath;
    private readonly LogRotator _rotator;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public AccessLogger(string logPath)
    {
        _logPath = logPath;
        _rotator = new LogRotator(logPath);
        _jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        // ディレクトリが存在しない場合は作成
        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// アクセスログを書き込み
    /// </summary>
    public void Log(AccessLogEntry entry)
    {
        WriteEntry(entry);
    }

    /// <summary>
    /// 起動ログを書き込み
    /// </summary>
    public void LogStart(StartLogEntry entry)
    {
        WriteEntry(entry);
    }

    /// <summary>
    /// 停止ログを書き込み
    /// </summary>
    public void LogStop(StopLogEntry entry)
    {
        WriteEntry(entry);
    }

    private void WriteEntry<T>(T entry)
    {
        if (_disposed) return;

        lock (_lock)
        {
            try
            {
                _rotator.RotateIfNeeded();
                var json = JsonSerializer.Serialize(entry, _jsonOptions);
                File.AppendAllText(_logPath, json + Environment.NewLine);
            }
            catch
            {
                // ログ書き込み失敗は無視（アプリケーション継続を優先）
            }
        }
    }

    /// <summary>
    /// リクエスト情報からログエントリを作成
    /// </summary>
    public static AccessLogEntry CreateEntry(
        HttpContext context,
        string endpoint,
        string? query,
        string? area,
        string? targetPath,
        int statusCode,
        long bytes,
        int durationMs,
        string? error = null)
    {
        var requestId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers.UserAgent.ToString();

        return new AccessLogEntry
        {
            RequestId = requestId,
            RemoteIp = remoteIp,
            Method = context.Request.Method,
            Endpoint = endpoint,
            Query = query,
            Area = area,
            TargetPath = targetPath,
            StatusCode = statusCode,
            Bytes = bytes,
            UserAgent = userAgent,
            DurationMs = durationMs,
            Error = error
        };
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
