using System;
using System.Collections.Generic;
using System.Threading;

namespace Replica;

public sealed class OrderDeleteCommandResult
{
    public OrderDeleteCommandResult(OrderDeleteBatchResult deleteResult, int cancelledRunsCount)
    {
        DeleteResult = deleteResult ?? throw new ArgumentNullException(nameof(deleteResult));
        CancelledRunsCount = cancelledRunsCount;
    }

    public OrderDeleteBatchResult DeleteResult { get; }
    public int CancelledRunsCount { get; }
}

public sealed class OrderDeleteCommandService
{
    private readonly OrderDeletionWorkflowService _deletionWorkflowService;

    public OrderDeleteCommandService(OrderDeletionWorkflowService? deletionWorkflowService = null)
    {
        _deletionWorkflowService = deletionWorkflowService ?? new OrderDeletionWorkflowService();
    }

    public OrderDeleteCommandResult Execute(
        IList<OrderData> orderHistory,
        IReadOnlyCollection<OrderData> selectedOrders,
        bool removeFilesFromDisk,
        string ordersRootPath,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        ISet<string> expandedOrderIds,
        Action<OrderData, bool> onOrderRemoved)
    {
        if (orderHistory == null)
            throw new ArgumentNullException(nameof(orderHistory));
        if (selectedOrders == null)
            throw new ArgumentNullException(nameof(selectedOrders));
        if (runTokensByOrder == null)
            throw new ArgumentNullException(nameof(runTokensByOrder));
        if (runProgressByOrderInternalId == null)
            throw new ArgumentNullException(nameof(runProgressByOrderInternalId));
        if (expandedOrderIds == null)
            throw new ArgumentNullException(nameof(expandedOrderIds));
        if (onOrderRemoved == null)
            throw new ArgumentNullException(nameof(onOrderRemoved));

        var cancelledRunsCount = 0;
        var deleteResult = _deletionWorkflowService.DeleteOrders(
            orderHistory,
            selectedOrders,
            removeFilesFromDisk,
            ordersRootPath,
            order =>
            {
                if (runTokensByOrder.TryGetValue(order.InternalId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                    runTokensByOrder.Remove(order.InternalId);
                    cancelledRunsCount++;
                }

                runProgressByOrderInternalId.Remove(order.InternalId);
                expandedOrderIds.Remove(order.InternalId);
                onOrderRemoved(order, removeFilesFromDisk);
            });

        return new OrderDeleteCommandResult(deleteResult, cancelledRunsCount);
    }
}
