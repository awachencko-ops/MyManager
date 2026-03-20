using System;

namespace Replica;

public readonly record struct WorkflowTimeoutBudget(TimeSpan PitStop, TimeSpan Imposing, TimeSpan PitStopReport);

public sealed class WorkflowTimeoutBudgetPolicy
{
    private readonly double _pitStopShare;
    private readonly double _imposingShare;
    private readonly TimeSpan _minStageTimeout;
    private readonly TimeSpan _maxPitStopReportTimeout;

    public WorkflowTimeoutBudgetPolicy(
        double pitStopShare = 0.55d,
        double imposingShare = 0.45d,
        TimeSpan? minStageTimeout = null,
        TimeSpan? maxPitStopReportTimeout = null)
    {
        var normalizedPitStopShare = pitStopShare <= 0 ? 0.55d : pitStopShare;
        var normalizedImposingShare = imposingShare <= 0 ? 0.45d : imposingShare;
        var totalShare = normalizedPitStopShare + normalizedImposingShare;
        if (totalShare <= 0d)
        {
            normalizedPitStopShare = 0.55d;
            normalizedImposingShare = 0.45d;
            totalShare = 1d;
        }

        _pitStopShare = normalizedPitStopShare / totalShare;
        _imposingShare = normalizedImposingShare / totalShare;
        _minStageTimeout = minStageTimeout ?? TimeSpan.FromSeconds(45);
        _maxPitStopReportTimeout = maxPitStopReportTimeout ?? TimeSpan.FromMinutes(2);
    }

    public WorkflowTimeoutBudget Calculate(TimeSpan totalTimeout)
    {
        var effectiveTotal = totalTimeout <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(5)
            : totalTimeout;

        var pitStopTimeout = CalculateStageTimeout(effectiveTotal, _pitStopShare);
        var imposingTimeout = CalculateStageTimeout(effectiveTotal, _imposingShare);

        var pitStopReportTimeout = pitStopTimeout < _maxPitStopReportTimeout
            ? pitStopTimeout
            : _maxPitStopReportTimeout;
        if (pitStopReportTimeout <= TimeSpan.Zero)
            pitStopReportTimeout = TimeSpan.FromSeconds(20);

        return new WorkflowTimeoutBudget(pitStopTimeout, imposingTimeout, pitStopReportTimeout);
    }

    private TimeSpan CalculateStageTimeout(TimeSpan totalTimeout, double share)
    {
        var rawSeconds = totalTimeout.TotalSeconds * Math.Clamp(share, 0d, 1d);
        var minSeconds = _minStageTimeout.TotalSeconds;
        var boundedSeconds = Math.Max(rawSeconds, minSeconds);
        boundedSeconds = Math.Min(boundedSeconds, totalTimeout.TotalSeconds);
        return TimeSpan.FromSeconds(boundedSeconds);
    }
}
