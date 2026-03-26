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

        Assert.Equal(
            "order-1: locked" + System.Environment.NewLine
            + "order-2: not found" + System.Environment.NewLine
            + "... ещё: 1",
            preview);
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

        Assert.Equal(
            "100: disk full" + System.Environment.NewLine
            + "200: неизвестная ошибка",
            preview);
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
        Assert.Equal("Сервер недоступен: api unavailable", feedback.BottomStatus);
        Assert.NotNull(feedback.Dialog);
        Assert.Equal(OrderRunFeedbackSeverity.Warning, feedback.Dialog!.Severity);
        Assert.Single(feedback.Logs);
        Assert.True(feedback.Logs[0].IsWarning);
        Assert.Contains("RUN | command-fatal", feedback.Logs[0].Message);
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
        Assert.Equal("Сервер не подтвердил запуск выбранных заказов", feedback.BottomStatus);
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

        Assert.Equal("Заказ 00526 сейчас не выполняется", feedback.BottomStatus);
        Assert.NotNull(feedback.Dialog);
        Assert.Equal(OrderRunFeedbackSeverity.Information, feedback.Dialog!.Severity);
        Assert.False(feedback.ShouldUpdateActionButtons);
        Assert.Single(feedback.Logs);
        Assert.True(feedback.Logs[0].IsWarning);
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

        Assert.Equal("Остановлен заказ 00526", feedback.BottomStatus);
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

        Assert.Contains("Часть заказов пропущена", feedback.BottomStatus);
        Assert.NotNull(feedback.Dialog);
        Assert.Contains("order-1: locked", feedback.Dialog!.Message);
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

        Assert.Equal("Ошибок запуска: 1", feedback.BottomStatus);
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

        Assert.Equal("Пакетная обработка завершена: 3", feedback.BottomStatus);
        Assert.Null(feedback.Dialog);
    }
}
