namespace LanDrop.Logging;

/// <summary>
/// ログファイルのローテーション
/// </summary>
public sealed class LogRotator
{
    private readonly string _logPath;
    private readonly long _maxSizeBytes;
    private readonly int _maxGenerations;
    private readonly object _lock = new();

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logPath">ログファイルパス</param>
    /// <param name="maxSizeMb">ローテーション閾値（MB）</param>
    /// <param name="maxGenerations">最大世代数</param>
    public LogRotator(string logPath, int maxSizeMb = 10, int maxGenerations = 5)
    {
        _logPath = logPath;
        _maxSizeBytes = maxSizeMb * 1024L * 1024L;
        _maxGenerations = maxGenerations;
    }

    /// <summary>
    /// 必要に応じてローテーションを実行
    /// </summary>
    /// <returns>ローテーションが実行されたらtrue</returns>
    public bool RotateIfNeeded()
    {
        lock (_lock)
        {
            try
            {
                var fileInfo = new FileInfo(_logPath);
                if (!fileInfo.Exists || fileInfo.Length < _maxSizeBytes)
                {
                    return false;
                }

                // 最古の世代を削除
                var oldestPath = $"{_logPath}.{_maxGenerations}";
                if (File.Exists(oldestPath))
                {
                    File.Delete(oldestPath);
                }

                // 世代をシフト
                for (int i = _maxGenerations - 1; i >= 1; i--)
                {
                    var src = $"{_logPath}.{i}";
                    var dst = $"{_logPath}.{i + 1}";
                    if (File.Exists(src))
                    {
                        File.Move(src, dst);
                    }
                }

                // 現在のログを.1に退避
                File.Move(_logPath, $"{_logPath}.1");

                return true;
            }
            catch
            {
                // ローテーション失敗は無視（ログ出力継続を優先）
                return false;
            }
        }
    }

    /// <summary>
    /// 現在のログファイルサイズを取得
    /// </summary>
    public long GetCurrentSize()
    {
        try
        {
            var fileInfo = new FileInfo(_logPath);
            return fileInfo.Exists ? fileInfo.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// ローテーション閾値（バイト）
    /// </summary>
    public long MaxSizeBytes => _maxSizeBytes;

    /// <summary>
    /// 最大世代数
    /// </summary>
    public int MaxGenerations => _maxGenerations;
}
