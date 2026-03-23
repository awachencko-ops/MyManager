namespace Replica.VerifyTests;

public sealed class LanApiConnectionStatusEvaluatorTests
{
    [Fact]
    public void IsDisconnected_WhenApiIsReachable_ReturnsFalseEvenIfReadyWillBeMissing()
    {
        var disconnected = LanApiConnectionStatusEvaluator.IsDisconnected(
            apiReachable: true,
            consecutiveFailures: 5,
            failureThreshold: 3);

        Assert.False(disconnected);
    }

    [Fact]
    public void IsDisconnected_WhenFailuresBelowThreshold_ReturnsFalse()
    {
        var disconnected = LanApiConnectionStatusEvaluator.IsDisconnected(
            apiReachable: false,
            consecutiveFailures: 2,
            failureThreshold: 3);

        Assert.False(disconnected);
    }

    [Fact]
    public void IsDisconnected_WhenFailuresReachThreshold_ReturnsTrue()
    {
        var disconnected = LanApiConnectionStatusEvaluator.IsDisconnected(
            apiReachable: false,
            consecutiveFailures: 3,
            failureThreshold: 3);

        Assert.True(disconnected);
    }

    [Fact]
    public void IsTransientFailure_WhenFailuresBelowThreshold_ReturnsTrue()
    {
        var transientFailure = LanApiConnectionStatusEvaluator.IsTransientFailure(
            apiReachable: false,
            consecutiveFailures: 2,
            failureThreshold: 3);

        Assert.True(transientFailure);
    }

    [Fact]
    public void IsDegraded_WhenApiReachableButReadyMissing_ReturnsTrue()
    {
        var degraded = LanApiConnectionStatusEvaluator.IsDegraded(
            apiReachable: true,
            isReady: false,
            isDegraded: false,
            sloHealthy: true,
            dependencyHealthLevel: DependencyHealthLevel.Healthy);

        Assert.True(degraded);
    }

    [Fact]
    public void IsDegraded_WhenApiUnreachable_ReturnsFalse()
    {
        var degraded = LanApiConnectionStatusEvaluator.IsDegraded(
            apiReachable: false,
            isReady: false,
            isDegraded: true,
            sloHealthy: false,
            dependencyHealthLevel: DependencyHealthLevel.Unavailable);

        Assert.False(degraded);
    }
}
