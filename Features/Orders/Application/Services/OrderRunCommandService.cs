using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Replica;

public enum OrderRunStartPhaseStatus
{
    Fatal = 0,
    NoRunnable = 1,
    ServerRejected = 2,
    ReadyToExecute = 3
}

public enum OrderRunStopPhaseStatus
{
    NotRunning = 0,
    LocalStatusApplied = 1,
    Conflict = 2,
    Unconfirmed = 3
}

public sealed class OrderRunStartPhaseResult
{
    private OrderRunStartPhaseResult(
        OrderRunStartPhaseStatus status,
        RunStartPreparationResult preparation,
        List<OrderRunStateService.RunSession> runSessions,
        string noRunnableDetails)
    {
        Status = status;
        Preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
        RunSessions = runSessions ?? [];
        NoRunnableDetails = noRunnableDetails ?? string.Empty;
    }

    public OrderRunStartPhaseStatus Status { get; }
    public RunStartPreparationResult Preparation { get; }
    public List<OrderRunStateService.RunSession> RunSessions { get; }
    public string NoRunnableDetails { get; }

    public static OrderRunStartPhaseResult Fatal(RunStartPreparationResult preparation)
    {
        return new OrderRunStartPhaseResult(
            OrderRunStartPhaseStatus.Fatal,
            preparation,
            runSessions: [],
            noRunnableDetails: string.Empty);
    }

    public static OrderRunStartPhaseResult NoRunnable(
        RunStartPreparationResult preparation,
        string noRunnableDetails)
    {
        return new OrderRunStartPhaseResult(
            OrderRunStartPhaseStatus.NoRunnable,
            preparation,
            runSessions: [],
            noRunnableDetails);
    }

    public static OrderRunStartPhaseResult ServerRejected(RunStartPreparationResult preparation)
    {
        return new OrderRunStartPhaseResult(
            OrderRunStartPhaseStatus.ServerRejected,
            preparation,
            runSessions: [],
            noRunnableDetails: string.Empty);
    }

    public static OrderRunStartPhaseResult Ready(
        RunStartPreparationResult preparation,
        List<OrderRunStateService.RunSession> runSessions)
    {
        return new OrderRunStartPhaseResult(
            OrderRunStartPhaseStatus.ReadyToExecute,
            preparation,
            runSessions,
            noRunnableDetails: string.Empty);
    }
}

public sealed class OrderRunStopPhaseResult
{
    private OrderRunStopPhaseResult(
        OrderRunStopPhaseStatus status,
        RunStopPreparationResult preparation,
        bool shouldWarnServerUnavailable,
        bool shouldLogServerFailure,
        string serverReason)
    {
        Status = status;
        Preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
        ShouldWarnServerUnavailable = shouldWarnServerUnavailable;
        ShouldLogServerFailure = shouldLogServerFailure;
        ServerReason = serverReason ?? string.Empty;
    }

    public OrderRunStopPhaseStatus Status { get; }
    public RunStopPreparationResult Preparation { get; }
    public bool ShouldWarnServerUnavailable { get; }
    public bool ShouldLogServerFailure { get; }
    public string ServerReason { get; }

    public static OrderRunStopPhaseResult NotRunning(RunStopPreparationResult preparation)
    {
        return new OrderRunStopPhaseResult(
            OrderRunStopPhaseStatus.NotRunning,
            preparation,
            shouldWarnServerUnavailable: false,
            shouldLogServerFailure: false,
            serverReason: string.Empty);
    }

    public static OrderRunStopPhaseResult LocalStatusApplied(
        RunStopPreparationResult preparation,
        bool shouldWarnServerUnavailable,
        bool shouldLogServerFailure,
        string serverReason)
    {
        return new OrderRunStopPhaseResult(
            OrderRunStopPhaseStatus.LocalStatusApplied,
            preparation,
            shouldWarnServerUnavailable,
            shouldLogServerFailure,
            serverReason);
    }

    public static OrderRunStopPhaseResult Conflict(
        RunStopPreparationResult preparation,
        string serverReason)
    {
        return new OrderRunStopPhaseResult(
            OrderRunStopPhaseStatus.Conflict,
            preparation,
            shouldWarnServerUnavailable: false,
            shouldLogServerFailure: false,
            serverReason);
    }

    public static OrderRunStopPhaseResult Unconfirmed(
        RunStopPreparationResult preparation,
        bool shouldLogServerFailure,
        string serverReason)
    {
        return new OrderRunStopPhaseResult(
            OrderRunStopPhaseStatus.Unconfirmed,
            preparation,
            shouldWarnServerUnavailable: false,
            shouldLogServerFailure,
            serverReason);
    }
}

public sealed class OrderRunCommandService
{
    private readonly OrderRunWorkflowOrchestrationService _workflowOrchestrationService;
    private readonly OrderRunStateService _orderRunStateService;
    private readonly OrderRunExecutionService _orderRunExecutionService;

    public OrderRunCommandService(
        OrderRunWorkflowOrchestrationService workflowOrchestrationService,
        OrderRunStateService orderRunStateService,
        OrderRunExecutionService orderRunExecutionService)
    {
        _workflowOrchestrationService = workflowOrchestrationService ?? throw new ArgumentNullException(nameof(workflowOrchestrationService));
        _orderRunStateService = orderRunStateService ?? throw new ArgumentNullException(nameof(orderRunStateService));
        _orderRunExecutionService = orderRunExecutionService ?? throw new ArgumentNullException(nameof(orderRunExecutionService));
    }

    public async Task<OrderRunStartPhaseResult> PrepareAndBeginAsync(
        IReadOnlyCollection<OrderData> selectedOrders,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        Func<OrderData, string> orderDisplayIdResolver,
        Func<IReadOnlyCollection<OrderData>, string, bool>? tryRefreshSnapshotFromStorage,
        CancellationToken cancellationToken = default)
    {
        if (selectedOrders == null)
            throw new ArgumentNullException(nameof(selectedOrders));
        if (runTokensByOrder == null)
            throw new ArgumentNullException(nameof(runTokensByOrder));
        if (runProgressByOrderInternalId == null)
            throw new ArgumentNullException(nameof(runProgressByOrderInternalId));
        if (orderDisplayIdResolver == null)
            throw new ArgumentNullException(nameof(orderDisplayIdResolver));

        var readOnlyRunTokens = runTokensByOrder as IReadOnlyDictionary<string, CancellationTokenSource>
            ?? new Dictionary<string, CancellationTokenSource>(runTokensByOrder);

        var preparation = await _workflowOrchestrationService.PrepareStartAsync(
            selectedOrders,
            readOnlyRunTokens,
            useLanApi,
            lanApiBaseUrl,
            actor,
            orderDisplayIdResolver,
            tryRefreshSnapshotFromStorage,
            cancellationToken).ConfigureAwait(false);

        if (preparation.IsFatal)
            return OrderRunStartPhaseResult.Fatal(preparation);

        if (preparation.RunPlan.RunnableOrders.Count == 0)
        {
            var noRunnableDetails = OrderRunStateService.BuildNoRunnableDetails(preparation.RunPlan);
            return OrderRunStartPhaseResult.NoRunnable(preparation, noRunnableDetails);
        }

        if (preparation.UsedLanApi && preparation.RunnableOrders.Count == 0)
            return OrderRunStartPhaseResult.ServerRejected(preparation);

        var runSessions = _orderRunStateService.BeginRunSessions(
            preparation.RunnableOrders,
            runTokensByOrder,
            runProgressByOrderInternalId);

        return OrderRunStartPhaseResult.Ready(preparation, runSessions);
    }

    public Task<OrderRunExecutionResult> ExecuteAsync(
        IReadOnlyCollection<OrderRunStateService.RunSession> runSessions,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        Func<OrderData, CancellationToken, Task> runOrderAsync,
        Action<OrderData> onCancelled,
        Action<OrderData, Exception> onFailed,
        Action<OrderData> onCompleted)
    {
        if (runSessions == null)
            throw new ArgumentNullException(nameof(runSessions));
        if (runTokensByOrder == null)
            throw new ArgumentNullException(nameof(runTokensByOrder));
        if (runProgressByOrderInternalId == null)
            throw new ArgumentNullException(nameof(runProgressByOrderInternalId));
        if (runOrderAsync == null)
            throw new ArgumentNullException(nameof(runOrderAsync));
        if (onCancelled == null)
            throw new ArgumentNullException(nameof(onCancelled));
        if (onFailed == null)
            throw new ArgumentNullException(nameof(onFailed));
        if (onCompleted == null)
            throw new ArgumentNullException(nameof(onCompleted));

        return _orderRunExecutionService.ExecuteAsync(
            runSessions,
            runOrderAsync,
            onCancelled,
            onFailed,
            onCompleted: order =>
            {
                _orderRunStateService.CompleteRunSession(
                    order,
                    runTokensByOrder,
                    runProgressByOrderInternalId);
                onCompleted(order);
            });
    }

    public async Task<OrderRunStopPhaseResult> ExecuteStopAsync(
        OrderData order,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        Func<IReadOnlyCollection<OrderData>, string, bool>? tryRefreshSnapshotFromStorage,
        Action<OrderData>? applyLocalStopStatus = null,
        CancellationToken cancellationToken = default)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));
        if (runTokensByOrder == null)
            throw new ArgumentNullException(nameof(runTokensByOrder));
        if (runProgressByOrderInternalId == null)
            throw new ArgumentNullException(nameof(runProgressByOrderInternalId));

        var stopPreparation = await _workflowOrchestrationService.PrepareStopAsync(
            order: order,
            useLanApi: useLanApi,
            lanApiBaseUrl: lanApiBaseUrl,
            actor: actor,
            runTokensByOrder: runTokensByOrder,
            runProgressByOrderInternalId: runProgressByOrderInternalId,
            tryRefreshSnapshotFromStorage: tryRefreshSnapshotFromStorage,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!stopPreparation.CanProceed)
            return OrderRunStopPhaseResult.NotRunning(stopPreparation);

        var stopCommandResult = stopPreparation.StopCommandResult;
        var stopResult = stopCommandResult.UsedLanApi ? stopCommandResult.ApiResult : null;
        var stopReason = string.IsNullOrWhiteSpace(stopResult?.Error)
            ? "сервер не подтвердил остановку"
            : stopResult!.Error;

        var shouldWarnServerUnavailable = stopCommandResult.UsedLanApi
            && stopResult is { IsSuccess: false, IsUnavailable: true };

        var shouldLogServerFailure = stopCommandResult.UsedLanApi
            && stopResult is { IsSuccess: false, IsNotFound: false, IsBadRequest: false, IsConflict: false, IsUnavailable: false };

        if (stopPreparation.CanApplyLocalStopStatus)
        {
            applyLocalStopStatus?.Invoke(order);
            return OrderRunStopPhaseResult.LocalStatusApplied(
                stopPreparation,
                shouldWarnServerUnavailable,
                shouldLogServerFailure,
                stopReason);
        }

        if (stopCommandResult.UsedLanApi && stopResult?.IsConflict == true)
            return OrderRunStopPhaseResult.Conflict(stopPreparation, stopReason);

        return OrderRunStopPhaseResult.Unconfirmed(
            stopPreparation,
            shouldLogServerFailure,
            stopReason);
    }
}
