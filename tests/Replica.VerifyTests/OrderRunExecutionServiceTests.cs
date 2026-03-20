using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderRunExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenAllSuccess_CompletesWithoutErrors()
    {
        var service = new OrderRunExecutionService();
        var order = new OrderData { InternalId = "order-1", Id = "1001" };
        var sessions = new List<OrderRunStateService.RunSession>
        {
            new(order, new CancellationTokenSource())
        };

        var runCalls = 0;
        var completedCalls = 0;
        var cancelledCalls = 0;
        var failedCalls = 0;

        var result = await service.ExecuteAsync(
            sessions,
            runOrderAsync: (_, _) =>
            {
                runCalls++;
                return Task.CompletedTask;
            },
            onCancelled: _ => cancelledCalls++,
            onFailed: (_, _) => failedCalls++,
            onCompleted: _ => completedCalls++);

        Assert.Empty(result.Errors);
        Assert.Equal(1, runCalls);
        Assert.Equal(1, completedCalls);
        Assert.Equal(0, cancelledCalls);
        Assert.Equal(0, failedCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_InvokesCancelHandler()
    {
        var service = new OrderRunExecutionService();
        var order = new OrderData { InternalId = "order-2", Id = "1002" };
        var sessions = new List<OrderRunStateService.RunSession>
        {
            new(order, new CancellationTokenSource())
        };

        var completedCalls = 0;
        var cancelledCalls = 0;
        var failedCalls = 0;

        var result = await service.ExecuteAsync(
            sessions,
            runOrderAsync: (_, _) => throw new OperationCanceledException(),
            onCancelled: _ => cancelledCalls++,
            onFailed: (_, _) => failedCalls++,
            onCompleted: _ => completedCalls++);

        Assert.Empty(result.Errors);
        Assert.Equal(1, completedCalls);
        Assert.Equal(1, cancelledCalls);
        Assert.Equal(0, failedCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFailure_InvokesErrorHandlerAndReturnsErrors()
    {
        var service = new OrderRunExecutionService();
        var order = new OrderData { InternalId = "order-3", Id = "1003" };
        var sessions = new List<OrderRunStateService.RunSession>
        {
            new(order, new CancellationTokenSource())
        };

        var completedCalls = 0;
        var cancelledCalls = 0;
        var failedCalls = 0;
        Exception? capturedException = null;

        var result = await service.ExecuteAsync(
            sessions,
            runOrderAsync: (_, _) => throw new InvalidOperationException("boom"),
            onCancelled: _ => cancelledCalls++,
            onFailed: (_, ex) =>
            {
                failedCalls++;
                capturedException = ex;
            },
            onCompleted: _ => completedCalls++);

        Assert.Single(result.Errors);
        Assert.Same(order, result.Errors[0].Order);
        Assert.Contains("boom", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(capturedException);
        Assert.Equal(1, completedCalls);
        Assert.Equal(0, cancelledCalls);
        Assert.Equal(1, failedCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMixedSessions_CompletesAllAndAggregatesFailures()
    {
        var service = new OrderRunExecutionService();
        var orderSuccess = new OrderData { InternalId = "order-4", Id = "1004" };
        var orderCancelled = new OrderData { InternalId = "order-5", Id = "1005" };
        var orderFailed = new OrderData { InternalId = "order-6", Id = "1006" };
        var sessions = new List<OrderRunStateService.RunSession>
        {
            new(orderSuccess, new CancellationTokenSource()),
            new(orderCancelled, new CancellationTokenSource()),
            new(orderFailed, new CancellationTokenSource())
        };

        var completedCalls = 0;
        var cancelledCalls = 0;
        var failedCalls = 0;

        var result = await service.ExecuteAsync(
            sessions,
            runOrderAsync: (order, _) =>
            {
                if (ReferenceEquals(order, orderCancelled))
                    throw new OperationCanceledException();
                if (ReferenceEquals(order, orderFailed))
                    throw new ApplicationException("run failed");

                return Task.CompletedTask;
            },
            onCancelled: _ => cancelledCalls++,
            onFailed: (_, _) => failedCalls++,
            onCompleted: _ => completedCalls++);

        Assert.Single(result.Errors);
        Assert.Same(orderFailed, result.Errors[0].Order);
        Assert.Equal(3, completedCalls);
        Assert.Equal(1, cancelledCalls);
        Assert.Equal(1, failedCalls);
    }
}
