using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderApplicationServiceTests
{
    [Fact]
    public void AddCreatedOrder_AssignsInternalIdAndAddsToHistory()
    {
        var service = CreateService();
        var history = new List<OrderData>();
        var order = new OrderData
        {
            Id = "1001",
            UserName = " test-user "
        };

        var internalId = service.AddCreatedOrder(history, order, user => user.Trim());

        Assert.Single(history);
        Assert.False(string.IsNullOrWhiteSpace(internalId));
        Assert.Equal(order.InternalId, internalId);
        Assert.Equal("test-user", history[0].UserName);
    }

    [Fact]
    public void BuildRunSkippedDetails_ComposesLocalAndServerReasons()
    {
        var service = CreateService();
        var runPlan = new OrderRunStateService.RunPlan(
            RunnableOrders: [],
            OrdersWithoutNumber: [new OrderData()],
            AlreadyRunningOrders: [new OrderData()]);

        var details = service.BuildRunSkippedDetails(runPlan, ["server rejected"]);

        Assert.Equal("без номера: 1, уже запущены: 1, сервер отклонил: 1", details);
    }

    [Fact]
    public void BuildRunStartUiFeedback_ForFatalPhase_ReturnsAbort()
    {
        var service = CreateService();
        var runPlan = new OrderRunStateService.RunPlan(RunnableOrders: [], OrdersWithoutNumber: [], AlreadyRunningOrders: []);
        var startPhase = OrderRunStartPhaseResult.Fatal(RunStartPreparationResult.Fatal(runPlan, "fatal"));

        var feedback = service.BuildRunStartUiFeedback(startPhase);

        Assert.True(feedback.ShouldAbort);
        Assert.Equal(OrderRunFeedbackSeverity.Warning, feedback.Dialog?.Severity);
    }

    [Fact]
    public void BuildRunStopUiFeedback_ForConflict_ReturnsConflictBottomStatus()
    {
        var service = CreateService();
        var stopPlan = new OrderRunStateService.StopPlan(
            CanProceed: true,
            HasLocalRunSession: false,
            ShouldSendServerStop: true,
            LocalCancellationTokenSource: null);
        var stopPreparation = RunStopPreparationResult.From(
            stopPlan,
            stopCommandResult: LanRunStopCommandResult.NotUsed(),
            canApplyLocalStopStatus: false,
            localCancellationRequested: false,
            snapshotRefreshFailed: false);
        var stopPhase = OrderRunStopPhaseResult.Conflict(stopPreparation, "version conflict");

        var feedback = service.BuildRunStopUiFeedback(stopPhase, "00526");

        Assert.Contains("00526", feedback.BottomStatus);
        Assert.Contains("конфликт", feedback.BottomStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OrderRunFeedbackSeverity.Information, feedback.Dialog?.Severity);
    }

    [Fact]
    public void BuildRunStartProgressUiFeedback_WhenServerSkipped_ReturnsDialog()
    {
        var service = CreateService();
        var runPlan = new OrderRunStateService.RunPlan(
            RunnableOrders: [new OrderData()],
            OrdersWithoutNumber: [],
            AlreadyRunningOrders: []);

        var feedback = service.BuildRunStartProgressUiFeedback(
            runnableOrdersCount: 1,
            runPlan: runPlan,
            serverSkipped: ["order-1: locked"]);

        Assert.NotNull(feedback.Dialog);
        Assert.Contains("order-1: locked", feedback.Dialog!.Message);
    }

    [Fact]
    public void BuildRunCompletionUiFeedback_WhenNoErrorsAndBatch_ReturnsBatchStatus()
    {
        var service = CreateService();

        var feedback = service.BuildRunCompletionUiFeedback(
            errors: [],
            runnableOrdersCount: 2,
            orderDisplayIdResolver: _ => "order");

        Assert.Contains("2", feedback.BottomStatus);
        Assert.Null(feedback.Dialog);
    }

    [Fact]
    public void BuildRunLifecycleUiFeedback_ForwardedFromService_ReturnsExpectedLogs()
    {
        var service = CreateService();

        var start = service.BuildRunCommandStartLifecycleUiFeedback();
        var snapshot = service.BuildRunSnapshotRefreshWarningUiFeedback("run-stop", "00526");
        var finish = service.BuildRunCommandFinishLifecycleUiFeedback(2, 1);

        var startLog = Assert.Single(start.Logs);
        var snapshotLog = Assert.Single(snapshot.Logs);
        var finishLog = Assert.Single(finish.Logs);

        Assert.Equal("RUN | command-start", startLog.Message);
        Assert.False(startLog.IsWarning);

        Assert.Equal("RUN | snapshot-refresh-failed | reason=run-stop | order=00526", snapshotLog.Message);
        Assert.True(snapshotLog.IsWarning);

        Assert.Equal("RUN | command-finish | started=2 | errors=1", finishLog.Message);
        Assert.False(finishLog.IsWarning);
    }

    [Fact]
    public void SyncStorageVersions_UpdatesOnlyMatchingLocalOrders()
    {
        var service = CreateService();
        var localOrders = new List<OrderData>
        {
            new() { InternalId = "a", StorageVersion = 1 },
            new() { InternalId = "b", StorageVersion = 2 }
        };
        var storageOrders = new List<OrderData>
        {
            new() { InternalId = "a", StorageVersion = 10 },
            new() { InternalId = "x", StorageVersion = 99 }
        };

        var updated = service.SyncStorageVersions(localOrders, storageOrders);

        Assert.Equal(1, updated);
        Assert.Equal(10, localOrders[0].StorageVersion);
        Assert.Equal(2, localOrders[1].StorageVersion);
    }

    [Fact]
    public void TryBuildRenamedPath_ReturnsSuccessForAvailableTarget()
    {
        var service = CreateService();
        var tempDir = Path.Combine(Path.GetTempPath(), "replica_order_app_service_tests");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, $"src_{Guid.NewGuid():N}.pdf");
        File.WriteAllText(sourcePath, "pdf");
        try
        {
            var result = service.TryBuildRenamedPath(sourcePath, "renamed_file");

            Assert.True(result.IsSuccess);
            Assert.Equal(RenamePathBuildStatus.Success, result.Status);
            Assert.EndsWith("renamed_file.pdf", result.RenamedPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(sourcePath))
                File.Delete(sourcePath);
        }
    }

    [Fact]
    public void ResolvePreferredOrderFolder_UsesOrderFolderName()
    {
        var service = CreateService();
        var order = new OrderData { FolderName = "A-100" };

        var resolved = service.ResolvePreferredOrderFolder(order, @"C:\orders", @"C:\temp");

        Assert.Equal(Path.Combine(@"C:\orders", "A-100"), resolved);
    }

    private static OrderApplicationService CreateService()
    {
        var now = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Local);
        var orderRunStateService = new OrderRunStateService();
        var lanRunCommandCoordinator = new LanRunCommandCoordinator(new LanOrderRunApiGateway());
        var orderRunWorkflowOrchestrationService = new OrderRunWorkflowOrchestrationService(
            orderRunStateService,
            lanRunCommandCoordinator);
        var orderRunCommandService = new OrderRunCommandService(
            orderRunWorkflowOrchestrationService,
            orderRunStateService,
            new OrderRunExecutionService());
        var orderItemMutationService = new OrderItemMutationService(() => now);
        var orderFilePathMutationService = new OrderFilePathMutationService(() => now);
        var orderFileRenameRemoveCommandService = new OrderFileRenameRemoveCommandService(
            orderFilePathMutationService,
            orderItemMutationService);

        return new OrderApplicationService(
            orderRunCommandService: orderRunCommandService,
            orderRunFeedbackService: new OrderRunFeedbackService(),
            ordersHistoryRepositoryCoordinator: new OrdersHistoryRepositoryCoordinator(),
            ordersHistoryMaintenanceService: new OrdersHistoryMaintenanceService(() => now),
            orderFolderPathResolutionService: new OrderFolderPathResolutionService(),
            orderEditorMutationService: new OrderEditorMutationService(() => now),
            orderItemMutationService: orderItemMutationService,
            orderFileStageCommandService: new OrderFileStageCommandService(),
            orderFilePathMutationService: orderFilePathMutationService,
            orderFileRenameRemoveCommandService: orderFileRenameRemoveCommandService,
            orderDeleteCommandService: new OrderDeleteCommandService(),
            orderItemDeleteCommandService: new OrderItemDeleteCommandService(itemMutationService: orderItemMutationService),
            orderStatusTransitionService: new OrderStatusTransitionService(),
            orderStorageVersionSyncService: new OrderStorageVersionSyncService(),
            lanOrderWriteCommandService: new LanOrderWriteCommandService(new LanOrderWriteApiGateway()));
    }
}
