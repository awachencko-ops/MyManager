using System;
using System.Collections.Generic;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderItemDeleteCommandServiceTests
{
    [Fact]
    public void Execute_WhenSingleItemRemovedFromGroupOrder_DemotesToSingleOrder()
    {
        var service = CreateService();
        var firstItem = new OrderFileItem
        {
            ItemId = "item-1",
            SequenceNo = 0,
            ClientFileLabel = "a.pdf",
            SourcePath = @"C:\orders\a.pdf",
            UpdatedAt = DateTime.Now
        };
        var secondItem = new OrderFileItem
        {
            ItemId = "item-2",
            SequenceNo = 1,
            ClientFileLabel = "b.pdf",
            SourcePath = @"C:\orders\b.pdf",
            UpdatedAt = DateTime.Now
        };
        var order = new OrderData
        {
            InternalId = "order-1",
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Items = [firstItem, secondItem]
        };

        var callbackCalls = 0;
        var callbackItemNames = new List<string>();
        var result = service.Execute(
            selectedOrderItems: [new OrderItemSelection(order, firstItem)],
            removeFilesFromDisk: false,
            onItemRemoved: (_, _, itemName) =>
            {
                callbackCalls++;
                callbackItemNames.Add(itemName);
            });

        Assert.Equal(1, callbackCalls);
        Assert.Single(callbackItemNames);
        Assert.Equal("a.pdf", callbackItemNames[0]);
        Assert.Equal(1, result.DeleteResult.RemovedCount);
        Assert.Empty(result.DeleteResult.FailedItems);
        Assert.Single(result.TopologyMutations);
        Assert.True(result.TopologyMutations[0].WasMultiBeforeMutation);
        Assert.True(result.TopologyMutations[0].MutationResult.DemotedToSingleOrder);
        Assert.Single(order.Items);
        Assert.Equal("item-2", order.Items[0].ItemId);
    }

    [Fact]
    public void Execute_WhenTwoItemsRemovedFromSameOrder_AppliesTopologyMutationOnce()
    {
        var service = CreateService();
        var item1 = new OrderFileItem { ItemId = "item-1", SequenceNo = 0, UpdatedAt = DateTime.Now };
        var item2 = new OrderFileItem { ItemId = "item-2", SequenceNo = 1, UpdatedAt = DateTime.Now };
        var item3 = new OrderFileItem { ItemId = "item-3", SequenceNo = 2, UpdatedAt = DateTime.Now };
        var order = new OrderData
        {
            InternalId = "order-2",
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Items = [item1, item2, item3]
        };

        var callbackCalls = 0;
        var result = service.Execute(
            selectedOrderItems:
            [
                new OrderItemSelection(order, item1),
                new OrderItemSelection(order, item2)
            ],
            removeFilesFromDisk: false,
            onItemRemoved: (_, _, _) => callbackCalls++);

        Assert.Equal(2, callbackCalls);
        Assert.Equal(2, result.DeleteResult.RemovedCount);
        Assert.Empty(result.DeleteResult.FailedItems);
        Assert.Single(result.TopologyMutations);
        Assert.True(result.TopologyMutations[0].MutationResult.DemotedToSingleOrder);
        Assert.Single(order.Items);
        Assert.Equal("item-3", order.Items[0].ItemId);
    }

    [Fact]
    public void Execute_WhenItemIsMissing_ReturnsFailedDeleteAndKeepsTopology()
    {
        var service = CreateService();
        var existing = new OrderFileItem
        {
            ItemId = "item-existing",
            SequenceNo = 0,
            UpdatedAt = DateTime.Now
        };
        var missing = new OrderFileItem
        {
            ItemId = "item-missing",
            SequenceNo = 1,
            UpdatedAt = DateTime.Now
        };
        var order = new OrderData
        {
            InternalId = "order-3",
            FileTopologyMarker = OrderFileTopologyMarker.SingleOrder,
            Items = [existing]
        };

        var callbackCalls = 0;
        var result = service.Execute(
            selectedOrderItems: [new OrderItemSelection(order, missing)],
            removeFilesFromDisk: false,
            onItemRemoved: (_, _, _) => callbackCalls++);

        Assert.Equal(0, callbackCalls);
        Assert.Equal(0, result.DeleteResult.RemovedCount);
        Assert.Single(result.DeleteResult.FailedItems);
        Assert.Single(result.TopologyMutations);
        Assert.False(result.TopologyMutations[0].MutationResult.DemotedToSingleOrder);
        Assert.False(result.TopologyMutations[0].MutationResult.PromotedToMultiOrder);
        Assert.Single(order.Items);
    }

    private static OrderItemDeleteCommandService CreateService()
    {
        return new OrderItemDeleteCommandService(
            new OrderDeletionWorkflowService(),
            new OrderItemMutationService());
    }
}
