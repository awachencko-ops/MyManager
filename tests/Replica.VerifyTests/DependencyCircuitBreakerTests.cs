using System;
using Xunit;

namespace Replica.VerifyTests;

public sealed class DependencyCircuitBreakerTests
{
    [Fact]
    public void GetLevel_WhenNoFailures_ReturnsHealthy()
    {
        var breaker = new DependencyCircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(10));
        var now = new DateTime(2026, 3, 20, 2, 0, 0, DateTimeKind.Utc);

        Assert.Equal(DependencyHealthLevel.Healthy, breaker.GetLevel(now));
        Assert.True(breaker.TryAllow(now, out var retryAfter));
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    [Fact]
    public void RecordFailure_BeforeThreshold_StaysDegradedAndAllows()
    {
        var breaker = new DependencyCircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(10));
        var now = new DateTime(2026, 3, 20, 2, 0, 0, DateTimeKind.Utc);

        breaker.RecordFailure(now);

        Assert.Equal(DependencyHealthLevel.Degraded, breaker.GetLevel(now));
        Assert.True(breaker.TryAllow(now, out _));
    }

    [Fact]
    public void RecordFailure_OnThreshold_OpensCircuitAndBlocksUntilWindowEnds()
    {
        var breaker = new DependencyCircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(10));
        var now = new DateTime(2026, 3, 20, 2, 0, 0, DateTimeKind.Utc);

        breaker.RecordFailure(now);
        breaker.RecordFailure(now.AddSeconds(1));
        breaker.RecordFailure(now.AddSeconds(2));

        var checkAt = now.AddSeconds(3);
        Assert.Equal(DependencyHealthLevel.Unavailable, breaker.GetLevel(checkAt));
        Assert.False(breaker.TryAllow(checkAt, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);

        var afterWindow = now.AddSeconds(20);
        Assert.True(breaker.TryAllow(afterWindow, out _));
        Assert.Equal(DependencyHealthLevel.Healthy, breaker.GetLevel(afterWindow));
    }

    [Fact]
    public void RecordSuccess_ResetsBreakerStateToHealthy()
    {
        var breaker = new DependencyCircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromSeconds(5));
        var now = new DateTime(2026, 3, 20, 2, 0, 0, DateTimeKind.Utc);

        breaker.RecordFailure(now);
        Assert.Equal(DependencyHealthLevel.Degraded, breaker.GetLevel(now));

        breaker.RecordSuccess();
        Assert.Equal(DependencyHealthLevel.Healthy, breaker.GetLevel(now));
        Assert.True(breaker.TryAllow(now, out _));
    }
}
