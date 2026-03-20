using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderRunStateServiceTests
{
    [Fact]
    public void BuildRunPlan_SplitsRunnableAndSkippedOrders()
    {
        var service = new OrderRunStateService();
        var orderRunnable = new OrderData { InternalId = "run-1", Id = "1001" };
        var orderWithoutNumber = new OrderData { InternalId = "skip-no-number", Id = string.Empty };
        var orderAlreadyRunning = new OrderData { InternalId = "skip-running", Id = "1002" };

        var runningTokens = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal)
        {
            [orderAlreadyRunning.InternalId] = new()
        };

        var plan = service.BuildRunPlan(
            new List<OrderData> { orderRunnable, orderWithoutNumber, orderAlreadyRunning },
            runningTokens);

        Assert.Single(plan.RunnableOrders);
        Assert.Equal(orderRunnable.InternalId, plan.RunnableOrders[0].InternalId);
        Assert.Single(plan.OrdersWithoutNumber);
        Assert.Equal(orderWithoutNumber.InternalId, plan.OrdersWithoutNumber[0].InternalId);
        Assert.Single(plan.AlreadyRunningOrders);
        Assert.Equal(orderAlreadyRunning.InternalId, plan.AlreadyRunningOrders[0].InternalId);
    }

    [Fact]
    public void BuildRunPlan_WhenLocalRunStateDisabled_DoesNotSkipByLocalTokens()
    {
        var service = new OrderRunStateService();
        var order = new OrderData { InternalId = "run-1", Id = "1001" };
        var runningTokens = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal)
        {
            [order.InternalId] = new()
        };

        var plan = service.BuildRunPlan(
            new List<OrderData> { order },
            runningTokens,
            useLocalRunState: false);

        Assert.Single(plan.RunnableOrders);
        Assert.Empty(plan.AlreadyRunningOrders);
    }

    [Fact]
    public void BeginRunSessions_InitializesTokensAndProgress()
    {
        var service = new OrderRunStateService();
        var orders = new List<OrderData>
        {
            new() { InternalId = "a", Id = "1001" },
            new() { InternalId = "b", Id = "1002" }
        };

        var runTokens = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var runProgress = new Dictionary<string, int>(StringComparer.Ordinal);

        var sessions = service.BeginRunSessions(orders, runTokens, runProgress);

        Assert.Equal(2, sessions.Count);
        Assert.Equal(2, runTokens.Count);
        Assert.Equal(2, runProgress.Count);
        Assert.All(orders, order => Assert.True(runProgress.ContainsKey(order.InternalId)));

        service.CompleteRunSession(orders[0], runTokens, runProgress);

        Assert.Single(runTokens);
        Assert.Single(runProgress);
        Assert.DoesNotContain("a", runTokens.Keys);
    }

    [Fact]
    public void TryStopOrder_RemovesRunStateAndReturnsCts()
    {
        var service = new OrderRunStateService();
        var order = new OrderData { InternalId = "run-1", Id = "1001" };

        var cts = new CancellationTokenSource();
        var runTokens = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal)
        {
            [order.InternalId] = cts
        };
        var runProgress = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [order.InternalId] = 42
        };

        var stopped = service.TryStopOrder(order, runTokens, runProgress, out var returnedCts);

        Assert.True(stopped);
        Assert.Same(cts, returnedCts);
        Assert.Empty(runTokens);
        Assert.Empty(runProgress);

        var stoppedAgain = service.TryStopOrder(order, runTokens, runProgress, out _);
        Assert.False(stoppedAgain);
    }

    [Fact]
    public void BuildStopPlan_WhenLanApiEnabled_ProceedsWithoutLocalSession()
    {
        var service = new OrderRunStateService();
        var order = new OrderData { InternalId = "run-1", Id = "1001" };

        var runTokens = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var runProgress = new Dictionary<string, int>(StringComparer.Ordinal);

        var plan = service.BuildStopPlan(
            order,
            useLanApi: true,
            runTokens,
            runProgress);

        Assert.True(plan.CanProceed);
        Assert.False(plan.HasLocalRunSession);
        Assert.True(plan.ShouldSendServerStop);
        Assert.Null(plan.LocalCancellationTokenSource);
    }
}
