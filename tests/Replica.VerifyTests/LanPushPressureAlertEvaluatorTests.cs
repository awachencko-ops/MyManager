namespace Replica.VerifyTests;

public sealed class LanPushPressureAlertEvaluatorTests
{
    [Fact]
    public void Evaluate_WhenEventsBelowMinThreshold_DoesNotAlert()
    {
        var decision = LanPushPressureAlertEvaluator.Evaluate(
            eventsReceived: 29,
            refreshApplied: 10,
            coalescedEvents: 20,
            throttledDelays: 8,
            minEvents: 30,
            coalescedRateThreshold: 0.55,
            throttledRateThreshold: 0.40,
            nowUtc: new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc),
            lastAlertAtUtc: DateTime.MinValue,
            cooldown: TimeSpan.FromMinutes(1));

        Assert.False(decision.ShouldAlert);
    }

    [Fact]
    public void Evaluate_WhenCoalescedRateMeetsThreshold_AndNoCooldown_Alerts()
    {
        var decision = LanPushPressureAlertEvaluator.Evaluate(
            eventsReceived: 100,
            refreshApplied: 80,
            coalescedEvents: 55,
            throttledDelays: 0,
            minEvents: 30,
            coalescedRateThreshold: 0.55,
            throttledRateThreshold: 0.40,
            nowUtc: new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc),
            lastAlertAtUtc: DateTime.MinValue,
            cooldown: TimeSpan.FromMinutes(1));

        Assert.True(decision.ShouldAlert);
        Assert.Equal(0.55d, decision.CoalescedRate, 3);
    }

    [Fact]
    public void Evaluate_WhenThrottledRateMeetsThreshold_AndNoCooldown_Alerts()
    {
        var decision = LanPushPressureAlertEvaluator.Evaluate(
            eventsReceived: 100,
            refreshApplied: 80,
            coalescedEvents: 0,
            throttledDelays: 32,
            minEvents: 30,
            coalescedRateThreshold: 0.55,
            throttledRateThreshold: 0.40,
            nowUtc: new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc),
            lastAlertAtUtc: DateTime.MinValue,
            cooldown: TimeSpan.FromMinutes(1));

        Assert.True(decision.ShouldAlert);
        Assert.Equal(0.40d, decision.ThrottledRate, 3);
    }

    [Fact]
    public void Evaluate_WhenRatesBelowThreshold_DoesNotAlert()
    {
        var decision = LanPushPressureAlertEvaluator.Evaluate(
            eventsReceived: 100,
            refreshApplied: 80,
            coalescedEvents: 20,
            throttledDelays: 10,
            minEvents: 30,
            coalescedRateThreshold: 0.55,
            throttledRateThreshold: 0.40,
            nowUtc: new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc),
            lastAlertAtUtc: DateTime.MinValue,
            cooldown: TimeSpan.FromMinutes(1));

        Assert.False(decision.ShouldAlert);
    }

    [Fact]
    public void Evaluate_WhenCooldownIsActive_DoesNotAlert()
    {
        var nowUtc = new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc);
        var lastAlertAtUtc = nowUtc.AddSeconds(-30);

        var decision = LanPushPressureAlertEvaluator.Evaluate(
            eventsReceived: 100,
            refreshApplied: 80,
            coalescedEvents: 70,
            throttledDelays: 50,
            minEvents: 30,
            coalescedRateThreshold: 0.55,
            throttledRateThreshold: 0.40,
            nowUtc: nowUtc,
            lastAlertAtUtc: lastAlertAtUtc,
            cooldown: TimeSpan.FromMinutes(1));

        Assert.False(decision.ShouldAlert);
    }

    [Fact]
    public void Evaluate_WhenCooldownBoundaryReached_Alerts()
    {
        var nowUtc = new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc);
        var lastAlertAtUtc = nowUtc.AddMinutes(-1);

        var decision = LanPushPressureAlertEvaluator.Evaluate(
            eventsReceived: 100,
            refreshApplied: 80,
            coalescedEvents: 70,
            throttledDelays: 50,
            minEvents: 30,
            coalescedRateThreshold: 0.55,
            throttledRateThreshold: 0.40,
            nowUtc: nowUtc,
            lastAlertAtUtc: lastAlertAtUtc,
            cooldown: TimeSpan.FromMinutes(1));

        Assert.True(decision.ShouldAlert);
    }

    [Fact]
    public void IsHintActive_WhenLastAlertMissing_ReturnsFalse()
    {
        var isActive = LanPushPressureAlertEvaluator.IsHintActive(
            nowUtc: new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc),
            lastAlertAtUtc: DateTime.MinValue,
            activeWindow: TimeSpan.FromMinutes(5));

        Assert.False(isActive);
    }

    [Fact]
    public void IsHintActive_WhenLastAlertWithinWindow_ReturnsTrue()
    {
        var nowUtc = new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc);
        var isActive = LanPushPressureAlertEvaluator.IsHintActive(
            nowUtc: nowUtc,
            lastAlertAtUtc: nowUtc.AddMinutes(-4),
            activeWindow: TimeSpan.FromMinutes(5));

        Assert.True(isActive);
    }

    [Fact]
    public void IsHintActive_WhenLastAlertOutsideWindow_ReturnsFalse()
    {
        var nowUtc = new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc);
        var isActive = LanPushPressureAlertEvaluator.IsHintActive(
            nowUtc: nowUtc,
            lastAlertAtUtc: nowUtc.AddMinutes(-6),
            activeWindow: TimeSpan.FromMinutes(5));

        Assert.False(isActive);
    }
}
