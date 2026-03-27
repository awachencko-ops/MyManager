using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Replica;

public sealed class OrderRunStateService
{
    public RunPlan BuildRunPlan(
        IReadOnlyCollection<OrderData> selectedOrders,
        IReadOnlyDictionary<string, CancellationTokenSource> runningTokens,
        bool useLocalRunState = true)
    {
        var safeSelected = selectedOrders ?? Array.Empty<OrderData>();
        var safeRunning = runningTokens ?? new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);

        // Order number remains a mandatory precondition for run-start in both local and LAN paths.
        var ordersWithoutNumber = safeSelected
            .Where(order => order != null && string.IsNullOrWhiteSpace(order.Id))
            .ToList();

        var alreadyRunningOrders = useLocalRunState
            ? safeSelected
                .Where(order => order != null && !string.IsNullOrWhiteSpace(order.InternalId) && safeRunning.ContainsKey(order.InternalId))
                .ToList()
            : new List<OrderData>();

        var runnableOrders = safeSelected
            .Where(order => order != null)
            .Except(ordersWithoutNumber)
            .Except(alreadyRunningOrders)
            .Distinct()
            .ToList();

        return new RunPlan(runnableOrders, ordersWithoutNumber, alreadyRunningOrders);
    }

    public List<RunSession> BeginRunSessions(
        IReadOnlyCollection<OrderData> runnableOrders,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId)
    {
        var sessions = new List<RunSession>();
        if (runnableOrders == null || runnableOrders.Count == 0)
            return sessions;

        foreach (var order in runnableOrders.Where(order => order != null))
        {
            var cts = new CancellationTokenSource();
            runTokensByOrder[order.InternalId] = cts;
            runProgressByOrderInternalId[order.InternalId] = 0;
            sessions.Add(new RunSession(order, cts));
        }

        return sessions;
    }

    public bool TryStopOrder(
        OrderData order,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        out CancellationTokenSource cancellationTokenSource)
    {
        cancellationTokenSource = null!;
        if (order == null || string.IsNullOrWhiteSpace(order.InternalId))
            return false;

        if (!runTokensByOrder.TryGetValue(order.InternalId, out var cts))
            return false;

        runTokensByOrder.Remove(order.InternalId);
        runProgressByOrderInternalId.Remove(order.InternalId);
        cancellationTokenSource = cts;
        return true;
    }

    public StopPlan BuildStopPlan(
        OrderData order,
        bool useLanApi,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId)
    {
        if (order == null || string.IsNullOrWhiteSpace(order.InternalId))
            return StopPlan.InvalidOrder();

        var hasLocalRunSession = TryStopOrder(
            order,
            runTokensByOrder,
            runProgressByOrderInternalId,
            out var localCancellationTokenSource);

        var canProceed = hasLocalRunSession || useLanApi;
        return new StopPlan(
            canProceed,
            hasLocalRunSession,
            useLanApi,
            localCancellationTokenSource);
    }

    public void CompleteRunSession(
        OrderData order,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId)
    {
        if (order == null || string.IsNullOrWhiteSpace(order.InternalId))
            return;

        runTokensByOrder.Remove(order.InternalId);
        runProgressByOrderInternalId.Remove(order.InternalId);
    }

    public static string BuildNoRunnableDetails(RunPlan plan)
    {
        var reasons = new List<string>();
        if (plan.OrdersWithoutNumber.Count > 0)
            reasons.Add($"без номера: {plan.OrdersWithoutNumber.Count}");
        if (plan.AlreadyRunningOrders.Count > 0)
            reasons.Add($"уже запущены: {plan.AlreadyRunningOrders.Count}");

        return reasons.Count == 0
            ? "не удалось определить причину"
            : string.Join(", ", reasons);
    }

    public static string BuildSkippedDetails(RunPlan plan)
    {
        var skippedReasons = new List<string>();
        if (plan.OrdersWithoutNumber.Count > 0)
            skippedReasons.Add($"без номера: {plan.OrdersWithoutNumber.Count}");
        if (plan.AlreadyRunningOrders.Count > 0)
            skippedReasons.Add($"уже запущены: {plan.AlreadyRunningOrders.Count}");

        return string.Join(", ", skippedReasons);
    }

    public sealed record RunPlan(
        List<OrderData> RunnableOrders,
        List<OrderData> OrdersWithoutNumber,
        List<OrderData> AlreadyRunningOrders);

    public sealed record RunSession(OrderData Order, CancellationTokenSource Cts);

    public sealed record StopPlan(
        bool CanProceed,
        bool HasLocalRunSession,
        bool ShouldSendServerStop,
        CancellationTokenSource? LocalCancellationTokenSource)
    {
        public static StopPlan InvalidOrder()
        {
            return new StopPlan(
                CanProceed: false,
                HasLocalRunSession: false,
                ShouldSendServerStop: false,
                LocalCancellationTokenSource: null);
        }
    }
}
