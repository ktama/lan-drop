using LanDrop.Lifecycle;
using Microsoft.Extensions.Hosting;

namespace LanDrop.Tests;

/// <summary>
/// LifecycleManager のテスト
/// - 処理中のリクエストがある間はアイドルとみなさない
/// </summary>
public class LifecycleManagerTests
{
    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public int StopCount { get; private set; }
        public void StopApplication() => StopCount++;
    }

    private static LifecycleManager Create(int idleMinutes = 30)
        => new(ttlMinutes: 60, idleMinutes: idleMinutes, new FakeLifetime());

    [Fact]
    public void NoActivity_BecomesIdleAfterIdleMinutes()
    {
        using var lm = Create(idleMinutes: 30);

        Assert.False(lm.IsIdleAt(DateTime.UtcNow.AddMinutes(29)));
        Assert.True(lm.IsIdleAt(DateTime.UtcNow.AddMinutes(31)));
    }

    [Fact]
    public void ActiveRequest_IsNeverIdle()
    {
        // 再現: 開始時刻だけで判定していたため、30分を超える転送中に停止していた
        using var lm = Create(idleMinutes: 30);

        using (lm.TrackRequest())
        {
            Assert.Equal(1, lm.ActiveRequests);
            Assert.False(lm.IsIdleAt(DateTime.UtcNow.AddMinutes(120)));
        }

        Assert.Equal(0, lm.ActiveRequests);
    }

    [Fact]
    public void AfterRequestEnds_IdleCountsFromTheEnd()
    {
        using var lm = Create(idleMinutes: 30);

        var scope = lm.TrackRequest();
        scope.Dispose();

        Assert.False(lm.IsIdleAt(DateTime.UtcNow.AddMinutes(29)));
        Assert.True(lm.IsIdleAt(DateTime.UtcNow.AddMinutes(31)));
    }

    [Fact]
    public void DoubleDispose_DoesNotUnderflow()
    {
        using var lm = Create();

        var scope = lm.TrackRequest();
        scope.Dispose();
        scope.Dispose();

        Assert.Equal(0, lm.ActiveRequests);
    }

    [Fact]
    public void ConcurrentRequests_AreCounted()
    {
        using var lm = Create();

        var scopes = Enumerable.Range(0, 100).AsParallel().Select(_ => lm.TrackRequest()).ToList();
        Assert.Equal(100, lm.ActiveRequests);

        scopes.AsParallel().ForAll(s => s.Dispose());
        Assert.Equal(0, lm.ActiveRequests);
    }
}
