using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Replica;

public sealed class OrderItemAddPreparationResult
{
    public OrderItemAddPreparationResult(
        bool wasMultiOrderBeforeMutation,
        OrderFileItem item,
        string sourcePath,
        bool topologyChangedBeforeAdd,
        IReadOnlyList<string> topologyIssuesBeforeAdd)
    {
        WasMultiOrderBeforeMutation = wasMultiOrderBeforeMutation;
        Item = item ?? throw new ArgumentNullException(nameof(item));
        SourcePath = sourcePath ?? string.Empty;
        TopologyChangedBeforeAdd = topologyChangedBeforeAdd;
        TopologyIssuesBeforeAdd = topologyIssuesBeforeAdd ?? [];
    }

    public bool WasMultiOrderBeforeMutation { get; }
    public OrderFileItem Item { get; }
    public string SourcePath { get; }
    public bool TopologyChangedBeforeAdd { get; }
    public IReadOnlyList<string> TopologyIssuesBeforeAdd { get; }
}

public sealed class OrderItemTopologyMutationResult
{
    public OrderItemTopologyMutationResult(
        OrderTopologyNormalizationResult normalization,
        bool wasMultiOrderBeforeMutation,
        bool isMultiOrderNow)
    {
        Normalization = normalization ?? throw new ArgumentNullException(nameof(normalization));
        WasMultiOrderBeforeMutation = wasMultiOrderBeforeMutation;
        IsMultiOrderNow = isMultiOrderNow;
    }

    public OrderTopologyNormalizationResult Normalization { get; }
    public bool WasMultiOrderBeforeMutation { get; }
    public bool IsMultiOrderNow { get; }
    public bool PromotedToMultiOrder => !WasMultiOrderBeforeMutation && IsMultiOrderNow;
    public bool DemotedToSingleOrder => WasMultiOrderBeforeMutation && !IsMultiOrderNow;
}

public sealed class OrderItemMutationService
{
    private readonly Func<DateTime> _nowProvider;

    public OrderItemMutationService(Func<DateTime>? nowProvider = null)
    {
        _nowProvider = nowProvider ?? (() => DateTime.Now);
    }

    public OrderItemAddPreparationResult PrepareAddItem(
        OrderData order,
        string sourcePath,
        string pitStopAction,
        string imposingAction)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        var wasMultiOrderBeforeMutation = OrderTopologyService.IsMultiOrder(order);
        var normalization = OrderTopologyService.Normalize(order);

        order.Items ??= [];
        var nextSequenceNo = order.Items
            .Where(x => x != null)
            .Select(x => x.SequenceNo)
            .DefaultIfEmpty(-1L)
            .Max() + 1L;

        var cleanSourcePath = CleanPath(sourcePath);
        var item = new OrderFileItem
        {
            ItemId = Guid.NewGuid().ToString("N"),
            SequenceNo = nextSequenceNo,
            ClientFileLabel = Path.GetFileNameWithoutExtension(cleanSourcePath),
            PitStopAction = string.IsNullOrWhiteSpace(pitStopAction) ? "-" : pitStopAction,
            ImposingAction = string.IsNullOrWhiteSpace(imposingAction) ? "-" : imposingAction,
            FileStatus = WorkflowStatusNames.Waiting,
            UpdatedAt = _nowProvider()
        };

        order.Items.Add(item);
        return new OrderItemAddPreparationResult(
            wasMultiOrderBeforeMutation,
            item,
            cleanSourcePath,
            normalization.Changed,
            normalization.Issues);
    }

    public bool RollbackPreparedItem(OrderData order, OrderFileItem item)
    {
        if (order?.Items == null || item == null || string.IsNullOrWhiteSpace(item.ItemId))
            return false;

        var removed = order.Items.RemoveAll(x => x != null && string.Equals(x.ItemId, item.ItemId, StringComparison.Ordinal)) > 0;
        if (!removed)
            return false;

        ReindexOrderItems(order);
        return true;
    }

    public OrderItemTopologyMutationResult ApplyTopologyAfterItemMutation(OrderData order, bool wasMultiOrderBeforeMutation)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        var normalization = OrderTopologyService.Normalize(order);
        var isMultiOrderNow = OrderTopologyService.IsMultiOrder(order);
        return new OrderItemTopologyMutationResult(normalization, wasMultiOrderBeforeMutation, isMultiOrderNow);
    }

    public bool ContainsOrderItem(OrderData order, string? itemId)
    {
        if (order?.Items == null || string.IsNullOrWhiteSpace(itemId))
            return false;

        return order.Items.Any(x => x != null && string.Equals(x.ItemId, itemId, StringComparison.Ordinal));
    }

    public bool RemoveItemIfEmpty(OrderData order, OrderFileItem item)
    {
        if (order?.Items == null || item == null)
            return false;

        if (!IsItemEmpty(item))
            return false;

        var removed = order.Items.RemoveAll(x => x != null && string.Equals(x.ItemId, item.ItemId, StringComparison.Ordinal)) > 0;
        if (!removed)
            return false;

        ReindexOrderItems(order);
        return true;
    }

    private static bool IsItemEmpty(OrderFileItem? item)
    {
        if (item == null)
            return true;

        var hasStagePaths = !string.IsNullOrWhiteSpace(CleanPath(item.SourcePath))
                            || !string.IsNullOrWhiteSpace(CleanPath(item.PreparedPath))
                            || !string.IsNullOrWhiteSpace(CleanPath(item.PrintPath));
        if (hasStagePaths)
            return false;

        return item.TechnicalFiles == null || item.TechnicalFiles.All(x => string.IsNullOrWhiteSpace(CleanPath(x)));
    }

    private static void ReindexOrderItems(OrderData order)
    {
        if (order?.Items == null || order.Items.Count == 0)
            return;

        var orderedItems = order.Items
            .Where(x => x != null)
            .OrderBy(x => x.SequenceNo)
            .ToList();
        for (var index = 0; index < orderedItems.Count; index++)
            orderedItems[index].SequenceNo = index;
    }

    private static string CleanPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Trim('"');
    }
}
