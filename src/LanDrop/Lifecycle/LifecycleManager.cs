namespace LanDrop.Lifecycle;

/// <summary>
/// 停止理由
/// </summary>
public enum StopReason
{
    /// <summary>TTL経過</summary>
    TtlExpired,
    
    /// <summary>アイドルタイムアウト</summary>
    IdleTimeout,
    
    /// <summary>ユーザー中断（Ctrl+C）</summary>
    UserInterrupt,
    
    /// <summary>エラー</summary>
    Error
}

/// <summary>
/// アプリケーションのライフサイクル管理
/// </summary>
public sealed class LifecycleManager : IDisposable
{
    private readonly int _ttlMinutes;
    private readonly int _idleMinutes;
    private readonly DateTime _startTime;
    private readonly Timer? _ttlTimer;
    private readonly Timer? _idleTimer;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly Action<StopReason>? _onShutdown;
    
    private DateTime _lastActivity;
    private bool _disposed;
    private StopReason? _stopReason;

    public LifecycleManager(
        int ttlMinutes, 
        int idleMinutes, 
        IHostApplicationLifetime lifetime,
        Action<StopReason>? onShutdown = null)
    {
        _ttlMinutes = ttlMinutes;
        _idleMinutes = idleMinutes;
        _lifetime = lifetime;
        _onShutdown = onShutdown;
        _startTime = DateTime.UtcNow;
        _lastActivity = DateTime.UtcNow;

        // TTLタイマー（1回のみ発火）
        if (ttlMinutes > 0)
        {
            _ttlTimer = new Timer(
                _ => Shutdown(StopReason.TtlExpired),
                null,
                TimeSpan.FromMinutes(ttlMinutes),
                Timeout.InfiniteTimeSpan);
        }

        // アイドルチェックタイマー（1分ごと）
        if (idleMinutes > 0)
        {
            _idleTimer = new Timer(
                _ => CheckIdle(),
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1));
        }
    }

    /// <summary>
    /// アクティビティを記録（アイドルタイマーのリセット）
    /// </summary>
    public void RecordActivity()
    {
        _lastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// 残りTTL（分）
    /// </summary>
    public int RemainingTtlMinutes
    {
        get
        {
            var elapsed = (DateTime.UtcNow - _startTime).TotalMinutes;
            return Math.Max(0, _ttlMinutes - (int)elapsed);
        }
    }

    /// <summary>
    /// 稼働時間（秒）
    /// </summary>
    public int UptimeSeconds => (int)(DateTime.UtcNow - _startTime).TotalSeconds;

    /// <summary>
    /// 停止理由
    /// </summary>
    public StopReason? StopReasonValue => _stopReason;

    private void CheckIdle()
    {
        if (_disposed) return;

        var idleTime = (DateTime.UtcNow - _lastActivity).TotalMinutes;
        if (idleTime >= _idleMinutes)
        {
            Shutdown(StopReason.IdleTimeout);
        }
    }

    private void Shutdown(StopReason reason)
    {
        if (_disposed || _stopReason.HasValue) return;

        _stopReason = reason;
        _onShutdown?.Invoke(reason);
        
        // アプリケーション停止をリクエスト
        _lifetime.StopApplication();
    }

    /// <summary>
    /// 手動でシャットダウンを要求
    /// </summary>
    public void RequestShutdown(StopReason reason)
    {
        Shutdown(reason);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _ttlTimer?.Dispose();
        _idleTimer?.Dispose();
    }

    /// <summary>
    /// 停止理由を文字列で取得
    /// </summary>
    public static string GetReasonDescription(StopReason reason)
    {
        return reason switch
        {
            StopReason.TtlExpired => "TTL expired",
            StopReason.IdleTimeout => "Idle timeout",
            StopReason.UserInterrupt => "User interrupt (Ctrl+C)",
            StopReason.Error => "Error",
            _ => "Unknown"
        };
    }
}
