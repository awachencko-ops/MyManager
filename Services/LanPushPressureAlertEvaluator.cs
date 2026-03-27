namespace Replica;

internal readonly record struct LanPushPressureAlertDecision(
    bool ShouldAlert,
    double CoalescedRate,
    double ThrottledRate);

internal static class LanPushPressureAlertEvaluator
{
    public static LanPushPressureAlertDecision Evaluate(
        long eventsReceived,
        long refreshApplied,
        long coalescedEvents,
        long throttledDelays,
        int minEvents,
        double coalescedRateThreshold,
        double throttledRateThreshold,
        DateTime nowUtc,
        DateTime lastAlertAtUtc,
        TimeSpan cooldown)
    {
        var normalizedEventsReceived = Math.Max(0L, eventsReceived);
        var normalizedRefreshApplied = Math.Max(0L, refreshApplied);
        var normalizedCoalescedEvents = Math.Max(0L, coalescedEvents);
        var normalizedThrottledDelays = Math.Max(0L, throttledDelays);
        var normalizedMinEvents = minEvents <= 0 ? 1 : minEvents;
        var normalizedCoalescedThreshold = NormalizeRateThreshold(coalescedRateThreshold);
        var normalizedThrottledThreshold = NormalizeRateThreshold(throttledRateThreshold);
        var normalizedCooldown = cooldown < TimeSpan.Zero ? TimeSpan.Zero : cooldown;

        var coalescedRate = normalizedEventsReceived > 0
            ? (double)normalizedCoalescedEvents / normalizedEventsReceived
            : 0d;
        var throttleBase = normalizedRefreshApplied > 0
            ? normalizedRefreshApplied
            : normalizedEventsReceived;
        var throttledRate = throttleBase > 0
            ? (double)normalizedThrottledDelays / throttleBase
            : 0d;

        if (normalizedEventsReceived < normalizedMinEvents)
            return new LanPushPressureAlertDecision(false, coalescedRate, throttledRate);

        var pressureExceeded = coalescedRate >= normalizedCoalescedThreshold
                               || throttledRate >= normalizedThrottledThreshold;
        if (!pressureExceeded)
            return new LanPushPressureAlertDecision(false, coalescedRate, throttledRate);

        var normalizedNowUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
        var normalizedLastAlertAtUtc = lastAlertAtUtc.Kind == DateTimeKind.Utc
            ? lastAlertAtUtc
            : lastAlertAtUtc.ToUniversalTime();
        if (normalizedCooldown > TimeSpan.Zero
            && normalizedLastAlertAtUtc > DateTime.MinValue
            && (normalizedNowUtc - normalizedLastAlertAtUtc) < normalizedCooldown)
        {
            return new LanPushPressureAlertDecision(false, coalescedRate, throttledRate);
        }

        return new LanPushPressureAlertDecision(true, coalescedRate, throttledRate);
    }

    public static bool IsHintActive(DateTime nowUtc, DateTime lastAlertAtUtc, TimeSpan activeWindow)
    {
        if (lastAlertAtUtc <= DateTime.MinValue)
            return false;

        var normalizedWindow = activeWindow < TimeSpan.Zero ? TimeSpan.Zero : activeWindow;
        if (normalizedWindow <= TimeSpan.Zero)
            return false;

        var normalizedNowUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
        var normalizedLastAlertAtUtc = lastAlertAtUtc.Kind == DateTimeKind.Utc
            ? lastAlertAtUtc
            : lastAlertAtUtc.ToUniversalTime();

        return (normalizedNowUtc - normalizedLastAlertAtUtc) <= normalizedWindow;
    }

    private static double NormalizeRateThreshold(double threshold)
    {
        if (double.IsNaN(threshold) || double.IsInfinity(threshold))
            return 1d;

        if (threshold < 0d)
            return 0d;
        if (threshold > 1d)
            return 1d;

        return threshold;
    }
}
