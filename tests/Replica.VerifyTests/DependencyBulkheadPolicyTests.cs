using System.Collections.Generic;
using Xunit;

namespace Replica.VerifyTests;

public sealed class DependencyBulkheadPolicyTests
{
    [Fact]
    public void TryEnter_WhenWithinLimit_ReturnsLeaseAndIncrementsInFlight()
    {
        var policy = new DependencyBulkheadPolicy(
            defaultLimit: 2,
            limits: new Dictionary<string, int> { ["pitstop"] = 2 });

        var entered = policy.TryEnter("pitstop", out var lease, out var inFlight, out var limit);

        Assert.True(entered);
        Assert.NotNull(lease);
        Assert.Equal(1, inFlight);
        Assert.Equal(2, limit);
        lease!.Dispose();
    }

    [Fact]
    public void TryEnter_WhenLimitReached_ReturnsFalse()
    {
        var policy = new DependencyBulkheadPolicy(
            defaultLimit: 1,
            limits: new Dictionary<string, int> { ["imposing"] = 1 });

        using var first = policy.TryEnter("imposing", out var lease1, out _, out _)
            ? lease1
            : null;

        var enteredSecond = policy.TryEnter("imposing", out var lease2, out var inFlight, out var limit);

        Assert.False(enteredSecond);
        Assert.Null(lease2);
        Assert.Equal(1, inFlight);
        Assert.Equal(1, limit);
    }

    [Fact]
    public void LeaseDispose_ReleasesSlotForNextRequest()
    {
        var policy = new DependencyBulkheadPolicy(defaultLimit: 1);

        Assert.True(policy.TryEnter("storage", out var lease1, out _, out _));
        Assert.False(policy.TryEnter("storage", out _, out _, out _));

        lease1!.Dispose();

        var enteredAfterDispose = policy.TryEnter("storage", out var lease2, out var inFlightAfterDispose, out var limit);
        Assert.True(enteredAfterDispose);
        Assert.Equal(1, inFlightAfterDispose);
        Assert.Equal(1, limit);
        lease2!.Dispose();
    }
}
