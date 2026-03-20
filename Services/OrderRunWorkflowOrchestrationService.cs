using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Replica;

public sealed class OrderRunWorkflowOrchestrationService
{
    private readonly OrderRunStateService _orderRunStateService;
    private readonly LanRunCommandCoordinator _lanRunCommandCoordinator;

    public OrderRunWorkflowOrchestrationService(
        OrderRunStateService orderRunStateService,
        LanRunCommandCoordinator lanRunCommandCoordinator)
    {
        _orderRunStateService = orderRunStateService ?? throw new ArgumentNullException(nameof(orderRunStateService));
        _lanRunCommandCoordinator = lanRunCommandCoordinator ?? throw new ArgumentNullException(nameof(lanRunCommandCoordinator));
    }

    public async Task<RunStartPreparationResult> PrepareStartAsync(
        IReadOnlyCollection<OrderData> selectedOrders,
        IReadOnlyDictionary<string, CancellationTokenSource> runTokensByOrder,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        Func<OrderData, string> orderDisplayIdResolver,
        Func<IReadOnlyCollection<OrderData>, string, bool>? tryRefreshSnapshotFromStorage,
        CancellationToken cancellationToken = default)
    {
        var runPlan = _orderRunStateService.BuildRunPlan(
            selectedOrders,
            runTokensByOrder,
            useLocalRunState: !useLanApi);

        if (runPlan.RunnableOrders.Count == 0)
            return RunStartPreparationResult.From(runPlan, runPlan.RunnableOrders, skippedByServer: [], useLanApi, snapshotRefreshFailed: false);

        var lanRunBatchResult = await _lanRunCommandCoordinator.TryStartRunsAsync(
            runPlan.RunnableOrders,
            useLanApi: useLanApi,
            lanApiBaseUrl: lanApiBaseUrl,
            actor: actor,
            orderDisplayIdResolver: orderDisplayIdResolver,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (lanRunBatchResult.IsFatal)
            return RunStartPreparationResult.Fatal(runPlan, lanRunBatchResult.FatalError);

        var approvedOrders = lanRunBatchResult.ApprovedOrders;
        var skippedByServer = lanRunBatchResult.SkippedByServer;

        var snapshotRefreshFailed = false;
        if (lanRunBatchResult.UsedLanApi
            && approvedOrders.Count > 0
            && tryRefreshSnapshotFromStorage != null)
        {
            snapshotRefreshFailed = !tryRefreshSnapshotFromStorage(approvedOrders, "run-start");
        }

        return RunStartPreparationResult.From(
            runPlan,
            approvedOrders,
            skippedByServer,
            lanRunBatchResult.UsedLanApi,
            snapshotRefreshFailed);
    }

    public async Task<RunStopPreparationResult> PrepareStopAsync(
        OrderData? order,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        Func<IReadOnlyCollection<OrderData>, string, bool>? tryRefreshSnapshotFromStorage,
        CancellationToken cancellationToken = default)
    {
        if (order == null)
            return RunStopPreparationResult.NotRunning(OrderRunStateService.StopPlan.InvalidOrder());

        var stopPlan = _orderRunStateService.BuildStopPlan(
            order,
            useLanApi,
            runTokensByOrder,
            runProgressByOrderInternalId);

        if (!stopPlan.CanProceed)
            return RunStopPreparationResult.NotRunning(stopPlan);

        var localCancellationRequested = false;
        if (stopPlan.LocalCancellationTokenSource != null)
        {
            stopPlan.LocalCancellationTokenSource.Cancel();
            localCancellationRequested = true;
        }

        var stopCommandResult = await _lanRunCommandCoordinator.TryStopRunAsync(
            order,
            useLanApi: stopPlan.ShouldSendServerStop,
            lanApiBaseUrl: lanApiBaseUrl,
            actor: actor,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var canApplyLocalStopStatus = stopPlan.HasLocalRunSession;
        var snapshotRefreshFailed = false;

        if (stopCommandResult.UsedLanApi && stopCommandResult.ApiResult != null && stopCommandResult.ApiResult.IsSuccess)
        {
            canApplyLocalStopStatus = true;
            if (tryRefreshSnapshotFromStorage != null)
                snapshotRefreshFailed = !tryRefreshSnapshotFromStorage(new[] { order }, "run-stop");
        }

        return RunStopPreparationResult.From(
            stopPlan,
            stopCommandResult,
            canApplyLocalStopStatus,
            localCancellationRequested,
            snapshotRefreshFailed);
    }
}

public sealed class RunStartPreparationResult
{
    private RunStartPreparationResult(
        bool isFatal,
        string fatalError,
        OrderRunStateService.RunPlan runPlan,
        List<OrderData> runnableOrders,
        List<string> skippedByServer,
        bool usedLanApi,
        bool snapshotRefreshFailed)
    {
        IsFatal = isFatal;
        FatalError = fatalError ?? string.Empty;
        RunPlan = runPlan;
        RunnableOrders = runnableOrders ?? [];
        SkippedByServer = skippedByServer ?? [];
        UsedLanApi = usedLanApi;
        SnapshotRefreshFailed = snapshotRefreshFailed;
    }

    public bool IsFatal { get; }
    public string FatalError { get; }
    public OrderRunStateService.RunPlan RunPlan { get; }
    public List<OrderData> RunnableOrders { get; }
    public List<string> SkippedByServer { get; }
    public bool UsedLanApi { get; }
    public bool SnapshotRefreshFailed { get; }

    public static RunStartPreparationResult Fatal(OrderRunStateService.RunPlan runPlan, string fatalError)
    {
        return new RunStartPreparationResult(
            isFatal: true,
            fatalError,
            runPlan,
            runnableOrders: [],
            skippedByServer: [],
            usedLanApi: true,
            snapshotRefreshFailed: false);
    }

    public static RunStartPreparationResult From(
        OrderRunStateService.RunPlan runPlan,
        List<OrderData> runnableOrders,
        List<string> skippedByServer,
        bool usedLanApi,
        bool snapshotRefreshFailed)
    {
        return new RunStartPreparationResult(
            isFatal: false,
            fatalError: string.Empty,
            runPlan,
            runnableOrders,
            skippedByServer,
            usedLanApi,
            snapshotRefreshFailed);
    }
}

public sealed class RunStopPreparationResult
{
    private RunStopPreparationResult(
        bool canProceed,
        OrderRunStateService.StopPlan stopPlan,
        LanRunStopCommandResult stopCommandResult,
        bool canApplyLocalStopStatus,
        bool localCancellationRequested,
        bool snapshotRefreshFailed)
    {
        CanProceed = canProceed;
        StopPlan = stopPlan;
        StopCommandResult = stopCommandResult;
        CanApplyLocalStopStatus = canApplyLocalStopStatus;
        LocalCancellationRequested = localCancellationRequested;
        SnapshotRefreshFailed = snapshotRefreshFailed;
    }

    public bool CanProceed { get; }
    public OrderRunStateService.StopPlan StopPlan { get; }
    public LanRunStopCommandResult StopCommandResult { get; }
    public bool CanApplyLocalStopStatus { get; }
    public bool LocalCancellationRequested { get; }
    public bool SnapshotRefreshFailed { get; }

    public static RunStopPreparationResult NotRunning(OrderRunStateService.StopPlan stopPlan)
    {
        return new RunStopPreparationResult(
            canProceed: false,
            stopPlan,
            LanRunStopCommandResult.NotUsed(),
            canApplyLocalStopStatus: false,
            localCancellationRequested: false,
            snapshotRefreshFailed: false);
    }

    public static RunStopPreparationResult From(
        OrderRunStateService.StopPlan stopPlan,
        LanRunStopCommandResult stopCommandResult,
        bool canApplyLocalStopStatus,
        bool localCancellationRequested,
        bool snapshotRefreshFailed)
    {
        return new RunStopPreparationResult(
            canProceed: true,
            stopPlan,
            stopCommandResult,
            canApplyLocalStopStatus,
            localCancellationRequested,
            snapshotRefreshFailed);
    }
}
