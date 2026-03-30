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
    public void DeleteOrders_WhenFolderMissing_DeletesPreparedAndPrintArtifactsFromOrderAndItems()
    {
        var service = new OrderDeletionWorkflowService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-delete-order-artifacts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var orderPreparedPath = Path.Combine(tempRoot, "order-prepared.pdf");
            var orderPrintPath = Path.Combine(tempRoot, "order-print.pdf");
            var itemPreparedPath = Path.Combine(tempRoot, "item-prepared.pdf");
            var itemPrintPath = Path.Combine(tempRoot, "item-print.pdf");
            File.WriteAllText(orderPreparedPath, "prepared");
            File.WriteAllText(orderPrintPath, "print");
            File.WriteAllText(itemPreparedPath, "item-prepared");
            File.WriteAllText(itemPrintPath, "item-print");

            var order = new OrderData
            {
                InternalId = "order-artifacts",
                Id = "1002A",
                FolderName = "missing-folder",
                PreparedPath = orderPreparedPath,
                PrintPath = orderPrintPath,
                Items = new List<OrderFileItem>
                {
                    new()
                    {
                        ItemId = "item-1",
                        PreparedPath = itemPreparedPath,
                        PrintPath = itemPrintPath,
                        SequenceNo = 0
                    }
                }
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
            Assert.False(File.Exists(orderPreparedPath));
            Assert.False(File.Exists(orderPrintPath));
            Assert.False(File.Exists(itemPreparedPath));
            Assert.False(File.Exists(itemPrintPath));
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
    public void DeleteOrderItems_WhenRemovingFromDisk_DeletesPreparedAndPrintFiles()
    {
        var service = new OrderDeletionWorkflowService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-delete-order-items-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var preparedPath = Path.Combine(tempRoot, "prepared.pdf");
            var printPath = Path.Combine(tempRoot, "print.pdf");
            File.WriteAllText(preparedPath, "prepared");
            File.WriteAllText(printPath, "print");

            var item = new OrderFileItem
            {
                ItemId = "item-remove",
                ClientFileLabel = "remove.pdf",
                PreparedPath = preparedPath,
                PrintPath = printPath,
                SequenceNo = 0
            };
            var order = new OrderData
            {
                InternalId = "order-delete-items-files",
                Id = "1003A",
                Items = new List<OrderFileItem> { item }
            };

            var result = service.DeleteOrderItems(
                new List<OrderItemSelection> { new(order, item) },
                removeFilesFromDisk: true,
                onItemRemoved: (_, _, _) => { });

            Assert.Equal(1, result.RemovedCount);
            Assert.Empty(result.FailedItems);
            Assert.Empty(order.Items);
            Assert.False(File.Exists(preparedPath));
            Assert.False(File.Exists(printPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void DeleteOrderItems_WhenRemovingOnlyInProgram_LeavesPreparedAndPrintFilesOnDisk()
    {
        var service = new OrderDeletionWorkflowService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-delete-order-items-logical-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var preparedPath = Path.Combine(tempRoot, "prepared.pdf");
            var printPath = Path.Combine(tempRoot, "print.pdf");
            File.WriteAllText(preparedPath, "prepared");
            File.WriteAllText(printPath, "print");

            var item = new OrderFileItem
            {
                ItemId = "item-logical-remove",
                ClientFileLabel = "logical.pdf",
                PreparedPath = preparedPath,
                PrintPath = printPath,
                SequenceNo = 0
            };
            var order = new OrderData
            {
                InternalId = "order-delete-items-logical",
                Id = "1004A",
                Items = new List<OrderFileItem> { item }
            };

            var result = service.DeleteOrderItems(
                new List<OrderItemSelection> { new(order, item) },
                removeFilesFromDisk: false,
                onItemRemoved: (_, _, _) => { });

            Assert.Equal(1, result.RemovedCount);
            Assert.Empty(result.FailedItems);
            Assert.Empty(order.Items);
            Assert.True(File.Exists(preparedPath));
            Assert.True(File.Exists(printPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
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
