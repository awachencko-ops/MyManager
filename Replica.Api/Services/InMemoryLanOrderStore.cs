using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Shared.Models;

namespace Replica.Api.Services;

public sealed class InMemoryLanOrderStore : ILanOrderStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, SharedOrder> _ordersById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activeRunTokensByOrder = new(StringComparer.Ordinal);
    private readonly List<SharedOrderEvent> _events = new();
    private long _eventSequence;

    private readonly List<SharedUser> _users =
    [
        new() { Id = "u-admin", Name = "Administrator", Role = "Admin", IsActive = true },
        new() { Id = "u-operator-1", Name = "Operator 1", Role = "Operator", IsActive = true },
        new() { Id = "u-operator-2", Name = "Operator 2", Role = "Operator", IsActive = true }
    ];

    public IReadOnlyList<SharedUser> GetUsers(bool includeInactive = false)
    {
        lock (_sync)
        {
            IEnumerable<SharedUser> users = _users;
            if (!includeInactive)
                users = users.Where(user => user.IsActive);

            return users.Select(CloneUser).ToList();
        }
    }

    public UserOperationResult UpsertUser(UpsertUserRequest request, string actor)
    {
        lock (_sync)
        {
            if (!UserManagementRules.TryNormalizeUpsertRequest(
                request,
                out var normalizedName,
                out var normalizedRole,
                out var normalizedIsActive,
                out var error))
            {
                return UserOperationResult.BadRequest(error);
            }

            var existing = _users.FirstOrDefault(user =>
                string.Equals(user.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
            var nextIsActive = normalizedIsActive ?? existing?.IsActive ?? true;

            if (existing != null)
            {
                var otherActiveAdminsCount = _users.Count(user =>
                    !string.Equals(user.Name, existing.Name, StringComparison.OrdinalIgnoreCase)
                    && user.IsActive
                    && string.Equals(ReplicaApiRoles.Normalize(user.Role), ReplicaApiRoles.Admin, StringComparison.Ordinal));
                if (UserManagementRules.WouldRemoveLastActiveAdmin(
                    existing.Role,
                    existing.IsActive,
                    normalizedRole,
                    nextIsActive,
                    otherActiveAdminsCount))
                {
                    return UserOperationResult.BadRequest("at least one active admin is required");
                }

                existing.Role = normalizedRole;
                existing.IsActive = nextIsActive;
                existing.UpdatedAt = DateTime.Now;
                return UserOperationResult.Success(CloneUser(existing));
            }

            var created = new SharedUser
            {
                Id = "u-" + Guid.NewGuid().ToString("N")[..8],
                Name = normalizedName,
                Role = normalizedRole,
                IsActive = nextIsActive,
                UpdatedAt = DateTime.Now
            };
            _users.Add(created);
            return UserOperationResult.Success(CloneUser(created));
        }
    }

    public IReadOnlyList<SharedOrder> GetOrders(string createdBy)
    {
        lock (_sync)
        {
            IEnumerable<SharedOrder> orders = _ordersById.Values;
            if (!string.IsNullOrWhiteSpace(createdBy))
            {
                var filter = createdBy.Trim();
                orders = orders.Where(x =>
                    string.Equals(x.CreatedById, filter, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.CreatedByUser, filter, StringComparison.OrdinalIgnoreCase));
            }

            return orders.Select(CloneOrder).ToList();
        }
    }

    public bool TryGetOrder(string orderId, out SharedOrder order)
    {
        lock (_sync)
        {
            if (_ordersById.TryGetValue(orderId ?? string.Empty, out var existing))
            {
                order = CloneOrder(existing);
                return true;
            }

            order = new SharedOrder();
            return false;
        }
    }

    public SharedOrder CreateOrder(CreateOrderRequest request, string actor)
    {
        lock (_sync)
        {
            var now = DateTime.Now;
            var managerOrderDate = request.ManagerOrderDate ?? DateTime.Today;
            var arrivalDate = request.ArrivalDate ?? now;
            var order = new SharedOrder
            {
                InternalId = Guid.NewGuid().ToString("N"),
                OrderNumber = request.OrderNumber?.Trim() ?? string.Empty,
                UserName = request.UserName?.Trim() ?? string.Empty,
                CreatedById = request.CreatedById?.Trim() ?? string.Empty,
                CreatedByUser = request.CreatedByUser?.Trim() ?? string.Empty,
                Status = request.Status?.Trim() ?? "Waiting",
                Keyword = request.Keyword?.Trim() ?? string.Empty,
                FolderName = request.FolderName?.Trim() ?? string.Empty,
                StartMode = request.StartMode,
                TopologyMarker = request.TopologyMarker,
                PitStopAction = request.PitStopAction?.Trim() ?? "-",
                ImposingAction = request.ImposingAction?.Trim() ?? "-",
                ManagerOrderDate = managerOrderDate == default ? DateTime.Today : managerOrderDate,
                ArrivalDate = arrivalDate == default ? now : arrivalDate,
                Version = 1,
                LastStatusAt = now
            };

            var items = request.Items ?? new List<SharedOrderItem>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = CloneItem(items[i]);
                item.ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? Guid.NewGuid().ToString("N") : item.ItemId;
                item.SequenceNo = i;
                item.Version = 1;
                item.UpdatedAt = DateTime.Now;
                order.Items.Add(item);
                AppendEvent(order.InternalId, item.ItemId, "add-item", "api", actor, new { item_id = item.ItemId, sequence_no = item.SequenceNo });
            }

            _ordersById[order.InternalId] = order;
            AppendEvent(order.InternalId, string.Empty, "add-order", "api", actor, new { order_id = order.InternalId, version = order.Version });
            return CloneOrder(order);
        }
    }

    public StoreOperationResult TryDeleteOrder(string orderId, DeleteOrderRequest request, string actor)
    {
        lock (_sync)
        {
            if (!_ordersById.TryGetValue(orderId ?? string.Empty, out var order))
                return StoreOperationResult.NotFound();

            request ??= new DeleteOrderRequest();
            if (request.ExpectedVersion > 0 && order.Version != request.ExpectedVersion)
                return StoreOperationResult.Conflict(order.Version, "order version mismatch");

            var snapshot = CloneOrder(order);
            _ordersById.Remove(order.InternalId);
            _activeRunTokensByOrder.Remove(order.InternalId);

            AppendEvent(order.InternalId, string.Empty, "delete-order", "api", actor, new
            {
                order_id = order.InternalId,
                order_version = order.Version
            });

            return StoreOperationResult.Success(snapshot);
        }
    }

    public StoreOperationResult TryUpdateOrder(string orderId, UpdateOrderRequest request, string actor)
    {
        lock (_sync)
        {
            if (!_ordersById.TryGetValue(orderId ?? string.Empty, out var order))
                return StoreOperationResult.NotFound();

            if (order.Version != request.ExpectedVersion)
                return StoreOperationResult.Conflict(order.Version, "order version mismatch");

            if (request.OrderNumber != null)
                order.OrderNumber = request.OrderNumber.Trim();
            if (request.ManagerOrderDate.HasValue)
                order.ManagerOrderDate = request.ManagerOrderDate.Value;
            if (request.UserName != null)
                order.UserName = request.UserName.Trim();
            if (request.Status != null)
            {
                order.Status = request.Status.Trim();
                order.LastStatusSource = "api";
                order.LastStatusReason = "patch-order";
                order.LastStatusAt = DateTime.Now;
            }
            if (request.Keyword != null)
                order.Keyword = request.Keyword.Trim();
            if (request.FolderName != null)
                order.FolderName = request.FolderName.Trim();
            if (request.PitStopAction != null)
                order.PitStopAction = request.PitStopAction.Trim();
            if (request.ImposingAction != null)
                order.ImposingAction = request.ImposingAction.Trim();

            order.Version++;
            AppendEvent(order.InternalId, string.Empty, "update-order", "api", actor, new { order_id = order.InternalId, version = order.Version });
            return StoreOperationResult.Success(CloneOrder(order));
        }
    }

    public StoreOperationResult TryAddItem(string orderId, AddOrderItemRequest request, string actor)
    {
        lock (_sync)
        {
            if (!_ordersById.TryGetValue(orderId ?? string.Empty, out var order))
                return StoreOperationResult.NotFound();

            if (order.Version != request.ExpectedOrderVersion)
                return StoreOperationResult.Conflict(order.Version, "order version mismatch");

            var item = CloneItem(request.Item);
            item.ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? Guid.NewGuid().ToString("N") : item.ItemId;
            item.SequenceNo = order.Items.Count;
            item.Version = 1;
            item.UpdatedAt = DateTime.Now;
            order.Items.Add(item);
            order.Version++;

            AppendEvent(order.InternalId, item.ItemId, "add-item", "api", actor, new { item_id = item.ItemId, sequence_no = item.SequenceNo, version = order.Version });
            return StoreOperationResult.Success(CloneOrder(order));
        }
    }

    public StoreOperationResult TryUpdateItem(string orderId, string itemId, UpdateOrderItemRequest request, string actor)
    {
        lock (_sync)
        {
            if (!_ordersById.TryGetValue(orderId ?? string.Empty, out var order))
                return StoreOperationResult.NotFound();

            if (order.Version != request.ExpectedOrderVersion)
                return StoreOperationResult.Conflict(order.Version, "order version mismatch");

            var item = order.Items.FirstOrDefault(x => string.Equals(x.ItemId, itemId, StringComparison.Ordinal));
            if (item == null)
                return StoreOperationResult.NotFound();

            if (item.Version != request.ExpectedItemVersion)
                return StoreOperationResult.Conflict(order.Version, "item version mismatch");

            if (request.ClientFileLabel != null)
                item.ClientFileLabel = request.ClientFileLabel.Trim();
            if (request.Variant != null)
                item.Variant = request.Variant.Trim();
            if (request.FileStatus != null)
                item.FileStatus = request.FileStatus.Trim();
            if (request.LastReason != null)
                item.LastReason = request.LastReason.Trim();
            if (request.SourcePath != null)
                item.SourcePath = request.SourcePath.Trim();
            if (request.PreparedPath != null)
                item.PreparedPath = request.PreparedPath.Trim();
            if (request.PrintPath != null)
                item.PrintPath = request.PrintPath.Trim();
            if (request.SourceFileHash != null)
                item.SourceFileHash = request.SourceFileHash.Trim();
            if (request.PreparedFileHash != null)
                item.PreparedFileHash = request.PreparedFileHash.Trim();
            if (request.PrintFileHash != null)
                item.PrintFileHash = request.PrintFileHash.Trim();
            if (request.PitStopAction != null)
                item.PitStopAction = request.PitStopAction.Trim();
            if (request.ImposingAction != null)
                item.ImposingAction = request.ImposingAction.Trim();

            item.Version++;
            item.UpdatedAt = DateTime.Now;
            order.Version++;

            AppendEvent(order.InternalId, item.ItemId, "update-item", "api", actor, new { item_id = item.ItemId, item_version = item.Version, order_version = order.Version });
            return StoreOperationResult.Success(CloneOrder(order));
        }
    }

    public StoreOperationResult TryDeleteItem(string orderId, string itemId, DeleteOrderItemRequest request, string actor)
    {
        lock (_sync)
        {
            if (!_ordersById.TryGetValue(orderId ?? string.Empty, out var order))
                return StoreOperationResult.NotFound();

            if (order.Version != request.ExpectedOrderVersion)
                return StoreOperationResult.Conflict(order.Version, "order version mismatch");

            var item = order.Items.FirstOrDefault(x => string.Equals(x.ItemId, itemId, StringComparison.Ordinal));
            if (item == null)
                return StoreOperationResult.NotFound();

            if (request.ExpectedItemVersion > 0 && item.Version != request.ExpectedItemVersion)
                return StoreOperationResult.Conflict(order.Version, "item version mismatch");

            order.Items.Remove(item);
            for (var i = 0; i < order.Items.Count; i++)
            {
                var row = order.Items[i];
                row.SequenceNo = i;
                row.Version++;
                row.UpdatedAt = DateTime.Now;
            }

            order.Version++;
            AppendEvent(order.InternalId, item.ItemId, "delete-item", "api", actor, new { item_id = item.ItemId, order_version = order.Version });
            return StoreOperationResult.Success(CloneOrder(order));
        }
    }

    public StoreOperationResult TryReorderItems(string orderId, ReorderOrderItemsRequest request, string actor)
    {
        lock (_sync)
        {
            if (!_ordersById.TryGetValue(orderId ?? string.Empty, out var order))
                return StoreOperationResult.NotFound();

            if (order.Version != request.ExpectedOrderVersion)
                return StoreOperationResult.Conflict(order.Version, "order version mismatch");

            if (request.OrderedItemIds == null || request.OrderedItemIds.Count != order.Items.Count)
                return StoreOperationResult.BadRequest("ordered item ids count mismatch");

            var normalizedIds = request.OrderedItemIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToList();
            if (normalizedIds.Count != order.Items.Count)
                return StoreOperationResult.BadRequest("ordered item ids count mismatch");

            var uniqueIds = new HashSet<string>(normalizedIds, StringComparer.Ordinal);
            if (uniqueIds.Count != normalizedIds.Count)
                return StoreOperationResult.BadRequest("ordered item ids must be unique");

            var byId = order.Items.ToDictionary(x => x.ItemId, x => x, StringComparer.Ordinal);
            var reordered = new List<SharedOrderItem>(order.Items.Count);
            foreach (var itemId in normalizedIds)
            {
                if (!byId.TryGetValue(itemId, out var item))
                    return StoreOperationResult.BadRequest($"unknown item id: {itemId}");

                reordered.Add(item);
            }

            order.Items.Clear();
            for (var i = 0; i < reordered.Count; i++)
            {
                var item = reordered[i];
                item.SequenceNo = i;
                item.UpdatedAt = DateTime.Now;
                order.Items.Add(item);
            }

            order.Version++;
            AppendEvent(order.InternalId, string.Empty, "topology", "api", actor, new { operation = "reorder-items", order_version = order.Version });
            return StoreOperationResult.Success(CloneOrder(order));
        }
    }

    public StoreOperationResult TryStartRun(string orderId, RunOrderRequest request, string actor)
    {
        lock (_sync)
        {
            if (!_ordersById.TryGetValue(orderId ?? string.Empty, out var order))
                return StoreOperationResult.NotFound();

            if (request.ExpectedOrderVersion > 0 && order.Version != request.ExpectedOrderVersion)
                return StoreOperationResult.Conflict(order.Version, "order version mismatch");

            if (_activeRunTokensByOrder.ContainsKey(order.InternalId))
                return StoreOperationResult.Conflict(order.Version, "run already active");

            var token = Guid.NewGuid().ToString("N");
            _activeRunTokensByOrder[order.InternalId] = token;

            order.Version++;
            order.Status = "Processing";
            order.LastStatusAt = DateTime.Now;
            order.LastStatusSource = "api";
            order.LastStatusReason = "run-started";

            AppendEvent(order.InternalId, string.Empty, "run", "api", actor, new
            {
                lease_token = token,
                version = order.Version
            });

            return StoreOperationResult.Success(CloneOrder(order));
        }
    }

    public StoreOperationResult TryStopRun(string orderId, StopOrderRequest request, string actor)
    {
        lock (_sync)
        {
            if (!_ordersById.TryGetValue(orderId ?? string.Empty, out var order))
                return StoreOperationResult.NotFound();

            if (request.ExpectedOrderVersion > 0 && order.Version != request.ExpectedOrderVersion)
                return StoreOperationResult.Conflict(order.Version, "order version mismatch");

            if (!_activeRunTokensByOrder.Remove(order.InternalId))
                return StoreOperationResult.BadRequest("run is not active");

            order.Version++;
            order.Status = "Cancelled";
            order.LastStatusAt = DateTime.Now;
            order.LastStatusSource = "api";
            order.LastStatusReason = "run-stopped";

            AppendEvent(order.InternalId, string.Empty, "stop", "api", actor, new
            {
                version = order.Version
            });

            return StoreOperationResult.Success(CloneOrder(order));
        }
    }

    private void AppendEvent(string orderInternalId, string itemId, string eventType, string eventSource, string actor, object payload)
    {
        _eventSequence++;
        _events.Add(new SharedOrderEvent
        {
            EventId = _eventSequence,
            OrderInternalId = orderInternalId ?? string.Empty,
            ItemId = itemId ?? string.Empty,
            EventType = eventType ?? string.Empty,
            EventSource = eventSource ?? string.Empty,
            CreatedBy = actor ?? string.Empty,
            CreatedAt = DateTime.Now,
            PayloadJson = JsonSerializer.Serialize(payload)
        });
    }

    private static SharedOrder CloneOrder(SharedOrder source)
    {
        return new SharedOrder
        {
            InternalId = source.InternalId,
            OrderNumber = source.OrderNumber,
            Keyword = source.Keyword,
            UserName = source.UserName,
            CreatedById = source.CreatedById,
            CreatedByUser = source.CreatedByUser,
            ArrivalDate = source.ArrivalDate,
            ManagerOrderDate = source.ManagerOrderDate,
            FolderName = source.FolderName,
            Status = source.Status,
            StartMode = source.StartMode,
            TopologyMarker = source.TopologyMarker,
            Version = source.Version,
            LastStatusReason = source.LastStatusReason,
            LastStatusSource = source.LastStatusSource,
            LastStatusAt = source.LastStatusAt,
            PitStopAction = source.PitStopAction,
            ImposingAction = source.ImposingAction,
            Items = source.Items.Select(CloneItem).ToList()
        };
    }

    private static SharedOrderItem CloneItem(SharedOrderItem source)
    {
        return new SharedOrderItem
        {
            ItemId = source.ItemId,
            Version = source.Version,
            SequenceNo = source.SequenceNo,
            ClientFileLabel = source.ClientFileLabel,
            Variant = source.Variant,
            SourcePath = source.SourcePath,
            SourceFileSizeBytes = source.SourceFileSizeBytes,
            SourceFileHash = source.SourceFileHash,
            PreparedPath = source.PreparedPath,
            PreparedFileSizeBytes = source.PreparedFileSizeBytes,
            PreparedFileHash = source.PreparedFileHash,
            PrintPath = source.PrintPath,
            PrintFileSizeBytes = source.PrintFileSizeBytes,
            PrintFileHash = source.PrintFileHash,
            FileStatus = source.FileStatus,
            LastReason = source.LastReason,
            UpdatedAt = source.UpdatedAt,
            PitStopAction = source.PitStopAction,
            ImposingAction = source.ImposingAction
        };
    }

    private static SharedUser CloneUser(SharedUser source)
    {
        return new SharedUser
        {
            Id = source.Id,
            Name = source.Name,
            Role = source.Role,
            IsActive = source.IsActive,
            UpdatedAt = source.UpdatedAt
        };
    }
}

public sealed class StoreOperationResult
{
    public bool IsSuccess { get; init; }
    public bool IsNotFound { get; init; }
    public bool IsConflict { get; init; }
    public bool IsBadRequest { get; init; }
    public string Error { get; init; } = string.Empty;
    public long CurrentVersion { get; init; }
    public SharedOrder? Order { get; init; }

    public static StoreOperationResult Success(SharedOrder order) => new() { IsSuccess = true, Order = order };
    public static StoreOperationResult NotFound() => new() { IsNotFound = true, Error = "not found" };
    public static StoreOperationResult Conflict(long currentVersion, string error) => new()
    {
        IsConflict = true,
        CurrentVersion = currentVersion,
        Error = error ?? string.Empty
    };

    public static StoreOperationResult BadRequest(string error) => new()
    {
        IsBadRequest = true,
        Error = error ?? string.Empty
    };
}
