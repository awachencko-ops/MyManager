using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderRunCommandServiceTests
{
    [Fact]
    public async Task PrepareAndBeginAsync_WhenNoRunnableOrders_ReturnsNoRunnable()
    {
        var gateway = new StubLanGateway();
        var service = CreateService(gateway, out _);
        var selectedOrders = new[]
        {
            new OrderData
            {
                InternalId = "order-1",
                Id = string.Empty
            }
        };

        var result = await service.PrepareAndBeginAsync(
            selectedOrders,
            runTokensByOrder: new Dictionary<string, CancellationTokenSource>(),
            runProgressByOrderInternalId: new Dictionary<string, int>(),
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            orderDisplayIdResolver: order => order.Id,
            tryRefreshSnapshotFromStorage: (_, _) => true);

        Assert.Equal(OrderRunStartPhaseStatus.NoRunnable, result.Status);
        Assert.NotEmpty(result.NoRunnableDetails);
        Assert.Empty(result.RunSessions);
        Assert.Equal(0, gateway.StartCalls);
    }

    [Fact]
    public async Task PrepareAndBeginAsync_WhenLanRejectsAllOrders_ReturnsServerRejected()
    {
        var gateway = new StubLanGateway();
        gateway.StartResponses.Enqueue(LanOrderRunApiResult.Conflict("version mismatch", 42));
        var service = CreateService(gateway, out _);

        var order = new OrderData
        {
            InternalId = "order-1",
            Id = "1001",
            StorageVersion = 10
        };

        var result = await service.PrepareAndBeginAsync(
            selectedOrders: new[] { order },
            runTokensByOrder: new Dictionary<string, CancellationTokenSource>(),
            runProgressByOrderInternalId: new Dictionary<string, int>(),
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            orderDisplayIdResolver: item => item.Id,
            tryRefreshSnapshotFromStorage: (_, _) => true);

        Assert.Equal(OrderRunStartPhaseStatus.ServerRejected, result.Status);
        Assert.Empty(result.RunSessions);
        Assert.Single(result.Preparation.SkippedByServer);
        Assert.Equal(42, order.StorageVersion);
        Assert.Equal(1, gateway.StartCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSessionCompleted_CleansRunStateAndCallsCompletedCallback()
    {
        var gateway = new StubLanGateway();
        var service = CreateService(gateway, out var runStateService);
        var order = new OrderData
        {
            InternalId = "order-1",
            Id = "1001"
        };
        var runTokens = new Dictionary<string, CancellationTokenSource>();
        var runProgress = new Dictionary<string, int>();

        var startResult = await service.PrepareAndBeginAsync(
            selectedOrders: new[] { order },
            runTokensByOrder: runTokens,
            runProgressByOrderInternalId: runProgress,
            useLanApi: false,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            orderDisplayIdResolver: item => item.Id,
            tryRefreshSnapshotFromStorage: (_, _) => true);

        Assert.Equal(OrderRunStartPhaseStatus.ReadyToExecute, startResult.Status);
        Assert.Single(startResult.RunSessions);
        Assert.True(runTokens.ContainsKey(order.InternalId));
        Assert.True(runProgress.ContainsKey(order.InternalId));

        var completedCalls = 0;
        var cancelledCalls = 0;
        var failedCalls = 0;

        var executionResult = await service.ExecuteAsync(
            startResult.RunSessions,
            runTokensByOrder: runTokens,
            runProgressByOrderInternalId: runProgress,
            runOrderAsync: (_, _) => Task.CompletedTask,
            onCancelled: _ => cancelledCalls++,
            onFailed: (_, _) => failedCalls++,
            onCompleted: _ => completedCalls++);

        Assert.Empty(executionResult.Errors);
        Assert.Equal(1, completedCalls);
        Assert.Equal(0, cancelledCalls);
        Assert.Equal(0, failedCalls);
        Assert.Empty(runTokens);
        Assert.Empty(runProgress);

        // Guard against accidental regression where service starts using another OrderRunStateService instance.
        Assert.Empty(runStateService.BuildRunPlan(new[] { order }, runTokens, useLocalRunState: true).AlreadyRunningOrders);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRunFails_ReportsErrorAndStillCleansRunState()
    {
        var gateway = new StubLanGateway();
        var service = CreateService(gateway, out _);
        var order = new OrderData
        {
            InternalId = "order-2",
            Id = "1002"
        };
        var runTokens = new Dictionary<string, CancellationTokenSource>();
        var runProgress = new Dictionary<string, int>();

        var startResult = await service.PrepareAndBeginAsync(
            selectedOrders: new[] { order },
            runTokensByOrder: runTokens,
            runProgressByOrderInternalId: runProgress,
            useLanApi: false,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            orderDisplayIdResolver: item => item.Id,
            tryRefreshSnapshotFromStorage: (_, _) => true);

        var failedCalls = 0;
        var completedCalls = 0;

        var executionResult = await service.ExecuteAsync(
            startResult.RunSessions,
            runTokensByOrder: runTokens,
            runProgressByOrderInternalId: runProgress,
            runOrderAsync: (_, _) => throw new InvalidOperationException("boom"),
            onCancelled: _ => { },
            onFailed: (_, ex) =>
            {
                Assert.Contains("boom", ex.Message, StringComparison.OrdinalIgnoreCase);
                failedCalls++;
            },
            onCompleted: _ => completedCalls++);

        Assert.Single(executionResult.Errors);
        Assert.Equal(1, failedCalls);
        Assert.Equal(1, completedCalls);
        Assert.Empty(runTokens);
        Assert.Empty(runProgress);
    }

    [Fact]
    public async Task ExecuteStopAsync_WhenOrderIsNotRunning_ReturnsNotRunning()
    {
        var gateway = new StubLanGateway();
        var service = CreateService(gateway, out _);
        var order = new OrderData
        {
            InternalId = "order-stop-1",
            Id = "2001"
        };

        var result = await service.ExecuteStopAsync(
            order: order,
            useLanApi: false,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            runTokensByOrder: new Dictionary<string, CancellationTokenSource>(),
            runProgressByOrderInternalId: new Dictionary<string, int>(),
            tryRefreshSnapshotFromStorage: (_, _) => true,
            applyLocalStopStatus: _ => throw new InvalidOperationException("should not be called"));

        Assert.Equal(OrderRunStopPhaseStatus.NotRunning, result.Status);
        Assert.False(result.Preparation.CanProceed);
        Assert.Equal(0, gateway.StopCalls);
    }

    [Fact]
    public async Task ExecuteStopAsync_WhenLocalSessionExists_AppliesLocalStatus()
    {
        var gateway = new StubLanGateway();
        var service = CreateService(gateway, out _);
        var order = new OrderData
        {
            InternalId = "order-stop-2",
            Id = "2002"
        };
        var runTokens = new Dictionary<string, CancellationTokenSource>
        {
            [order.InternalId] = new CancellationTokenSource()
        };
        var runProgress = new Dictionary<string, int>
        {
            [order.InternalId] = 25
        };
        var appliedCount = 0;

        var result = await service.ExecuteStopAsync(
            order: order,
            useLanApi: false,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            runTokensByOrder: runTokens,
            runProgressByOrderInternalId: runProgress,
            tryRefreshSnapshotFromStorage: (_, _) => true,
            applyLocalStopStatus: _ => appliedCount++);

        Assert.Equal(OrderRunStopPhaseStatus.LocalStatusApplied, result.Status);
        Assert.True(result.Preparation.CanProceed);
        Assert.True(result.Preparation.LocalCancellationRequested);
        Assert.Equal(1, appliedCount);
        Assert.Empty(runTokens);
        Assert.Empty(runProgress);
        Assert.Equal(0, gateway.StopCalls);
    }

    [Fact]
    public async Task ExecuteStopAsync_WhenLanUnavailableAndLocalSessionExists_ReturnsWarningFlag()
    {
        var gateway = new StubLanGateway();
        gateway.StopResponses.Enqueue(LanOrderRunApiResult.Unavailable("gateway timeout"));
        var service = CreateService(gateway, out _);
        var order = new OrderData
        {
            InternalId = "order-stop-3",
            Id = "2003"
        };
        var runTokens = new Dictionary<string, CancellationTokenSource>
        {
            [order.InternalId] = new CancellationTokenSource()
        };
        var runProgress = new Dictionary<string, int>
        {
            [order.InternalId] = 10
        };

        var result = await service.ExecuteStopAsync(
            order: order,
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            runTokensByOrder: runTokens,
            runProgressByOrderInternalId: runProgress,
            tryRefreshSnapshotFromStorage: (_, _) => true,
            applyLocalStopStatus: _ => { });

        Assert.Equal(OrderRunStopPhaseStatus.LocalStatusApplied, result.Status);
        Assert.True(result.ShouldWarnServerUnavailable);
        Assert.False(result.ShouldLogServerFailure);
        Assert.Contains("timeout", result.ServerReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, gateway.StopCalls);
    }

    [Fact]
    public async Task ExecuteStopAsync_WhenLanConflictWithoutLocalSession_ReturnsConflict()
    {
        var gateway = new StubLanGateway();
        gateway.StopResponses.Enqueue(LanOrderRunApiResult.Conflict("version mismatch", currentVersion: 56));
        var service = CreateService(gateway, out _);
        var order = new OrderData
        {
            InternalId = "order-stop-4",
            Id = "2004",
            StorageVersion = 12
        };

        var result = await service.ExecuteStopAsync(
            order: order,
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000/",
            actor: "operator-1",
            runTokensByOrder: new Dictionary<string, CancellationTokenSource>(),
            runProgressByOrderInternalId: new Dictionary<string, int>(),
            tryRefreshSnapshotFromStorage: (_, _) => true,
            applyLocalStopStatus: _ => throw new InvalidOperationException("should not be called"));

        Assert.Equal(OrderRunStopPhaseStatus.Conflict, result.Status);
        Assert.False(result.Preparation.CanApplyLocalStopStatus);
        Assert.Contains("version", result.ServerReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(56, order.StorageVersion);
        Assert.Equal(1, gateway.StopCalls);
    }

    private static OrderRunCommandService CreateService(StubLanGateway gateway, out OrderRunStateService runStateService)
    {
        runStateService = new OrderRunStateService();
        var coordinator = new LanRunCommandCoordinator(gateway);
        var orchestration = new OrderRunWorkflowOrchestrationService(runStateService, coordinator);
        return new OrderRunCommandService(orchestration, runStateService, new OrderRunExecutionService());
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
