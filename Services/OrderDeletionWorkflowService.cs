using System;
using System.Collections.Generic;
using System.IO;

namespace Replica;

public sealed class OrderDeletionWorkflowService
{
    private const string DefaultItemDisplayName = "\u0444\u0430\u0439\u043b";
    private const string ItemNotFoundMessage = "\u0444\u0430\u0439\u043b \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d";
    private const string DeleteFileErrorPrefix = "Failed to delete file";

    public OrderDeleteBatchResult DeleteOrders(
        IList<OrderData> orderHistory,
        IReadOnlyCollection<OrderData> selectedOrders,
        bool removeFilesFromDisk,
        string ordersRootPath,
        Action<OrderData> onOrderRemoved)
    {
        if (orderHistory == null)
            throw new ArgumentNullException(nameof(orderHistory));
        if (selectedOrders == null)
            throw new ArgumentNullException(nameof(selectedOrders));
        if (onOrderRemoved == null)
            throw new ArgumentNullException(nameof(onOrderRemoved));

        var removedOrders = new List<OrderData>(selectedOrders.Count);
        var failedOrders = new List<OrderDeleteFailure>();

        foreach (var order in selectedOrders)
        {
            if (order == null)
                continue;

            try
            {
                if (removeFilesFromDisk)
                    DeleteOrderArtifacts(order, ordersRootPath);

                onOrderRemoved(order);
                orderHistory.Remove(order);
                removedOrders.Add(order);
            }
            catch (Exception ex)
            {
                failedOrders.Add(new OrderDeleteFailure(order, ex.Message));
            }
        }

        return new OrderDeleteBatchResult(removedOrders, failedOrders);
    }

    public OrderItemDeleteBatchResult DeleteOrderItems(
        IReadOnlyCollection<OrderItemSelection> selectedOrderItems,
        bool removeFilesFromDisk,
        Action<OrderData, OrderFileItem, string> onItemRemoved)
    {
        if (selectedOrderItems == null)
            throw new ArgumentNullException(nameof(selectedOrderItems));
        if (onItemRemoved == null)
            throw new ArgumentNullException(nameof(onItemRemoved));

        var removedItems = new List<OrderItemDeleteSuccess>(selectedOrderItems.Count);
        var failedItems = new List<OrderItemDeleteFailure>();

        foreach (var selection in selectedOrderItems)
        {
            var order = selection.Order;
            var item = selection.Item;
            if (order == null || item == null)
                continue;

            var itemDisplayName = BuildOrderItemDisplayName(item);
            try
            {
                if (removeFilesFromDisk)
                    DeleteOrderItemFiles(item);

                var removed = order.Items?.RemoveAll(x => x != null && string.Equals(x.ItemId, item.ItemId, StringComparison.Ordinal)) > 0;
                if (!removed)
                {
                    failedItems.Add(new OrderItemDeleteFailure(order, itemDisplayName, ItemNotFoundMessage));
                    continue;
                }

                ReindexOrderItems(order);
                onItemRemoved(order, item, itemDisplayName);
                removedItems.Add(new OrderItemDeleteSuccess(order, itemDisplayName));
            }
            catch (Exception ex)
            {
                failedItems.Add(new OrderItemDeleteFailure(order, itemDisplayName, ex.Message));
            }
        }

        return new OrderItemDeleteBatchResult(removedItems, failedItems);
    }

    public static string BuildOrderItemDisplayName(OrderFileItem item)
    {
        if (item == null)
            return DefaultItemDisplayName;

        if (!string.IsNullOrWhiteSpace(item.ClientFileLabel))
            return item.ClientFileLabel.Trim();

        if (!string.IsNullOrWhiteSpace(item.ItemId))
            return item.ItemId;

        return DefaultItemDisplayName;
    }

    private static void DeleteOrderArtifacts(OrderData order, string ordersRootPath)
    {
        var orderFolder = string.IsNullOrWhiteSpace(order.FolderName)
            ? string.Empty
            : Path.Combine(ordersRootPath, order.FolderName);

        if (!string.IsNullOrWhiteSpace(orderFolder) && Directory.Exists(orderFolder))
        {
            Directory.Delete(orderFolder, true);
            return;
        }

        DeleteOrderFiles(order);
    }

    private static void DeleteOrderFiles(OrderData order)
    {
        foreach (var path in GetOrderAllKnownPaths(order))
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                throw new IOException($"{DeleteFileErrorPrefix}: {path}", ex);
            }
        }
    }

    private static void DeleteOrderItemFiles(OrderFileItem item)
    {
        foreach (var path in GetOrderItemAllKnownPaths(item))
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                throw new IOException($"{DeleteFileErrorPrefix}: {path}", ex);
            }
        }
    }

    private static IEnumerable<string?> GetOrderAllKnownPaths(OrderData order)
    {
        if (order == null)
            yield break;

        yield return order.SourcePath;
        yield return order.PreparedPath;
        yield return order.PrintPath;

        if (order.Items == null)
            yield break;

        foreach (var item in order.Items)
        {
            if (item == null)
                continue;

            yield return item.SourcePath;
            yield return item.PreparedPath;
            yield return item.PrintPath;
        }
    }

    private static IEnumerable<string?> GetOrderItemAllKnownPaths(OrderFileItem item)
    {
        if (item == null)
            yield break;

        yield return item.SourcePath;
        yield return item.PreparedPath;
        yield return item.PrintPath;

        if (item.TechnicalFiles == null || item.TechnicalFiles.Count == 0)
            yield break;

        foreach (var technicalFilePath in item.TechnicalFiles)
            yield return technicalFilePath;
    }

    private static void ReindexOrderItems(OrderData order)
    {
        if (order?.Items == null || order.Items.Count == 0)
            return;

        var sequence = 0;
        foreach (var item in order.Items)
        {
            if (item == null)
                continue;

            item.SequenceNo = sequence++;
        }
    }
}

public sealed record OrderDeleteBatchResult(
    IReadOnlyList<OrderData> RemovedOrders,
    IReadOnlyList<OrderDeleteFailure> FailedOrders)
{
    public int RemovedCount => RemovedOrders.Count;
}

public sealed record OrderDeleteFailure(OrderData Order, string Message);

public sealed record OrderItemSelection(OrderData Order, OrderFileItem Item);

public sealed record OrderItemDeleteBatchResult(
    IReadOnlyList<OrderItemDeleteSuccess> RemovedItems,
    IReadOnlyList<OrderItemDeleteFailure> FailedItems)
{
    public int RemovedCount => RemovedItems.Count;
}

public sealed record OrderItemDeleteSuccess(OrderData Order, string ItemDisplayName);

public sealed record OrderItemDeleteFailure(OrderData Order, string ItemDisplayName, string Message);
