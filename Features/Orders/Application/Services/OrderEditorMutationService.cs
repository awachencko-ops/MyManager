using System;
using System.Collections.Generic;

namespace Replica;

public sealed class OrderEditorMutationService
{
    private readonly Func<DateTime> _nowProvider;

    public OrderEditorMutationService(Func<DateTime>? nowProvider = null)
    {
        _nowProvider = nowProvider ?? (() => DateTime.Now);
    }

    public string AddCreatedOrder(
        ICollection<OrderData> orderHistory,
        OrderData order,
        Func<string, string> normalizeUserName)
    {
        if (orderHistory == null)
            throw new ArgumentNullException(nameof(orderHistory));
        if (order == null)
            throw new ArgumentNullException(nameof(order));
        if (normalizeUserName == null)
            throw new ArgumentNullException(nameof(normalizeUserName));

        if (string.IsNullOrWhiteSpace(order.InternalId))
            order.InternalId = Guid.NewGuid().ToString("N");
        if (order.OrderDate == default)
            order.OrderDate = OrderData.PlaceholderOrderDate;
        if (order.ArrivalDate == default)
            order.ArrivalDate = _nowProvider();

        order.UserName = normalizeUserName(order.UserName ?? string.Empty);
        orderHistory.Add(order);

        return order.InternalId;
    }

    public void ApplySimpleEdit(OrderData order, string orderNumber, DateTime orderDate)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        order.Id = orderNumber?.Trim() ?? string.Empty;
        order.OrderDate = orderDate;
        if (order.ArrivalDate == default)
            order.ArrivalDate = _nowProvider();
    }

    public void ApplyExtendedEdit(OrderData targetOrder, OrderData updatedOrder)
    {
        if (targetOrder == null)
            throw new ArgumentNullException(nameof(targetOrder));
        if (updatedOrder == null)
            throw new ArgumentNullException(nameof(updatedOrder));

        targetOrder.Id = updatedOrder.Id;
        targetOrder.StartMode = updatedOrder.StartMode;
        targetOrder.Keyword = updatedOrder.Keyword;
        targetOrder.ArrivalDate = updatedOrder.ArrivalDate;
        targetOrder.OrderDate = updatedOrder.OrderDate;
        targetOrder.FolderName = updatedOrder.FolderName;
        targetOrder.SourcePath = updatedOrder.SourcePath;
        targetOrder.PreparedPath = updatedOrder.PreparedPath;
        targetOrder.PrintPath = updatedOrder.PrintPath;
        targetOrder.PitStopAction = updatedOrder.PitStopAction;
        targetOrder.ImposingAction = updatedOrder.ImposingAction;
    }
}
