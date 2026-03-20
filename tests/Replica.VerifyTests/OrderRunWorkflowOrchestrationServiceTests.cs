using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Replica.Shared.Models;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderRunWorkflowOrchestrationServiceTests
{
    [Fact]
    public async Task PrepareStartAsync_WhenNoRunnableOrders_DoesNotCallLanGateway()
    {
        var gateway = new StubLanGateway();
        var coordinator = new LanRunCommandCoordinator(gateway);
        var service = new OrderRunWorkflowOrchestrationService(new OrderRunStateService(), coordinator);

        var selectedOrders = new[]
        {
            new OrderData
            {
                InternalId = "order-1",
                Id = string.Empty
            }
        };

        var result = await service.PrepareStartAsync(
            selectedOrders,
            runTokensByOrder: new Dictionary<string, CancellationTokenSource>(),
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            orderDisplayIdResolver: order => order.Id,
            tryRefreshSnapshotFromStorage: (_, _) => true);

        Assert.False(result.IsFatal);
        Assert.Empty(result.RunnableOrders);
        Assert.Single(result.RunPlan.OrdersWithoutNumber);
        Assert.Equal(0, gateway.StartCalls);
    }

    [Fact]
    public async Task PrepareStartAsync_WhenLanApprovedAndSnapshotRefreshFails_ReportsRefreshFailure()
    {
        var gateway = new StubLanGateway();
        gateway.StartResponses.Enqueue(LanOrderRunApiResult.Success(new SharedOrder
        {
            InternalId = "order-1",
            Status = "Processing",
            Version = 22
        }));

        var coordinator = new LanRunCommandCoordinator(gateway);
        var service = new OrderRunWorkflowOrchestrationService(new OrderRunStateService(), coordinator);
        var order = new OrderData
        {
            InternalId = "order-1",
            Id = "1001",
            StorageVersion = 10
        };

        string refreshReason = string.Empty;
        var result = await service.PrepareStartAsync(
            selectedOrders: new[] { order },
            runTokensByOrder: new Dictionary<string, CancellationTokenSource>(),
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            orderDisplayIdResolver: item => item.Id,
            tryRefreshSnapshotFromStorage: (_, reason) =>
            {
                refreshReason = reason;
                return false;
            });

        Assert.False(result.IsFatal);
        Assert.True(result.UsedLanApi);
        Assert.Single(result.RunnableOrders);
        Assert.True(result.SnapshotRefreshFailed);
        Assert.Equal("run-start", refreshReason);
        Assert.Equal(22, order.StorageVersion);
        Assert.Equal(1, gateway.StartCalls);
    }

    [Fact]
    public async Task PrepareStopAsync_WhenLocalRunSessionExists_CancelsLocalSessionAndAllowsStatusUpdate()
    {
        var gateway = new StubLanGateway();
        var coordinator = new LanRunCommandCoordinator(gateway);
        var service = new OrderRunWorkflowOrchestrationService(new OrderRunStateService(), coordinator);
        var order = new OrderData
        {
            InternalId = "order-1",
            Id = "1001",
            StorageVersion = 10
        };
        var localCts = new CancellationTokenSource();
        var runTokens = new Dictionary<string, CancellationTokenSource> { [order.InternalId] = localCts };
        var runProgress = new Dictionary<string, int> { [order.InternalId] = 5 };

        var result = await service.PrepareStopAsync(
            order: order,
            useLanApi: false,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            runTokensByOrder: runTokens,
            runProgressByOrderInternalId: runProgress,
            tryRefreshSnapshotFromStorage: (_, _) => true);

        Assert.True(result.CanProceed);
        Assert.True(result.LocalCancellationRequested);
        Assert.True(localCts.IsCancellationRequested);
        Assert.True(result.CanApplyLocalStopStatus);
        Assert.False(result.StopCommandResult.UsedLanApi);
        Assert.Empty(runTokens);
        Assert.Empty(runProgress);
        Assert.Equal(0, gateway.StopCalls);
    }

    [Fact]
    public async Task PrepareStopAsync_WhenOnlyLanSessionAndLanSuccess_AllowsLocalStatusUpdate()
    {
        var gateway = new StubLanGateway();
        gateway.StopResponses.Enqueue(LanOrderRunApiResult.Success(new SharedOrder
        {
            InternalId = "order-1",
            Status = "Cancelled",
            Version = 33
        }));

        var coordinator = new LanRunCommandCoordinator(gateway);
        var service = new OrderRunWorkflowOrchestrationService(new OrderRunStateService(), coordinator);
        var order = new OrderData
        {
            InternalId = "order-1",
            Id = "1001",
            StorageVersion = 10
        };

        string refreshReason = string.Empty;
        var result = await service.PrepareStopAsync(
            order: order,
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            runTokensByOrder: new Dictionary<string, CancellationTokenSource>(),
            runProgressByOrderInternalId: new Dictionary<string, int>(),
            tryRefreshSnapshotFromStorage: (_, reason) =>
            {
                refreshReason = reason;
                return true;
            });

        Assert.True(result.CanProceed);
        Assert.True(result.StopCommandResult.UsedLanApi);
        Assert.NotNull(result.StopCommandResult.ApiResult);
        Assert.True(result.StopCommandResult.ApiResult!.IsSuccess);
        Assert.True(result.CanApplyLocalStopStatus);
        Assert.False(result.SnapshotRefreshFailed);
        Assert.Equal("run-stop", refreshReason);
        Assert.Equal(33, order.StorageVersion);
        Assert.Equal(1, gateway.StopCalls);
    }

    private sealed class StubLanGateway : ILanOrderRunApiGateway
    {
        public Queue<LanOrderRunApiResult> StartResponses { get; } = new();
        public Queue<LanOrderRunApiResult> StopResponses { get; } = new();
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public Task<LanOrderRunApiResult> StartRunAsync(
            string apiBaseUrl,
            string orderInternalId,
            long expectedOrderVersion,
            string actor,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return Task.FromResult(
                StartResponses.Count > 0
                    ? StartResponses.Dequeue()
                    : LanOrderRunApiResult.Failed("start response queue is empty"));
        }

        public Task<LanOrderRunApiResult> StopRunAsync(
            string apiBaseUrl,
            string orderInternalId,
            long expectedOrderVersion,
            string actor,
            CancellationToken cancellationToken = default)
        {
            StopCalls++;
            return Task.FromResult(
                StopResponses.Count > 0
                    ? StopResponses.Dequeue()
                    : LanOrderRunApiResult.Failed("stop response queue is empty"));
        }
    }
}
