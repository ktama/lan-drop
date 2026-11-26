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
    
    private long _lastActivityTicks;
    private int _activeRequests;
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
        _lastActivityTicks = DateTime.UtcNow.Ticks;

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
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// リクエストの処理開始を記録する。戻り値を Dispose すると処理終了として記録する
    /// </summary>
    /// <remarks>
    /// 処理中のリクエスト（大きなファイルの転送など）がある間はアイドルとみなさない。
    /// 開始時だけ記録すると、アイドル時間より長い転送が途中で打ち切られるため。
    /// </remarks>
    public IDisposable TrackRequest()
    {
        Interlocked.Increment(ref _activeRequests);
        RecordActivity();
        return new RequestScope(this);
    }

    /// <summary>
    /// 処理中のリクエスト数
    /// </summary>
    public int ActiveRequests => Volatile.Read(ref _activeRequests);

    /// <summary>
    /// 指定時刻にアイドル状態か（処理中のリクエストがなく、最後の活動からアイドル時間が経過）
    /// </summary>
    public bool IsIdleAt(DateTime utcNow)
    {
        if (_idleMinutes <= 0 || ActiveRequests > 0)
            return false;

        var last = new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
        return (utcNow - last).TotalMinutes >= _idleMinutes;
    }

    private sealed class RequestScope(LifecycleManager owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            owner.RecordActivity();
            Interlocked.Decrement(ref owner._activeRequests);
        }
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

        if (IsIdleAt(DateTime.UtcNow))
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
