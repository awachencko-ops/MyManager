using System.Collections.Generic;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderRunFeedbackServiceTests
{
    [Fact]
    public void BuildServerSkippedPreview_ReturnsTrimmedPreviewWithOverflowSuffix()
    {
        var service = new OrderRunFeedbackService();
        var skipped = new List<string>
        {
            "  order-1: locked  ",
            "order-2: not found",
            "order-3: version conflict"
        };

        var preview = service.BuildServerSkippedPreview(skipped, previewLimit: 2);

        Assert.Contains("order-1: locked", preview);
        Assert.Contains("order-2: not found", preview);
        Assert.Contains(": 1", preview);
    }

    [Fact]
    public void BuildSkippedDetails_IncludesLocalAndServerReasons()
    {
        var service = new OrderRunFeedbackService();
        var runPlan = new OrderRunStateService.RunPlan(
            RunnableOrders: new List<OrderData>(),
            OrdersWithoutNumber: new List<OrderData> { new() },
            AlreadyRunningOrders: new List<OrderData> { new() });

        var details = service.BuildSkippedDetails(runPlan, new[] { "server reject 1", "server reject 2" });

        Assert.Equal("без номера: 1, уже запущены: 1, сервер отклонил: 2", details);
    }

    [Fact]
    public void BuildExecutionErrorsPreview_UsesResolverAndFallbackMessage()
    {
        var service = new OrderRunFeedbackService();
        var errors = new List<OrderRunExecutionError>
        {
            new(new OrderData { Id = "100", InternalId = "internal-100" }, "disk full"),
            new(new OrderData { Id = "200", InternalId = "internal-200" }, " ")
        };

        var preview = service.BuildExecutionErrorsPreview(errors, order => order.Id ?? string.Empty);

        Assert.Contains("100: disk full", preview);
        Assert.Contains("200: неизвестная ошибка", preview);
    }

    [Fact]
    public void BuildExecutionErrorsPreview_ReturnsEmptyWhenNoErrors()
    {
        var service = new OrderRunFeedbackService();

        var preview = service.BuildExecutionErrorsPreview(new List<OrderRunExecutionError>(), _ => "order");

        Assert.Equal(string.Empty, preview);
    }

    [Fact]
    public void BuildStartUiFeedback_ForFatalPhase_ReturnsAbortWarningAndLog()
    {
        var service = new OrderRunFeedbackService();
        var runPlan = new OrderRunStateService.RunPlan(RunnableOrders: [], OrdersWithoutNumber: [], AlreadyRunningOrders: []);
        var preparation = RunStartPreparationResult.Fatal(runPlan, "api unavailable");
        var startPhase = OrderRunStartPhaseResult.Fatal(preparation);

        var feedback = service.BuildStartUiFeedback(startPhase);

        Assert.True(feedback.ShouldAbort);
        Assert.Contains("api unavailable", feedback.BottomStatus);
        Assert.NotNull(feedback.Dialog);
        Assert.Equal(OrderRunFeedbackSeverity.Warning, feedback.Dialog!.Severity);
        Assert.Single(feedback.Logs);
        Assert.True(feedback.Logs[0].IsWarning);
        Assert.Contains("RUN | command-fatal", feedback.Logs[0].Message);
    }

    [Fact]
    public void BuildRunSelectionRequiredUiFeedback_ReturnsAbortInfoDialog()
    {
        var service = new OrderRunFeedbackService();

        var feedback = service.BuildRunSelectionRequiredUiFeedback();

        Assert.True(feedback.ShouldAbort);
        Assert.Equal("Выберите строку заказа для запуска", feedback.BottomStatus);
        Assert.NotNull(feedback.Dialog);
        Assert.Equal(OrderRunFeedbackSeverity.Information, feedback.Dialog!.Severity);
        Assert.Equal("Запуск", feedback.Dialog.Caption);
    }

    [Fact]
    public void BuildRunStartUiMutation_ForBatch_ReturnsBatchOperationAndProcessingStatus()
    {
        var service = new OrderRunFeedbackService();

        var mutation = service.BuildRunStartUiMutation(isBatchRun: true);

        Assert.Contains("Пакетный запуск", mutation.OperationLogMessage);
        Assert.Equal(WorkflowStatusNames.Processing, mutation.StatusMutation.Status);
        Assert.Equal(OrderStatusSourceNames.Ui, mutation.StatusMutation.Source);
        Assert.Contains("Пакетный запуск", mutation.StatusMutation.Reason);
        Assert.False(mutation.StatusMutation.PersistHistory);
        Assert.False(mutation.StatusMutation.RebuildGrid);
    }

    [Fact]
    public void BuildStartUiFeedback_ForServerRejected_IncludesPreviewAndWarningLog()
    {
        var service = new OrderRunFeedbackService();
        var runPlan = new OrderRunStateService.RunPlan(RunnableOrders: [], OrdersWithoutNumber: [], AlreadyRunningOrders: []);
        var preparation = RunStartPreparationResult.From(
            runPlan,
            runnableOrders: [],
            skippedByServer: ["order-1: locked"],
            usedLanApi: true,
            snapshotRefreshFailed: false);
        var startPhase = OrderRunStartPhaseResult.ServerRejected(preparation);

        var feedback = service.BuildStartUiFeedback(startPhase);

        Assert.True(feedback.ShouldAbort);
        Assert.Contains("Сервер", feedback.BottomStatus);
        Assert.NotNull(feedback.Dialog);
        Assert.Contains("order-1: locked", feedback.Dialog!.Message);
        Assert.Single(feedback.Logs);
        Assert.Contains("RUN | command-rejected-by-server", feedback.Logs[0].Message);
    }

    [Fact]
    public void BuildStopUiFeedback_ForNotRunning_ReturnsInfoDialogAndWarningLog()
    {
        var service = new OrderRunFeedbackService();
        var stopPhase = OrderRunStopPhaseResult.NotRunning(
            RunStopPreparationResult.NotRunning(OrderRunStateService.StopPlan.InvalidOrder()));

        var feedback = service.BuildStopUiFeedback(stopPhase, "00526");

        Assert.Contains("00526", feedback.BottomStatus);
        Assert.NotNull(feedback.Dialog);
        Assert.Equal(OrderRunFeedbackSeverity.Information, feedback.Dialog!.Severity);
        Assert.False(feedback.ShouldUpdateActionButtons);
        Assert.Single(feedback.Logs);
        Assert.True(feedback.Logs[0].IsWarning);
    }

    [Fact]
    public void BuildStopSelectionRequiredUiFeedback_ReturnsInfoDialogAndNoActionUpdate()
    {
        var service = new OrderRunFeedbackService();

        var feedback = service.BuildStopSelectionRequiredUiFeedback();

        Assert.Equal("Выберите заказ для остановки", feedback.BottomStatus);
        Assert.False(feedback.ShouldUpdateActionButtons);
        Assert.NotNull(feedback.Dialog);
        Assert.Equal(OrderRunFeedbackSeverity.Information, feedback.Dialog!.Severity);
        Assert.Equal("Остановка", feedback.Dialog.Caption);
    }

    [Fact]
    public void BuildRunCancelledUiMutation_ReturnsCancelledStatusWithNoPersist()
    {
        var service = new OrderRunFeedbackService();

        var mutation = service.BuildRunCancelledUiMutation();

        Assert.Equal(WorkflowStatusNames.Cancelled, mutation.Status);
        Assert.Equal(OrderStatusSourceNames.Ui, mutation.Source);
        Assert.Equal("Остановлено пользователем", mutation.Reason);
        Assert.False(mutation.PersistHistory);
        Assert.False(mutation.RebuildGrid);
    }

    [Fact]
    public void BuildRunFailedUiMutation_EmptyMessage_UsesUnknownErrorFallback()
    {
        var service = new OrderRunFeedbackService();

        var mutation = service.BuildRunFailedUiMutation("  ");

        Assert.Equal(WorkflowStatusNames.Error, mutation.Status);
        Assert.Equal(OrderStatusSourceNames.Ui, mutation.Source);
        Assert.Equal("неизвестная ошибка", mutation.Reason);
        Assert.False(mutation.PersistHistory);
        Assert.False(mutation.RebuildGrid);
    }

    [Fact]
    public void BuildStopLocalUiMutation_ReturnsCancelledStatusWithPersistAndRebuild()
    {
        var service = new OrderRunFeedbackService();

        var mutation = service.BuildStopLocalUiMutation();

        Assert.Equal("Остановлено пользователем", mutation.OperationLogMessage);
        Assert.Equal(WorkflowStatusNames.Cancelled, mutation.StatusMutation.Status);
        Assert.Equal(OrderStatusSourceNames.Ui, mutation.StatusMutation.Source);
        Assert.Equal("Остановлено пользователем", mutation.StatusMutation.Reason);
        Assert.True(mutation.StatusMutation.PersistHistory);
        Assert.True(mutation.StatusMutation.RebuildGrid);
    }

    [Fact]
    public void BuildStopUiFeedback_ForLocalAppliedWithUnavailable_AddsWarningDialogAndFinishInfoLog()
    {
        var service = new OrderRunFeedbackService();
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
            shouldWarnServerUnavailable: true,
            shouldLogServerFailure: false,
            serverReason: "connection refused");

        var feedback = service.BuildStopUiFeedback(stopPhase, "00526");

        Assert.Contains("00526", feedback.BottomStatus);
        Assert.True(feedback.ShouldUpdateActionButtons);
        Assert.NotNull(feedback.Dialog);
        Assert.Equal(OrderRunFeedbackSeverity.Warning, feedback.Dialog!.Severity);
        Assert.Contains("connection refused", feedback.Dialog.Message);
        Assert.Contains(feedback.Logs, log => log.Message.Contains("stop-server-unavailable") && log.IsWarning);
        Assert.Contains(feedback.Logs, log => log.Message.Contains("local-status-applied=1") && !log.IsWarning);
    }

    [Fact]
    public void BuildStartProgressUiFeedback_WithSkippedItems_ReturnsSkippedBottomStatusAndServerDialog()
    {
        var service = new OrderRunFeedbackService();
        var runPlan = new OrderRunStateService.RunPlan(
            RunnableOrders: new List<OrderData> { new() },
            OrdersWithoutNumber: new List<OrderData> { new() },
            AlreadyRunningOrders: new List<OrderData>());

        var feedback = service.BuildStartProgressUiFeedback(
            runnableOrdersCount: 1,
            runPlan: runPlan,
            serverSkipped: new[] { "order-1: locked" });

        Assert.Contains("пропущена", feedback.BottomStatus);
        Assert.NotNull(feedback.Dialog);
        Assert.Contains("order-1: locked", feedback.Dialog!.Message);
    }

    [Fact]
    public void BuildRunUiEffectsPlans_ReturnExpectedFlags()
    {
        var service = new OrderRunFeedbackService();

        var postStatus = service.BuildRunPostStatusApplyUiEffectsPlan();
        var perOrderCompletion = service.BuildRunPerOrderCompletionUiEffectsPlan();
        var postExecution = service.BuildRunPostExecutionUiEffectsPlan();

        Assert.True(postStatus.ShouldUpdateTrayProgress);
        Assert.True(postStatus.ShouldSaveHistory);
        Assert.True(postStatus.ShouldRefreshGrid);
        Assert.False(postStatus.ShouldUpdateActionButtons);

        Assert.True(perOrderCompletion.ShouldUpdateTrayProgress);
        Assert.False(perOrderCompletion.ShouldSaveHistory);
        Assert.False(perOrderCompletion.ShouldRefreshGrid);
        Assert.False(perOrderCompletion.ShouldUpdateActionButtons);

        Assert.False(postExecution.ShouldUpdateTrayProgress);
        Assert.True(postExecution.ShouldSaveHistory);
        Assert.True(postExecution.ShouldRefreshGrid);
        Assert.True(postExecution.ShouldUpdateActionButtons);
    }

    [Fact]
    public void BuildStopPostPhaseUiEffectsPlan_UsesStopPhaseAndFeedbackFlags()
    {
        var service = new OrderRunFeedbackService();
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
        var stopFeedback = new OrderRunStopUiFeedback(
            bottomStatus: string.Empty,
            shouldUpdateActionButtons: true,
            dialog: null);

        var effects = service.BuildStopPostPhaseUiEffectsPlan(stopPhase, stopFeedback);

        Assert.True(effects.ShouldUpdateTrayProgress);
        Assert.False(effects.ShouldSaveHistory);
        Assert.False(effects.ShouldRefreshGrid);
        Assert.True(effects.ShouldUpdateActionButtons);
    }

    [Fact]
    public void BuildCompletionUiFeedback_WithErrors_ReturnsWarningDialogAndErrorCount()
    {
        var service = new OrderRunFeedbackService();
        var errors = new List<OrderRunExecutionError>
        {
            new(new OrderData { Id = "100", InternalId = "internal-100" }, "disk full")
        };

        var feedback = service.BuildCompletionUiFeedback(errors, runnableOrdersCount: 1, orderDisplayIdResolver: order => order.Id ?? string.Empty);

        Assert.Contains("1", feedback.BottomStatus);
        Assert.NotNull(feedback.Dialog);
        Assert.Equal(OrderRunFeedbackSeverity.Warning, feedback.Dialog!.Severity);
        Assert.Contains("disk full", feedback.Dialog.Message);
    }

    [Fact]
    public void BuildCompletionUiFeedback_WithoutErrorsAndBatch_ReturnsBatchCompletionStatus()
    {
        var service = new OrderRunFeedbackService();

        var feedback = service.BuildCompletionUiFeedback(
            errors: new List<OrderRunExecutionError>(),
            runnableOrdersCount: 3,
            orderDisplayIdResolver: _ => "order");

        Assert.Contains("3", feedback.BottomStatus);
        Assert.Null(feedback.Dialog);
    }

    [Fact]
    public void BuildRunCommandStartLifecycleUiFeedback_ReturnsInfoStartLog()
    {
        var service = new OrderRunFeedbackService();

        var feedback = service.BuildRunCommandStartLifecycleUiFeedback();

        var log = Assert.Single(feedback.Logs);
        Assert.Equal("RUN | command-start", log.Message);
        Assert.False(log.IsWarning);
    }

    [Fact]
    public void BuildRunStopCommandStartLifecycleUiFeedback_ReturnsInfoStopStartLog()
    {
        var service = new OrderRunFeedbackService();

        var feedback = service.BuildRunStopCommandStartLifecycleUiFeedback();

        var log = Assert.Single(feedback.Logs);
        Assert.Equal("RUN | stop-command-start", log.Message);
        Assert.False(log.IsWarning);
    }

    [Fact]
    public void BuildRunSnapshotRefreshWarningUiFeedback_WithOrder_ReturnsWarningLog()
    {
        var service = new OrderRunFeedbackService();

        var feedback = service.BuildRunSnapshotRefreshWarningUiFeedback("run-stop", "00526");

        var log = Assert.Single(feedback.Logs);
        Assert.Equal("RUN | snapshot-refresh-failed | reason=run-stop | order=00526", log.Message);
        Assert.True(log.IsWarning);
    }

    [Fact]
    public void BuildRunCommandFinishLifecycleUiFeedback_NormalizesCountsAndReturnsInfoLog()
    {
        var service = new OrderRunFeedbackService();

        var feedback = service.BuildRunCommandFinishLifecycleUiFeedback(startedCount: -1, errorsCount: 2);

        var log = Assert.Single(feedback.Logs);
        Assert.Equal("RUN | command-finish | started=0 | errors=2", log.Message);
        Assert.False(log.IsWarning);
    }
}
