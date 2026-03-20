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
}
