using System;

namespace Replica;

public enum DependencyHealthLevel
{
    Healthy = 0,
    Degraded = 1,
    Unavailable = 2
}

public sealed record DependencyHealthSignal(
    string DependencyName,
    DependencyHealthLevel Level,
    string Reason,
    DateTime TimestampUtc);

public sealed class DependencyCircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private int _consecutiveFailures;
    private DateTime _openUntilUtc;

    public DependencyCircuitBreaker(int failureThreshold, TimeSpan openDuration)
    {
        _failureThreshold = Math.Max(1, failureThreshold);
        _openDuration = openDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : openDuration;
        _openUntilUtc = DateTime.MinValue;
    }

    public bool TryAllow(DateTime nowUtc, out TimeSpan retryAfter)
    {
        if (nowUtc < _openUntilUtc)
        {
            retryAfter = _openUntilUtc - nowUtc;
            return false;
        }

        retryAfter = TimeSpan.Zero;
        return true;
    }

    public void RecordSuccess()
    {
        _consecutiveFailures = 0;
        _openUntilUtc = DateTime.MinValue;
    }

    public void RecordFailure(DateTime nowUtc)
    {
        _consecutiveFailures++;
        if (_consecutiveFailures < _failureThreshold)
            return;

        _openUntilUtc = nowUtc + _openDuration;
        _consecutiveFailures = 0;
    }

    public DependencyHealthLevel GetLevel(DateTime nowUtc)
    {
        if (nowUtc < _openUntilUtc)
            return DependencyHealthLevel.Unavailable;

        return _consecutiveFailures > 0
            ? DependencyHealthLevel.Degraded
            : DependencyHealthLevel.Healthy;
    }
}
