using System;
using Xunit;

namespace Replica.VerifyTests;

public sealed class WorkflowTimeoutBudgetPolicyTests
{
    [Fact]
    public void Calculate_UsesConfiguredSharesForNormalTimeout()
    {
        var policy = new WorkflowTimeoutBudgetPolicy(
            pitStopShare: 0.6d,
            imposingShare: 0.4d,
            minStageTimeout: TimeSpan.FromSeconds(10),
            maxPitStopReportTimeout: TimeSpan.FromMinutes(2));

        var budget = policy.Calculate(TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(6), budget.PitStop);
        Assert.Equal(TimeSpan.FromMinutes(4), budget.Imposing);
        Assert.Equal(TimeSpan.FromMinutes(2), budget.PitStopReport);
    }

    [Fact]
    public void Calculate_WhenTotalTimeoutTooSmall_UsesTotalAsUpperBound()
    {
        var policy = new WorkflowTimeoutBudgetPolicy(
            pitStopShare: 0.5d,
            imposingShare: 0.5d,
            minStageTimeout: TimeSpan.FromMinutes(1),
            maxPitStopReportTimeout: TimeSpan.FromMinutes(2));

        var budget = policy.Calculate(TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(20), budget.PitStop);
        Assert.Equal(TimeSpan.FromSeconds(20), budget.Imposing);
        Assert.Equal(TimeSpan.FromSeconds(20), budget.PitStopReport);
    }

    [Fact]
    public void Calculate_WhenTotalTimeoutInvalid_UsesFallbackBudget()
    {
        var policy = new WorkflowTimeoutBudgetPolicy(
            pitStopShare: 0.55d,
            imposingShare: 0.45d,
            minStageTimeout: TimeSpan.FromSeconds(30),
            maxPitStopReportTimeout: TimeSpan.FromSeconds(50));

        var budget = policy.Calculate(TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(165), budget.PitStop);
        Assert.Equal(TimeSpan.FromSeconds(135), budget.Imposing);
        Assert.Equal(TimeSpan.FromSeconds(50), budget.PitStopReport);
    }
}
