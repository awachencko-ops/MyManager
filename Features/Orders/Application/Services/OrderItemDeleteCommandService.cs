using System;
using System.Collections.Generic;

namespace Replica;

public sealed record OrderItemDeleteTopologyMutation(
    OrderData Order,
    bool WasMultiBeforeMutation,
    OrderItemTopologyMutationResult MutationResult);

public sealed class OrderItemDeleteCommandResult
{
    public OrderItemDeleteCommandResult(
        OrderItemDeleteBatchResult deleteResult,
        IReadOnlyList<OrderItemDeleteTopologyMutation> topologyMutations)
    {
        DeleteResult = deleteResult ?? throw new ArgumentNullException(nameof(deleteResult));
        TopologyMutations = topologyMutations ?? [];
    }

    public OrderItemDeleteBatchResult DeleteResult { get; }
    public IReadOnlyList<OrderItemDeleteTopologyMutation> TopologyMutations { get; }
}

public sealed class OrderItemDeleteCommandService
{
    private readonly OrderDeletionWorkflowService _deletionWorkflowService;
    private readonly OrderItemMutationService _itemMutationService;

    public OrderItemDeleteCommandService(
        OrderDeletionWorkflowService? deletionWorkflowService = null,
        OrderItemMutationService? itemMutationService = null)
    {
        _deletionWorkflowService = deletionWorkflowService ?? new OrderDeletionWorkflowService();
        _itemMutationService = itemMutationService ?? new OrderItemMutationService();
    }

    public OrderItemDeleteCommandResult Execute(
        IReadOnlyCollection<OrderItemSelection> selectedOrderItems,
        bool removeFilesFromDisk,
        Action<OrderData, OrderFileItem, string> onItemRemoved)
    {
        if (selectedOrderItems == null)
            throw new ArgumentNullException(nameof(selectedOrderItems));
        if (onItemRemoved == null)
            throw new ArgumentNullException(nameof(onItemRemoved));

        var affectedOrders = CaptureAffectedOrders(selectedOrderItems);
        var deleteResult = _deletionWorkflowService.DeleteOrderItems(
            selectedOrderItems,
            removeFilesFromDisk,
            onItemRemoved);

        var topologyMutations = new List<OrderItemDeleteTopologyMutation>(affectedOrders.Count);
        foreach (var (_, payload) in affectedOrders)
        {
            var mutationResult = _itemMutationService.ApplyTopologyAfterItemMutation(
                payload.Order,
                payload.WasMultiBeforeMutation);
            topologyMutations.Add(new OrderItemDeleteTopologyMutation(
                payload.Order,
                payload.WasMultiBeforeMutation,
                mutationResult));
        }

        return new OrderItemDeleteCommandResult(deleteResult, topologyMutations);
    }

    private static Dictionary<string, (OrderData Order, bool WasMultiBeforeMutation)> CaptureAffectedOrders(
        IReadOnlyCollection<OrderItemSelection> selectedOrderItems)
    {
        var affectedOrders = new Dictionary<string, (OrderData Order, bool WasMultiBeforeMutation)>(StringComparer.Ordinal);
        foreach (var selection in selectedOrderItems)
        {
            var order = selection.Order;
            var item = selection.Item;
            if (order == null || item == null)
                continue;

            if (!affectedOrders.ContainsKey(order.InternalId))
                affectedOrders[order.InternalId] = (order, OrderTopologyService.IsMultiOrder(order));
        }

        return affectedOrders;
    }
}
