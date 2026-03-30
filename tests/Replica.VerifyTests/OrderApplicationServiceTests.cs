using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
    public void BuildRunSelectionRequiredUiFeedback_ForwardedFromService_ReturnsAbortInfo()
    {
        var service = CreateService();

        var runSelection = service.BuildRunSelectionRequiredUiFeedback();
        var stopSelection = service.BuildRunStopSelectionRequiredUiFeedback();

        Assert.True(runSelection.ShouldAbort);
        Assert.Equal("Выберите строку заказа для запуска", runSelection.BottomStatus);
        Assert.Equal(OrderRunFeedbackSeverity.Information, runSelection.Dialog?.Severity);

        Assert.Equal("Выберите заказ для остановки", stopSelection.BottomStatus);
        Assert.False(stopSelection.ShouldUpdateActionButtons);
        Assert.Equal(OrderRunFeedbackSeverity.Information, stopSelection.Dialog?.Severity);
    }

    [Fact]
    public void BuildRunStatusMutations_ForwardedFromService_ReturnsExpectedPlans()
    {
        var service = CreateService();

        var start = service.BuildRunStartUiMutation(isBatchRun: false);
        var cancelled = service.BuildRunCancelledUiMutation();
        var failed = service.BuildRunFailedUiMutation("disk full");
        var stopLocal = service.BuildRunStopLocalUiMutation();
        var runPostStatus = service.BuildRunPostStatusApplyUiEffectsPlan();
        var runPerOrderCompletion = service.BuildRunPerOrderCompletionUiEffectsPlan();
        var runPostExecution = service.BuildRunPostExecutionUiEffectsPlan();
        var stopPlan = new OrderRunStateService.StopPlan(
            CanProceed: true,
            HasLocalRunSession: true,
            ShouldSendServerStop: true,
            LocalCancellationTokenSource: null);
        var stopPreparation = RunStopPreparationResult.From(
            stopPlan,
            stopCommandResult: LanRunStopCommandResult.NotUsed(),
            canApplyLocalStopStatus: true,
            localCancellationRequested: true,
            snapshotRefreshFailed: false);
        var stopPhase = OrderRunStopPhaseResult.LocalStatusApplied(
            stopPreparation,
            shouldWarnServerUnavailable: false,
            shouldLogServerFailure: false,
            serverReason: string.Empty);
        var stopFeedback = new OrderRunStopUiFeedback(string.Empty, shouldUpdateActionButtons: true, dialog: null);
        var stopPostPhase = service.BuildStopPostPhaseUiEffectsPlan(stopPhase, stopFeedback);

        Assert.Equal(WorkflowStatusNames.Processing, start.StatusMutation.Status);
        Assert.Contains("Запуск заказа", start.OperationLogMessage);
        Assert.False(start.StatusMutation.PersistHistory);
        Assert.False(start.StatusMutation.RebuildGrid);

        Assert.Equal(WorkflowStatusNames.Cancelled, cancelled.Status);
        Assert.Equal("Остановлено пользователем", cancelled.Reason);

        Assert.Equal(WorkflowStatusNames.Error, failed.Status);
        Assert.Equal("disk full", failed.Reason);

        Assert.Equal("Остановлено пользователем", stopLocal.OperationLogMessage);
        Assert.Equal(WorkflowStatusNames.Cancelled, stopLocal.StatusMutation.Status);
        Assert.True(stopLocal.StatusMutation.PersistHistory);
        Assert.True(stopLocal.StatusMutation.RebuildGrid);

        Assert.True(runPostStatus.ShouldUpdateTrayProgress);
        Assert.True(runPostStatus.ShouldSaveHistory);
        Assert.True(runPostStatus.ShouldRefreshGrid);
        Assert.False(runPostStatus.ShouldUpdateActionButtons);

        Assert.True(runPerOrderCompletion.ShouldUpdateTrayProgress);
        Assert.False(runPerOrderCompletion.ShouldSaveHistory);
        Assert.False(runPerOrderCompletion.ShouldRefreshGrid);
        Assert.False(runPerOrderCompletion.ShouldUpdateActionButtons);

        Assert.False(runPostExecution.ShouldUpdateTrayProgress);
        Assert.True(runPostExecution.ShouldSaveHistory);
        Assert.True(runPostExecution.ShouldRefreshGrid);
        Assert.True(runPostExecution.ShouldUpdateActionButtons);

        Assert.True(stopPostPhase.ShouldUpdateTrayProgress);
        Assert.False(stopPostPhase.ShouldSaveHistory);
        Assert.False(stopPostPhase.ShouldRefreshGrid);
        Assert.True(stopPostPhase.ShouldUpdateActionButtons);
    }

    [Fact]
    public void BuildPostGridMutationUiPlan_ReturnsDefaultMutationPolicy()
    {
        var service = CreateService();

        var plan = service.BuildPostGridMutationUiPlan();

        Assert.True(plan.ShouldSaveHistory);
        Assert.True(plan.ShouldUpdateActionButtons);
        Assert.Equal(OrderGridRefreshMode.FastRowsThenRebuild, plan.RefreshMode);
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
        var stopStart = service.BuildRunStopCommandStartLifecycleUiFeedback();
        var snapshot = service.BuildRunSnapshotRefreshWarningUiFeedback("run-stop", "00526");
        var finish = service.BuildRunCommandFinishLifecycleUiFeedback(2, 1);

        var startLog = Assert.Single(start.Logs);
        var stopStartLog = Assert.Single(stopStart.Logs);
        var snapshotLog = Assert.Single(snapshot.Logs);
        var finishLog = Assert.Single(finish.Logs);

        Assert.Equal("RUN | command-start", startLog.Message);
        Assert.False(startLog.IsWarning);

        Assert.Equal("RUN | stop-command-start", stopStartLog.Message);
        Assert.False(stopStartLog.IsWarning);

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
    public void UpsertOrderInHistory_AddsAndUpdatesByInternalId()
    {
        var service = CreateService();
        var history = new List<OrderData>
        {
            new() { InternalId = "ord-1", Id = "1001" }
        };

        var added = new OrderData { InternalId = "ord-2", Id = "1002" };
        service.UpsertOrderInHistory(history, added);
        Assert.Equal(2, history.Count);
        Assert.Same(added, history[1]);

        var updated = new OrderData { InternalId = "ord-1", Id = "1001-updated" };
        service.UpsertOrderInHistory(history, updated);
        Assert.Equal(2, history.Count);
        Assert.Same(updated, history[0]);
    }

    [Fact]
    public void UpsertOrderInHistory_WhenIncomingOrderNumberIsBlank_PreservesExistingOrderNumber()
    {
        var service = CreateService();
        var history = new List<OrderData>
        {
            new() { InternalId = "ord-1", Id = "1001" }
        };

        var updated = new OrderData
        {
            InternalId = "ord-1",
            Id = " ",
            Status = WorkflowStatusNames.Processing
        };

        service.UpsertOrderInHistory(history, updated);

        Assert.Single(history);
        Assert.Same(updated, history[0]);
        Assert.Equal("1001", history[0].Id);
        Assert.Equal(WorkflowStatusNames.Processing, history[0].Status);
    }

    [Fact]
    public void ApplyLanStatusSnapshot_UpdatesStatusFieldsFromServer()
    {
        var service = CreateService();
        var local = new OrderData
        {
            StorageVersion = 1,
            Status = WorkflowStatusNames.Waiting,
            LastStatusSource = "local",
            LastStatusReason = "pending"
        };
        var timestamp = new DateTime(2026, 3, 26, 16, 40, 0, DateTimeKind.Local);
        var server = new OrderData
        {
            StorageVersion = 12,
            Status = " Завершено ",
            LastStatusSource = " api ",
            LastStatusReason = " done ",
            LastStatusAt = timestamp
        };

        service.ApplyLanStatusSnapshot(local, server);

        Assert.Equal(12, local.StorageVersion);
        Assert.Equal("Завершено", local.Status);
        Assert.Equal("api", local.LastStatusSource);
        Assert.Equal("done", local.LastStatusReason);
        Assert.Equal(timestamp, local.LastStatusAt);
    }

    [Fact]
    public void ApplyLanOrderItemVersionsSnapshot_UpdatesOnlyMatchingItems()
    {
        var service = CreateService();
        var local = new OrderData
        {
            InternalId = "ord-1",
            StorageVersion = 2,
            Items =
            [
                new OrderFileItem { ItemId = "i-1", StorageVersion = 1 },
                new OrderFileItem { ItemId = "i-2", StorageVersion = 1 }
            ]
        };
        var server = new OrderData
        {
            InternalId = "ord-1",
            StorageVersion = 9,
            Items =
            [
                new OrderFileItem { ItemId = "i-1", StorageVersion = 7 },
                new OrderFileItem { ItemId = "i-3", StorageVersion = 5 }
            ]
        };

        service.ApplyLanOrderItemVersionsSnapshot(local, server);

        Assert.Equal(9, local.StorageVersion);
        Assert.Equal(7, local.Items![0].StorageVersion);
        Assert.Equal(1, local.Items[1].StorageVersion);
    }

    [Fact]
    public void ApplyLanOrderItemDeleteSnapshot_RemovesMissingItemsAndSyncsVersions()
    {
        var service = CreateService();
        var local = new OrderData
        {
            InternalId = "ord-1",
            StorageVersion = 3,
            Items =
            [
                new OrderFileItem { ItemId = "i-1", StorageVersion = 1 },
                new OrderFileItem { ItemId = "i-2", StorageVersion = 1 }
            ]
        };
        var server = new OrderData
        {
            InternalId = "ord-1",
            StorageVersion = 11,
            Items =
            [
                new OrderFileItem { ItemId = "i-2", StorageVersion = 8 }
            ]
        };

        service.ApplyLanOrderItemDeleteSnapshot(local, server);

        Assert.Equal(11, local.StorageVersion);
        Assert.Single(local.Items!);
        Assert.Equal("i-2", local.Items[0].ItemId);
        Assert.Equal(8, local.Items[0].StorageVersion);
    }

    [Fact]
    public void ApplyLanOrderWriteResult_OnSuccess_UpsertsHistoryAndReturnsRefreshReason()
    {
        var service = CreateService();
        var local = new OrderData { InternalId = "ord-1", Id = "1001" };
        var history = new List<OrderData> { local };
        var server = new OrderData { InternalId = "ord-1", Id = "1001", StorageVersion = 15 };

        var outcome = service.ApplyLanOrderWriteResult(
            history,
            targetOrder: local,
            LanOrderWriteCommandResult.Success(server),
            operationCaption: "Редактирование заказа",
            successSnapshotReason: "lan-api-update-order");

        Assert.True(outcome.IsSuccess);
        Assert.Same(server, outcome.Order);
        Assert.Same(server, history[0]);
        Assert.True(outcome.ShouldRefreshSnapshot);
        Assert.Equal("lan-api-update-order", outcome.SnapshotRefreshReason);
        Assert.Null(outcome.Dialog);
        Assert.True(string.IsNullOrWhiteSpace(outcome.BottomStatus));
    }

    [Fact]
    public void ApplyLanOrderWriteResult_OnConflict_SetsStorageVersionAndBuildsUiFeedback()
    {
        var service = CreateService();
        var local = new OrderData { InternalId = "ord-1", StorageVersion = 1 };
        var history = new List<OrderData> { local };

        var outcome = service.ApplyLanOrderWriteResult(
            history,
            targetOrder: local,
            LanOrderWriteCommandResult.Conflict("version conflict", currentVersion: 42),
            operationCaption: "Редактирование заказа",
            successSnapshotReason: "lan-api-update-order");

        Assert.False(outcome.IsSuccess);
        Assert.Equal(42, local.StorageVersion);
        Assert.True(outcome.ShouldRefreshSnapshot);
        Assert.Equal("lan-api-write-conflict", outcome.SnapshotRefreshReason);
        Assert.Contains("конфликт", outcome.BottomStatus, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(outcome.Dialog);
        Assert.Equal(OrderRunFeedbackSeverity.Information, outcome.Dialog!.Severity);
    }

    [Fact]
    public async Task SyncLanOrderItemUpsertAsync_OnGatewayFailure_ReturnsWarningAndFailedRefreshReason()
    {
        var service = CreateService();
        var item = new OrderFileItem { ItemId = "item-1", StorageVersion = 2 };
        var order = new OrderData
        {
            InternalId = "ord-1",
            Id = "00526",
            Items = [item]
        };

        var outcome = await service.SyncLanOrderItemUpsertAsync(
            order,
            item,
            lanApiBaseUrl: string.Empty,
            actor: "tester",
            reason: "unit-test",
            orderDisplayIdResolver: _ => "00526");

        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.ShouldRefreshSnapshot);
        Assert.Equal("lan-api-item-upsert-failed-unit-test", outcome.SnapshotRefreshReason);
        var log = Assert.Single(outcome.Logs);
        Assert.True(log.IsWarning);
        Assert.Contains("item-upsert-sync-failed", log.Message);
        Assert.Contains("item=item-1", log.Message);
    }

    [Fact]
    public async Task SyncLanItemReorderForOrdersAsync_DeduplicatesByInternalId_AndBuildsSingleFailureLog()
    {
        var service = CreateService();
        var orderA = new OrderData
        {
            InternalId = "ord-a",
            Id = "00526",
            Items = [new OrderFileItem { ItemId = "a1" }, new OrderFileItem { ItemId = "a2" }]
        };
        var duplicateOrderA = new OrderData
        {
            InternalId = "ord-a",
            Id = "00526-copy",
            Items = [new OrderFileItem { ItemId = "x1" }, new OrderFileItem { ItemId = "x2" }]
        };
        var singleItemOrder = new OrderData
        {
            InternalId = "ord-b",
            Id = "00527",
            Items = [new OrderFileItem { ItemId = "b1" }]
        };

        var outcome = await service.SyncLanItemReorderForOrdersAsync(
            orders: [orderA, duplicateOrderA, singleItemOrder],
            orderHistory: new List<OrderData> { orderA, singleItemOrder },
            lanApiBaseUrl: string.Empty,
            actor: "tester",
            reason: "unit-test",
            orderDisplayIdResolver: o => o.Id ?? string.Empty);

        Assert.True(outcome.ShouldRefreshSnapshot);
        Assert.Equal("lan-api-item-reorder-unit-test", outcome.SnapshotRefreshReason);
        var log = Assert.Single(outcome.Logs);
        Assert.True(log.IsWarning);
        Assert.Contains("item-reorder-sync-failed", log.Message);
        Assert.Contains("order=00526", log.Message);
    }

    [Fact]
    public async Task TryPersistOrderStatusViaLanApiAsync_OnUnavailableGateway_ReturnsWarningLog()
    {
        var service = CreateService();
        var order = new OrderData
        {
            InternalId = "ord-1",
            Id = "00526",
            Status = WorkflowStatusNames.Processing,
            UserName = "tester"
        };

        var outcome = await service.TryPersistOrderStatusViaLanApiAsync(
            order,
            lanApiBaseUrl: string.Empty,
            actor: "tester",
            normalizeUserName: value => value ?? string.Empty,
            source: OrderStatusSourceNames.Ui,
            reason: "unit-test",
            orderDisplayIdResolver: o => o.Id ?? string.Empty);

        Assert.False(outcome.IsPersisted);
        Assert.False(outcome.ShouldRefreshSnapshot);
        Assert.True(string.IsNullOrWhiteSpace(outcome.SnapshotRefreshReason));
        var log = Assert.Single(outcome.Logs);
        Assert.True(log.IsWarning);
        Assert.Contains("status-update-failed", log.Message);
        Assert.Contains("order=00526", log.Message);
        Assert.Contains("unavailable=1", log.Message);
    }

    [Fact]
    public async Task TryPersistOrderStatusViaLanApiAsync_OnConflict_UpdatesOrderVersionAndReturnsWarningLog()
    {
        var service = CreateService(new StubLanOrderWriteApiGateway(_ => LanOrderWriteApiResult.Conflict("version conflict", currentVersion: 42)));
        var order = new OrderData
        {
            InternalId = "ord-1",
            Id = "00526",
            StorageVersion = 5,
            Status = WorkflowStatusNames.Processing,
            UserName = "tester"
        };

        var outcome = await service.TryPersistOrderStatusViaLanApiAsync(
            order,
            lanApiBaseUrl: "http://localhost:5000",
            actor: "tester",
            normalizeUserName: value => value ?? string.Empty,
            source: OrderStatusSourceNames.Processor,
            reason: "conflict-test",
            orderDisplayIdResolver: o => o.Id ?? string.Empty);

        Assert.False(outcome.IsPersisted);
        Assert.Equal(42, order.StorageVersion);
        var log = Assert.Single(outcome.Logs);
        Assert.True(log.IsWarning);
        Assert.Contains("status-update-failed", log.Message);
        Assert.Contains("conflict=1", log.Message);
    }

    [Fact]
    public async Task ApplyStatusTransitionWithPersistenceAsync_LocalMode_ReturnsSaveLocalHistoryPlan()
    {
        var service = CreateService();
        var order = new OrderData
        {
            InternalId = "ord-1",
            Id = "00526",
            Status = WorkflowStatusNames.Waiting
        };

        var outcome = await service.ApplyStatusTransitionWithPersistenceAsync(
            order,
            status: WorkflowStatusNames.Processing,
            source: OrderStatusSourceNames.Ui,
            reason: "unit-test",
            persistHistory: true,
            rebuildGrid: true,
            useLanApi: false,
            lanApiBaseUrl: string.Empty,
            actor: "tester",
            normalizeUserName: value => value ?? string.Empty,
            orderDisplayIdResolver: o => o.Id ?? string.Empty);

        Assert.True(outcome.Changed);
        Assert.NotNull(outcome.Transition);
        Assert.True(outcome.ShouldSaveLocalHistory);
        Assert.False(outcome.ShouldRefreshSnapshot);
        Assert.Equal(OrderStatusUiRefreshMode.FastRowsThenRebuild, outcome.UiRefreshMode);
        Assert.Empty(outcome.Logs);
        Assert.Equal(WorkflowStatusNames.Processing, order.Status);
    }

    [Fact]
    public async Task ApplyStatusTransitionWithPersistenceAsync_LanPersistFailure_ReturnsFallbackSaveAndWarning()
    {
        var service = CreateService(new StubLanOrderWriteApiGateway(_ => LanOrderWriteApiResult.Unavailable("down")));
        var order = new OrderData
        {
            InternalId = "ord-1",
            Id = "00526",
            Status = WorkflowStatusNames.Waiting
        };

        var outcome = await service.ApplyStatusTransitionWithPersistenceAsync(
            order,
            status: WorkflowStatusNames.Processing,
            source: OrderStatusSourceNames.Processor,
            reason: "unit-test",
            persistHistory: true,
            rebuildGrid: true,
            useLanApi: true,
            lanApiBaseUrl: "http://localhost:5000",
            actor: "tester",
            normalizeUserName: value => value ?? string.Empty,
            orderDisplayIdResolver: o => o.Id ?? string.Empty);

        Assert.True(outcome.Changed);
        Assert.True(outcome.ShouldSaveLocalHistory);
        Assert.False(outcome.ShouldRefreshSnapshot);
        Assert.Equal(OrderStatusUiRefreshMode.Coalesced, outcome.UiRefreshMode);
        Assert.True(outcome.Logs.Count >= 2);
        Assert.Contains(outcome.Logs, log => log.Message.Contains("status-update-failed", StringComparison.Ordinal));
        Assert.Contains(outcome.Logs, log => log.Message.Contains("status-update-fallback-local-save", StringComparison.Ordinal));
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

    private static OrderApplicationService CreateService(ILanOrderWriteApiGateway? lanOrderWriteApiGateway = null)
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
            lanOrderWriteCommandService: new LanOrderWriteCommandService(lanOrderWriteApiGateway ?? new LanOrderWriteApiGateway()));
    }

    private sealed class StubLanOrderWriteApiGateway : ILanOrderWriteApiGateway
    {
        private readonly Func<LanUpdateOrderRequest, LanOrderWriteApiResult> _updateOrder;

        public StubLanOrderWriteApiGateway(Func<LanUpdateOrderRequest, LanOrderWriteApiResult> updateOrder)
        {
            _updateOrder = updateOrder;
        }

        public Task<LanOrderWriteApiResult> CreateOrderAsync(string apiBaseUrl, LanCreateOrderRequest request, string actor, CancellationToken cancellationToken = default)
            => Task.FromResult(LanOrderWriteApiResult.Failed("not-implemented"));

        public Task<LanOrderWriteApiResult> DeleteOrderAsync(string apiBaseUrl, string orderInternalId, LanDeleteOrderRequest request, string actor, CancellationToken cancellationToken = default)
            => Task.FromResult(LanOrderWriteApiResult.Failed("not-implemented"));

        public Task<LanOrderWriteApiResult> UpdateOrderAsync(string apiBaseUrl, string orderInternalId, LanUpdateOrderRequest request, string actor, CancellationToken cancellationToken = default)
            => Task.FromResult(_updateOrder(request));

        public Task<LanOrderWriteApiResult> ReorderOrderItemsAsync(string apiBaseUrl, string orderInternalId, LanReorderOrderItemsRequest request, string actor, CancellationToken cancellationToken = default)
            => Task.FromResult(LanOrderWriteApiResult.Failed("not-implemented"));

        public Task<LanOrderWriteApiResult> AddOrderItemAsync(string apiBaseUrl, string orderInternalId, LanAddOrderItemRequest request, string actor, CancellationToken cancellationToken = default)
            => Task.FromResult(LanOrderWriteApiResult.Failed("not-implemented"));

        public Task<LanOrderWriteApiResult> UpdateOrderItemAsync(string apiBaseUrl, string orderInternalId, string itemId, LanUpdateOrderItemRequest request, string actor, CancellationToken cancellationToken = default)
            => Task.FromResult(LanOrderWriteApiResult.Failed("not-implemented"));

        public Task<LanOrderWriteApiResult> DeleteOrderItemAsync(string apiBaseUrl, string orderInternalId, string itemId, LanDeleteOrderItemRequest request, string actor, CancellationToken cancellationToken = default)
            => Task.FromResult(LanOrderWriteApiResult.Failed("not-implemented"));
    }
}
