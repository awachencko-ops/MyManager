using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderDeletionWorkflowServiceTests
{
    [Fact]
    public void DeleteOrders_WhenRemovingFromDisk_DeletesFolderAndRemovesFromHistory()
    {
        var service = new OrderDeletionWorkflowService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-delete-orders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var folderName = "order-1001";
            var orderFolder = Path.Combine(tempRoot, folderName);
            Directory.CreateDirectory(orderFolder);
            File.WriteAllText(Path.Combine(orderFolder, "source.pdf"), "test");

            var order = new OrderData
            {
                InternalId = "order-1",
                Id = "1001",
                FolderName = folderName
            };
            var history = new List<OrderData> { order };
            var callbackCount = 0;

            var result = service.DeleteOrders(
                history,
                new List<OrderData> { order },
                removeFilesFromDisk: true,
                ordersRootPath: tempRoot,
                onOrderRemoved: _ => callbackCount++);

            Assert.Equal(1, result.RemovedCount);
            Assert.Empty(result.FailedOrders);
            Assert.Empty(history);
            Assert.False(Directory.Exists(orderFolder));
            Assert.Equal(1, callbackCount);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void DeleteOrders_WhenFolderMissing_FallsBackToKnownFilePaths()
    {
        var service = new OrderDeletionWorkflowService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-delete-order-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourcePath = Path.Combine(tempRoot, "source.pdf");
            File.WriteAllText(sourcePath, "file");

            var order = new OrderData
            {
                InternalId = "order-2",
                Id = "1002",
                FolderName = "missing-folder",
                SourcePath = sourcePath
            };
            var history = new List<OrderData> { order };

            var result = service.DeleteOrders(
                history,
                new List<OrderData> { order },
                removeFilesFromDisk: true,
                ordersRootPath: tempRoot,
                onOrderRemoved: _ => { });

            Assert.Equal(1, result.RemovedCount);
            Assert.Empty(result.FailedOrders);
            Assert.False(File.Exists(sourcePath));
            Assert.Empty(history);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void DeleteOrderItems_WhenSuccess_RemovesItemAndReindexes()
    {
        var service = new OrderDeletionWorkflowService();
        var order = new OrderData
        {
            InternalId = "order-3",
            Id = "1003",
            Items = new List<OrderFileItem>
            {
                new() { ItemId = "item-1", ClientFileLabel = "first.pdf", SequenceNo = 2 },
                new() { ItemId = "item-2", ClientFileLabel = "second.pdf", SequenceNo = 8 }
            }
        };
        var itemToDelete = order.Items[1];

        var removedEvents = new List<string>();
        var result = service.DeleteOrderItems(
            new List<OrderItemSelection> { new(order, itemToDelete) },
            removeFilesFromDisk: false,
            onItemRemoved: (_, _, itemName) => removedEvents.Add(itemName));

        Assert.Equal(1, result.RemovedCount);
        Assert.Empty(result.FailedItems);
        Assert.Single(order.Items);
        Assert.Equal("item-1", order.Items[0].ItemId);
        Assert.Equal(0, order.Items[0].SequenceNo);
        Assert.Single(removedEvents);
        Assert.Equal("second.pdf", removedEvents[0]);
    }

    [Fact]
    public void DeleteOrderItems_WhenItemMissing_ReturnsFailure()
    {
        var service = new OrderDeletionWorkflowService();
        var order = new OrderData
        {
            InternalId = "order-4",
            Id = "1004",
            Items = new List<OrderFileItem>
            {
                new() { ItemId = "item-1", ClientFileLabel = "first.pdf", SequenceNo = 0 }
            }
        };
        var missingItem = new OrderFileItem { ItemId = "item-missing", ClientFileLabel = "missing.pdf" };

        var callbackCount = 0;
        var result = service.DeleteOrderItems(
            new List<OrderItemSelection> { new(order, missingItem) },
            removeFilesFromDisk: false,
            onItemRemoved: (_, _, _) => callbackCount++);

        Assert.Equal(0, result.RemovedCount);
        Assert.Single(result.FailedItems);
        Assert.Contains("\u0444\u0430\u0439\u043b \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d", result.FailedItems[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("missing.pdf", result.FailedItems[0].ItemDisplayName);
        Assert.Equal(0, callbackCount);
        Assert.Single(order.Items);
    }

    [Fact]
    public void BuildOrderItemDisplayName_UsesLabelThenItemIdThenFallback()
    {
        Assert.Equal(
            "label.pdf",
            OrderDeletionWorkflowService.BuildOrderItemDisplayName(new OrderFileItem { ClientFileLabel = " label.pdf " }));

        Assert.Equal(
            "item-id",
            OrderDeletionWorkflowService.BuildOrderItemDisplayName(new OrderFileItem { ItemId = "item-id" }));

        Assert.Equal(
            "\u0444\u0430\u0439\u043b",
            OrderDeletionWorkflowService.BuildOrderItemDisplayName(new OrderFileItem { ItemId = string.Empty, ClientFileLabel = string.Empty }));
    }
}
