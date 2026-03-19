using System.Collections.Generic;
using Replica.Api.Contracts;
using Replica.Shared.Models;

namespace Replica.Api.Services;

public interface ILanOrderStore
{
    IReadOnlyList<SharedUser> GetUsers();
    IReadOnlyList<SharedOrder> GetOrders(string createdBy);
    bool TryGetOrder(string orderId, out SharedOrder order);
    SharedOrder CreateOrder(CreateOrderRequest request, string actor);
    StoreOperationResult TryUpdateOrder(string orderId, UpdateOrderRequest request, string actor);
    StoreOperationResult TryAddItem(string orderId, AddOrderItemRequest request, string actor);
    StoreOperationResult TryUpdateItem(string orderId, string itemId, UpdateOrderItemRequest request, string actor);
    StoreOperationResult TryReorderItems(string orderId, ReorderOrderItemsRequest request, string actor);
}
