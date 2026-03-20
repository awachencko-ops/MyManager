using System;
using System.Collections.Generic;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderEditorMutationServiceTests
{
    [Fact]
    public void AddCreatedOrder_WhenFieldsAreMissing_FillsDefaultsAndAddsToHistory()
    {
        var fixedNow = new DateTime(2026, 3, 20, 12, 34, 56, DateTimeKind.Local);
        var service = new OrderEditorMutationService(() => fixedNow);
        var history = new List<OrderData>();
        var order = new OrderData
        {
            InternalId = string.Empty,
            OrderDate = default,
            ArrivalDate = default,
            UserName = "  user  "
        };

        var internalId = service.AddCreatedOrder(
            history,
            order,
            normalizeUserName: value => value.Trim().ToUpperInvariant());

        Assert.Single(history);
        Assert.Equal(order, history[0]);
        Assert.False(string.IsNullOrWhiteSpace(internalId));
        Assert.Equal(internalId, order.InternalId);
        Assert.Equal(OrderData.PlaceholderOrderDate, order.OrderDate);
        Assert.Equal(fixedNow, order.ArrivalDate);
        Assert.Equal("USER", order.UserName);
    }

    [Fact]
    public void AddCreatedOrder_WhenFieldsAlreadySet_KeepsExistingValues()
    {
        var fixedNow = new DateTime(2026, 3, 20, 1, 2, 3, DateTimeKind.Local);
        var service = new OrderEditorMutationService(() => fixedNow);
        var history = new List<OrderData>();
        var existingArrival = new DateTime(2025, 4, 10, 9, 0, 0, DateTimeKind.Local);
        var existingOrderDate = new DateTime(2025, 4, 11, 9, 0, 0, DateTimeKind.Local);
        var order = new OrderData
        {
            InternalId = "order-existing",
            OrderDate = existingOrderDate,
            ArrivalDate = existingArrival,
            UserName = "alice"
        };

        var internalId = service.AddCreatedOrder(
            history,
            order,
            normalizeUserName: value => value);

        Assert.Equal("order-existing", internalId);
        Assert.Equal(existingOrderDate, order.OrderDate);
        Assert.Equal(existingArrival, order.ArrivalDate);
        Assert.Equal("alice", order.UserName);
    }

    [Fact]
    public void ApplySimpleEdit_UpdatesOrderAndBackfillsArrivalDate()
    {
        var fixedNow = new DateTime(2026, 3, 20, 9, 15, 0, DateTimeKind.Local);
        var service = new OrderEditorMutationService(() => fixedNow);
        var order = new OrderData
        {
            ArrivalDate = default,
            Id = "old"
        };

        service.ApplySimpleEdit(order, "  12345  ", new DateTime(2026, 3, 10));

        Assert.Equal("12345", order.Id);
        Assert.Equal(new DateTime(2026, 3, 10), order.OrderDate);
        Assert.Equal(fixedNow, order.ArrivalDate);
    }

    [Fact]
    public void ApplyExtendedEdit_CopiesEditableFieldsFromUpdatedOrder()
    {
        var service = new OrderEditorMutationService();
        var target = new OrderData
        {
            Id = "old-id",
            Keyword = "old-keyword",
            PitStopAction = "old-pit",
            ImposingAction = "old-imp"
        };
        var updated = new OrderData
        {
            Id = "new-id",
            StartMode = OrderStartMode.Extended,
            Keyword = "new-keyword",
            ArrivalDate = new DateTime(2026, 1, 1),
            OrderDate = new DateTime(2026, 1, 2),
            FolderName = "folder",
            SourcePath = @"C:\in\source.pdf",
            PreparedPath = @"C:\prep\prepared.pdf",
            PrintPath = @"C:\print\out.pdf",
            PitStopAction = "pit",
            ImposingAction = "imp"
        };

        service.ApplyExtendedEdit(target, updated);

        Assert.Equal("new-id", target.Id);
        Assert.Equal(OrderStartMode.Extended, target.StartMode);
        Assert.Equal("new-keyword", target.Keyword);
        Assert.Equal(new DateTime(2026, 1, 1), target.ArrivalDate);
        Assert.Equal(new DateTime(2026, 1, 2), target.OrderDate);
        Assert.Equal("folder", target.FolderName);
        Assert.Equal(@"C:\in\source.pdf", target.SourcePath);
        Assert.Equal(@"C:\prep\prepared.pdf", target.PreparedPath);
        Assert.Equal(@"C:\print\out.pdf", target.PrintPath);
        Assert.Equal("pit", target.PitStopAction);
        Assert.Equal("imp", target.ImposingAction);
    }
}
