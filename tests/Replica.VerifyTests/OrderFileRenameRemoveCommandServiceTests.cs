using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderFileRenameRemoveCommandServiceTests
{
    [Fact]
    public void TryBuildRenamedPath_WhenTargetAlreadyExists_ReturnsTargetExists()
    {
        var service = new OrderFileRenameRemoveCommandService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-rename-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourcePath = Path.Combine(tempRoot, "source.pdf");
            var existingTargetPath = Path.Combine(tempRoot, "target.pdf");
            File.WriteAllText(sourcePath, "source");
            File.WriteAllText(existingTargetPath, "target");

            var result = service.TryBuildRenamedPath(sourcePath, "target");

            Assert.Equal(RenamePathBuildStatus.TargetExists, result.Status);
            Assert.False(result.IsSuccess);
            Assert.True(string.IsNullOrWhiteSpace(result.RenamedPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void TryBuildRenamedPath_SanitizesInvalidCharacters()
    {
        var service = new OrderFileRenameRemoveCommandService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-rename-sanitize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourcePath = Path.Combine(tempRoot, "source.pdf");
            File.WriteAllText(sourcePath, "source");

            var result = service.TryBuildRenamedPath(sourcePath, "new:name");

            Assert.Equal(RenamePathBuildStatus.Success, result.Status);
            Assert.True(result.IsSuccess);
            Assert.EndsWith("new_name.pdf", result.RenamedPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void ApplyItemFileRemoved_WhenItemBecomesEmpty_RemovesItemAndDemotesToSingle()
    {
        var service = new OrderFileRenameRemoveCommandService();
        var removingItem = new OrderFileItem
        {
            ItemId = "item-remove",
            SequenceNo = 0,
            SourcePath = @"C:\orders\remove.pdf",
            UpdatedAt = DateTime.Now
        };
        var keptItem = new OrderFileItem
        {
            ItemId = "item-keep",
            SequenceNo = 1,
            SourcePath = @"C:\orders\keep.pdf",
            UpdatedAt = DateTime.Now
        };
        var order = new OrderData
        {
            InternalId = "order-1",
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Items = new List<OrderFileItem> { removingItem, keptItem }
        };

        var outcome = service.ApplyItemFileRemoved(
            order,
            removingItem,
            OrderStages.Source,
            wasMultiOrderBeforeMutation: true);

        Assert.True(outcome.ItemRemovedFromOrder);
        Assert.True(outcome.TopologyMutation.DemotedToSingleOrder);
        Assert.False(outcome.CanRestoreItemSelection);
        Assert.Single(order.Items);
        Assert.Equal("item-keep", order.Items[0].ItemId);
    }

    [Fact]
    public void ApplyItemFileRemoved_WhenItemStillHasTechnicalFiles_DoesNotRemoveItem()
    {
        var service = new OrderFileRenameRemoveCommandService();
        var keepingItem = new OrderFileItem
        {
            ItemId = "item-keep",
            SequenceNo = 0,
            SourcePath = @"C:\orders\keep.pdf",
            TechnicalFiles = new List<string> { @"C:\orders\tech\sheet1.pdf" },
            UpdatedAt = DateTime.Now
        };
        var secondItem = new OrderFileItem
        {
            ItemId = "item-2",
            SequenceNo = 1,
            SourcePath = @"C:\orders\second.pdf",
            UpdatedAt = DateTime.Now
        };
        var order = new OrderData
        {
            InternalId = "order-2",
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Items = new List<OrderFileItem> { keepingItem, secondItem }
        };

        var outcome = service.ApplyItemFileRemoved(
            order,
            keepingItem,
            OrderStages.Source,
            wasMultiOrderBeforeMutation: true);

        Assert.False(outcome.ItemRemovedFromOrder);
        Assert.False(outcome.TopologyMutation.DemotedToSingleOrder);
        Assert.True(outcome.CanRestoreItemSelection);
        Assert.Equal(2, order.Items.Count);
    }

    [Fact]
    public void ApplyPrintTileFileRenamed_WhenOrderAndItemMatch_UpdatesBothPrintPaths()
    {
        var service = new OrderFileRenameRemoveCommandService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-print-rename-match-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var oldPath = Path.Combine(tempRoot, "old.pdf");
            var renamedPath = Path.Combine(tempRoot, "renamed.pdf");
            File.WriteAllText(renamedPath, "renamed-content");

            var item = new OrderFileItem
            {
                ItemId = "item-1",
                SequenceNo = 0,
                PrintPath = oldPath,
                UpdatedAt = DateTime.Now
            };
            var order = new OrderData
            {
                InternalId = "order-print-1",
                PrintPath = oldPath,
                Items = new List<OrderFileItem> { item }
            };

            var statusUpdate = service.ApplyPrintTileFileRenamed(order, oldPath, renamedPath);

            Assert.Equal(WorkflowStatusNames.Completed, statusUpdate.Status);
            Assert.Equal(renamedPath, order.PrintPath);
            Assert.Equal(renamedPath, item.PrintPath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void ApplyPrintTileFileRenamed_WhenNoMatchingPath_FallsBackToOrderPrintPath()
    {
        var service = new OrderFileRenameRemoveCommandService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-print-rename-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var oldPath = Path.Combine(tempRoot, "old.pdf");
            var renamedPath = Path.Combine(tempRoot, "renamed.pdf");
            File.WriteAllText(renamedPath, "renamed-content");

            var order = new OrderData
            {
                InternalId = "order-print-2",
                PrintPath = Path.Combine(tempRoot, "unrelated.pdf"),
                Items = new List<OrderFileItem>
                {
                    new()
                    {
                        ItemId = "item-1",
                        SequenceNo = 0,
                        PrintPath = Path.Combine(tempRoot, "item-unrelated.pdf"),
                        UpdatedAt = DateTime.Now
                    }
                }
            };

            var statusUpdate = service.ApplyPrintTileFileRenamed(order, oldPath, renamedPath);

            Assert.Equal(WorkflowStatusNames.Completed, statusUpdate.Status);
            Assert.Equal(renamedPath, order.PrintPath);
            Assert.Equal(Path.Combine(tempRoot, "item-unrelated.pdf"), order.Items[0].PrintPath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }
}
