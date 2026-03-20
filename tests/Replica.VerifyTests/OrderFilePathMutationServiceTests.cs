using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderFilePathMutationServiceTests
{
    [Fact]
    public void ApplyOrderFilePath_ForSingleItemOrder_MirrorsMetadataToItemAndReturnsFileSyncStatus()
    {
        var fixedNow = new DateTime(2026, 3, 20, 15, 10, 0, DateTimeKind.Local);
        var service = new OrderFilePathMutationService(() => fixedNow);
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-file-path-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourcePath = Path.Combine(tempRoot, "source.pdf");
            File.WriteAllText(sourcePath, "source-content");
            var expectedSize = new FileInfo(sourcePath).Length;

            var singleItem = new OrderFileItem { ItemId = "item-1", UpdatedAt = fixedNow.AddMinutes(-5) };
            var order = new OrderData
            {
                InternalId = "order-1",
                Items = new List<OrderFileItem> { singleItem }
            };

            var statusUpdate = service.ApplyOrderFilePath(order, OrderStages.Source, sourcePath);

            Assert.Equal(WorkflowStatusNames.Processing, statusUpdate.Status);
            Assert.Equal("Найден исходный файл", statusUpdate.Reason);
            Assert.Equal(sourcePath, order.SourcePath);
            Assert.Equal(expectedSize, order.SourceFileSizeBytes);
            Assert.False(string.IsNullOrWhiteSpace(order.SourceFileHash));
            Assert.Equal(sourcePath, singleItem.SourcePath);
            Assert.Equal(expectedSize, singleItem.SourceFileSizeBytes);
            Assert.Equal(order.SourceFileHash, singleItem.SourceFileHash);
            Assert.Equal(WorkflowStatusNames.Processing, singleItem.FileStatus);
            Assert.Equal(fixedNow, singleItem.UpdatedAt);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void ApplyItemFilePath_ForSingleItemOrder_MirrorsBackToOrderAndReturnsItemReason()
    {
        var fixedNow = new DateTime(2026, 3, 20, 15, 20, 0, DateTimeKind.Local);
        var service = new OrderFilePathMutationService(() => fixedNow);
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-file-path-item-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var printPath = Path.Combine(tempRoot, "print.pdf");
            File.WriteAllText(printPath, "print-content");
            var expectedSize = new FileInfo(printPath).Length;

            var singleItem = new OrderFileItem { ItemId = "item-2", UpdatedAt = fixedNow.AddMinutes(-5) };
            var order = new OrderData
            {
                InternalId = "order-2",
                Items = new List<OrderFileItem> { singleItem }
            };

            var statusUpdate = service.ApplyItemFilePath(order, singleItem, OrderStages.Print, printPath);

            Assert.Equal(WorkflowStatusNames.Completed, statusUpdate.Status);
            Assert.Equal("item: Найден печатный файл", statusUpdate.Reason);
            Assert.Equal(printPath, singleItem.PrintPath);
            Assert.Equal(expectedSize, singleItem.PrintFileSizeBytes);
            Assert.False(string.IsNullOrWhiteSpace(singleItem.PrintFileHash));
            Assert.Equal(printPath, order.PrintPath);
            Assert.Equal(expectedSize, order.PrintFileSizeBytes);
            Assert.Equal(singleItem.PrintFileHash, order.PrintFileHash);
            Assert.Equal(WorkflowStatusNames.Completed, singleItem.FileStatus);
            Assert.Equal(fixedNow, singleItem.UpdatedAt);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void CalculateOrderStatusFromItems_ForMixedActivity_ReturnsAggregateProcessing()
    {
        var service = new OrderFilePathMutationService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-file-path-aggregate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var donePrint = Path.Combine(tempRoot, "done-print.pdf");
            var activeSource = Path.Combine(tempRoot, "active-source.pdf");
            File.WriteAllText(donePrint, "done");
            File.WriteAllText(activeSource, "active");

            var order = new OrderData
            {
                InternalId = "order-3",
                Items = new List<OrderFileItem>
                {
                    new() { ItemId = "item-1", PrintPath = donePrint },
                    new() { ItemId = "item-2", SourcePath = activeSource }
                }
            };

            var statusUpdate = service.CalculateOrderStatusFromItems(order);

            Assert.Equal("aggregate", statusUpdate.Reason);
            Assert.Equal(WorkflowStatusNames.Processing, statusUpdate.Status);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }
}
