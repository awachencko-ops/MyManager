using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Replica.Shared.Models;
using Xunit;

namespace Replica.VerifyTests;

public sealed class LanRunCommandCoordinatorTests
{
    [Fact]
    public async Task TryStartRunsAsync_WhenLanApiDisabled_ReturnsAllOrdersWithoutGatewayCalls()
    {
        var gateway = new StubLanGateway();
        var coordinator = new LanRunCommandCoordinator(gateway);
        var orders = new List<OrderData>
        {
            new() { InternalId = "order-1", Id = "1001", StorageVersion = 1 },
            new() { InternalId = "order-2", Id = "1002", StorageVersion = 2 }
        };

        var result = await coordinator.TryStartRunsAsync(
            orders,
            useLanApi: false,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            orderDisplayIdResolver: order => order.Id);

        Assert.False(result.IsFatal);
        Assert.False(result.UsedLanApi);
        Assert.Equal(2, result.ApprovedOrders.Count);
        Assert.Empty(result.SkippedByServer);
        Assert.Equal(0, gateway.StartCalls);
    }

    [Fact]
    public async Task TryStartRunsAsync_WhenConflict_SkipsOrderAndUpdatesVersion()
    {
        var gateway = new StubLanGateway();
        gateway.StartResponses.Enqueue(LanOrderRunApiResult.Success(new SharedOrder
        {
            InternalId = "order-1",
            Status = "Processing",
            Version = 11
        }));
        gateway.StartResponses.Enqueue(LanOrderRunApiResult.Conflict("already running", currentVersion: 25));

        var coordinator = new LanRunCommandCoordinator(gateway);
        var firstOrder = new OrderData { InternalId = "order-1", Id = "1001", StorageVersion = 10 };
        var secondOrder = new OrderData { InternalId = "order-2", Id = "1002", StorageVersion = 20 };

        var result = await coordinator.TryStartRunsAsync(
            new[] { firstOrder, secondOrder },
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            orderDisplayIdResolver: order => order.Id);

        Assert.False(result.IsFatal);
        Assert.True(result.UsedLanApi);
        Assert.Single(result.ApprovedOrders);
        Assert.Same(firstOrder, result.ApprovedOrders[0]);
        Assert.Single(result.SkippedByServer);
        Assert.Contains("1002", result.SkippedByServer[0]);
        Assert.Equal(11, firstOrder.StorageVersion);
        Assert.Equal("Processing", firstOrder.Status);
        Assert.Equal(25, secondOrder.StorageVersion);
        Assert.Equal(2, gateway.StartCalls);
    }

    [Fact]
    public async Task TryStartRunsAsync_WhenFatalError_ReturnsFatalAndStopsBatch()
    {
        var gateway = new StubLanGateway();
        gateway.StartResponses.Enqueue(LanOrderRunApiResult.Unavailable("LAN API timeout"));
        gateway.StartResponses.Enqueue(LanOrderRunApiResult.Success(new SharedOrder
        {
            InternalId = "order-2",
            Version = 99
        }));

        var coordinator = new LanRunCommandCoordinator(gateway);
        var result = await coordinator.TryStartRunsAsync(
            new[]
            {
                new OrderData { InternalId = "order-1", Id = "1001", StorageVersion = 10 },
                new OrderData { InternalId = "order-2", Id = "1002", StorageVersion = 20 }
            },
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            orderDisplayIdResolver: order => order.Id);

        Assert.True(result.IsFatal);
        Assert.Contains("timeout", result.FatalError);
        Assert.Empty(result.ApprovedOrders);
        Assert.Empty(result.SkippedByServer);
        Assert.Equal(1, gateway.StartCalls);
    }

    [Fact]
    public async Task TryStopRunAsync_WhenSuccess_AppliesSnapshot()
    {
        var gateway = new StubLanGateway();
        gateway.StopResponses.Enqueue(LanOrderRunApiResult.Success(new SharedOrder
        {
            InternalId = "order-1",
            Version = 30,
            Status = "Cancelled",
            LastStatusReason = "Stopped by operator",
            LastStatusSource = "api"
        }));

        var coordinator = new LanRunCommandCoordinator(gateway);
        var order = new OrderData
        {
            InternalId = "order-1",
            Id = "1001",
            StorageVersion = 10,
            Status = "Processing"
        };

        var result = await coordinator.TryStopRunAsync(
            order,
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1");

        Assert.True(result.UsedLanApi);
        Assert.NotNull(result.ApiResult);
        Assert.True(result.ApiResult!.IsSuccess);
        Assert.Equal(30, order.StorageVersion);
        Assert.Equal("Cancelled", order.Status);
        Assert.Equal("Stopped by operator", order.LastStatusReason);
        Assert.Equal(1, gateway.StopCalls);
    }

    [Fact]
    public async Task TryStopRunAsync_WhenConflict_UpdatesVersion()
    {
        var gateway = new StubLanGateway();
        gateway.StopResponses.Enqueue(LanOrderRunApiResult.Conflict("run already finished", currentVersion: 44));

        var coordinator = new LanRunCommandCoordinator(gateway);
        var order = new OrderData
        {
            InternalId = "order-1",
            Id = "1001",
            StorageVersion = 40
        };

        var result = await coordinator.TryStopRunAsync(
            order,
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1");

        Assert.True(result.UsedLanApi);
        Assert.NotNull(result.ApiResult);
        Assert.True(result.ApiResult!.IsConflict);
        Assert.Equal(44, order.StorageVersion);
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
