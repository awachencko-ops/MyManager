using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderDeleteCommandServiceTests
{
    [Fact]
    public void Execute_WhenOrderHasLocalRunSession_CleansRunStateAndRemovesFromHistory()
    {
        var service = CreateService();
        var order = new OrderData
        {
            InternalId = "order-1",
            Id = "1001",
            FolderName = string.Empty
        };
        var history = new List<OrderData> { order };
        var runCts = new CancellationTokenSource();
        var runTokens = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal)
        {
            [order.InternalId] = runCts
        };
        var runProgress = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [order.InternalId] = 67
        };
        var expandedOrderIds = new HashSet<string>(StringComparer.Ordinal)
        {
            order.InternalId
        };
        var callbackCount = 0;
        var callbackRemoveFromDisk = false;

        var result = service.Execute(
            orderHistory: history,
            selectedOrders: new[] { order },
            removeFilesFromDisk: false,
            ordersRootPath: string.Empty,
            runTokensByOrder: runTokens,
            runProgressByOrderInternalId: runProgress,
            expandedOrderIds: expandedOrderIds,
            onOrderRemoved: (_, removeFromDisk) =>
            {
                callbackCount++;
                callbackRemoveFromDisk = removeFromDisk;
            });

        Assert.Equal(1, result.DeleteResult.RemovedCount);
        Assert.Empty(result.DeleteResult.FailedOrders);
        Assert.Equal(1, result.CancelledRunsCount);
        Assert.Empty(history);
        Assert.False(runTokens.ContainsKey(order.InternalId));
        Assert.False(runProgress.ContainsKey(order.InternalId));
        Assert.DoesNotContain(order.InternalId, expandedOrderIds);
        Assert.Equal(1, callbackCount);
        Assert.False(callbackRemoveFromDisk);
    }

    [Fact]
    public void Execute_WhenOrderHasNoRunToken_StillCleansProgressAndExpandedState()
    {
        var service = CreateService();
        var order = new OrderData
        {
            InternalId = "order-2",
            Id = "1002",
            FolderName = string.Empty
        };
        var history = new List<OrderData> { order };
        var runTokens = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var runProgress = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [order.InternalId] = 11
        };
        var expandedOrderIds = new HashSet<string>(StringComparer.Ordinal)
        {
            order.InternalId
        };

        var result = service.Execute(
            orderHistory: history,
            selectedOrders: new[] { order },
            removeFilesFromDisk: false,
            ordersRootPath: string.Empty,
            runTokensByOrder: runTokens,
            runProgressByOrderInternalId: runProgress,
            expandedOrderIds: expandedOrderIds,
            onOrderRemoved: (_, _) => { });

        Assert.Equal(1, result.DeleteResult.RemovedCount);
        Assert.Equal(0, result.CancelledRunsCount);
        Assert.False(runProgress.ContainsKey(order.InternalId));
        Assert.DoesNotContain(order.InternalId, expandedOrderIds);
    }

    [Fact]
    public void Execute_WhenRemoveFilesFromDisk_DeletesOrderFolder()
    {
        var service = CreateService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-delete-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var folderName = "order-1003";
            var orderFolder = Path.Combine(tempRoot, folderName);
            Directory.CreateDirectory(orderFolder);
            File.WriteAllText(Path.Combine(orderFolder, "source.pdf"), "test");

            var order = new OrderData
            {
                InternalId = "order-3",
                Id = "1003",
                FolderName = folderName
            };
            var history = new List<OrderData> { order };

            var result = service.Execute(
                orderHistory: history,
                selectedOrders: new[] { order },
                removeFilesFromDisk: true,
                ordersRootPath: tempRoot,
                runTokensByOrder: new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal),
                runProgressByOrderInternalId: new Dictionary<string, int>(StringComparer.Ordinal),
                expandedOrderIds: new HashSet<string>(StringComparer.Ordinal),
                onOrderRemoved: (_, _) => { });

            Assert.Equal(1, result.DeleteResult.RemovedCount);
            Assert.Empty(result.DeleteResult.FailedOrders);
            Assert.True(result.CancelledRunsCount == 0);
            Assert.False(Directory.Exists(orderFolder));
            Assert.Empty(history);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    private static OrderDeleteCommandService CreateService()
    {
        return new OrderDeleteCommandService(new OrderDeletionWorkflowService());
    }
}
