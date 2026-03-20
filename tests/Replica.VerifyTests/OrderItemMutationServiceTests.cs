using System;
using System.Collections.Generic;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderItemMutationServiceTests
{
    [Fact]
    public void PrepareAddItem_AppendsItemWithDefaultsAndSanitizedSourcePath()
    {
        var fixedNow = new DateTime(2026, 3, 20, 14, 0, 0, DateTimeKind.Local);
        var service = new OrderItemMutationService(() => fixedNow);
        var order = new OrderData
        {
            Id = "1001",
            FileTopologyMarker = OrderFileTopologyMarker.SingleOrder,
            Items =
            [
                new OrderFileItem
                {
                    ItemId = "item-1",
                    SequenceNo = 7,
                    ClientFileLabel = "old",
                    UpdatedAt = fixedNow.AddMinutes(-10)
                }
            ]
        };

        var result = service.PrepareAddItem(
            order,
            "\"C:\\orders\\test2.pdf\"",
            pitStopAction: "pit",
            imposingAction: "imp");

        Assert.False(result.WasMultiOrderBeforeMutation);
        Assert.Equal("C:\\orders\\test2.pdf", result.SourcePath);
        Assert.Same(result.Item, order.Items[^1]);
        Assert.Equal(8, result.Item.SequenceNo);
        Assert.Equal("test2", result.Item.ClientFileLabel);
        Assert.Equal("pit", result.Item.PitStopAction);
        Assert.Equal("imp", result.Item.ImposingAction);
        Assert.Equal(WorkflowStatusNames.Waiting, result.Item.FileStatus);
        Assert.Equal(fixedNow, result.Item.UpdatedAt);
    }

    [Fact]
    public void PrepareAddItem_ForMultiOrder_ReportsWasMultiBeforeMutation()
    {
        var service = new OrderItemMutationService();
        var order = new OrderData
        {
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Items =
            [
                new OrderFileItem { ItemId = "a", SequenceNo = 0, UpdatedAt = DateTime.Now },
                new OrderFileItem { ItemId = "b", SequenceNo = 1, UpdatedAt = DateTime.Now }
            ]
        };

        var result = service.PrepareAddItem(
            order,
            "C:\\orders\\c.pdf",
            pitStopAction: "-",
            imposingAction: "-");

        Assert.True(result.WasMultiOrderBeforeMutation);
        Assert.Equal(2, result.Item.SequenceNo);
        Assert.Equal(3, order.Items.Count);
    }

    [Fact]
    public void RollbackPreparedItem_RemovesItemAndReindexes()
    {
        var service = new OrderItemMutationService();
        var first = new OrderFileItem { ItemId = "item-1", SequenceNo = 0, UpdatedAt = DateTime.Now };
        var second = new OrderFileItem { ItemId = "item-2", SequenceNo = 5, UpdatedAt = DateTime.Now };
        var prepared = new OrderFileItem { ItemId = "item-new", SequenceNo = 8, UpdatedAt = DateTime.Now };
        var order = new OrderData
        {
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Items = [first, second, prepared]
        };

        var removed = service.RollbackPreparedItem(order, prepared);

        Assert.True(removed);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(0, order.Items[0].SequenceNo);
        Assert.Equal(1, order.Items[1].SequenceNo);
    }

    [Fact]
    public void RemoveItemIfEmpty_WhenNoFilesAndNoTechnicalFiles_RemovesAndReindexes()
    {
        var service = new OrderItemMutationService();
        var kept = new OrderFileItem
        {
            ItemId = "item-keep",
            SequenceNo = 10,
            SourcePath = "C:\\orders\\keep.pdf",
            UpdatedAt = DateTime.Now
        };
        var empty = new OrderFileItem
        {
            ItemId = "item-empty",
            SequenceNo = 15,
            TechnicalFiles = new List<string> { " ", "" },
            UpdatedAt = DateTime.Now
        };
        var order = new OrderData
        {
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Items = [kept, empty]
        };

        var removed = service.RemoveItemIfEmpty(order, empty);

        Assert.True(removed);
        Assert.Single(order.Items);
        Assert.Equal("item-keep", order.Items[0].ItemId);
        Assert.Equal(0, order.Items[0].SequenceNo);
    }

    [Fact]
    public void RemoveItemIfEmpty_WhenItemHasSourceFile_DoesNotRemove()
    {
        var service = new OrderItemMutationService();
        var nonEmpty = new OrderFileItem
        {
            ItemId = "item-non-empty",
            SequenceNo = 0,
            SourcePath = "C:\\orders\\source.pdf",
            UpdatedAt = DateTime.Now
        };
        var order = new OrderData
        {
            FileTopologyMarker = OrderFileTopologyMarker.SingleOrder,
            Items = [nonEmpty]
        };

        var removed = service.RemoveItemIfEmpty(order, nonEmpty);

        Assert.False(removed);
        Assert.Single(order.Items);
    }

    [Fact]
    public void ApplyTopologyAfterItemMutation_DetectsPromotionToMultiOrder()
    {
        var service = new OrderItemMutationService();
        var order = new OrderData
        {
            FileTopologyMarker = OrderFileTopologyMarker.SingleOrder,
            Items =
            [
                new OrderFileItem { ItemId = "item-1", SequenceNo = 0, UpdatedAt = DateTime.Now },
                new OrderFileItem { ItemId = "item-2", SequenceNo = 1, UpdatedAt = DateTime.Now }
            ]
        };

        var result = service.ApplyTopologyAfterItemMutation(order, wasMultiOrderBeforeMutation: false);

        Assert.True(result.IsMultiOrderNow);
        Assert.True(result.PromotedToMultiOrder);
        Assert.False(result.DemotedToSingleOrder);
    }

    [Fact]
    public void ApplyTopologyAfterItemMutation_DetectsDemotionToSingleOrder()
    {
        var service = new OrderItemMutationService();
        var order = new OrderData
        {
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Items =
            [
                new OrderFileItem { ItemId = "item-1", SequenceNo = 0, UpdatedAt = DateTime.Now }
            ]
        };

        var result = service.ApplyTopologyAfterItemMutation(order, wasMultiOrderBeforeMutation: true);

        Assert.False(result.IsMultiOrderNow);
        Assert.False(result.PromotedToMultiOrder);
        Assert.True(result.DemotedToSingleOrder);
    }
}
