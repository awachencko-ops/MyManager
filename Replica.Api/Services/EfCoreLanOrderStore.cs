using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Replica.Api.Contracts;
using Replica.Api.Data;
using Replica.Api.Data.Entities;
using Replica.Api.Infrastructure;
using Replica.Shared.Models;

namespace Replica.Api.Services;

public sealed class EfCoreLanOrderStore : ILanOrderStore
{
    private readonly IDbContextFactory<ReplicaDbContext> _dbContextFactory;
    private const string CreateOrderCommandName = "create-order";
    private const string DeleteOrderCommandName = "delete-order";
    private const string UpdateOrderCommandName = "update-order";
    private const string AddItemCommandName = "add-item";
    private const string UpdateItemCommandName = "update-item";
    private const string DeleteItemCommandName = "delete-item";
    private const string ReorderItemsCommandName = "reorder-items";
    private const string RunCommandName = "run";
    private const string StopCommandName = "stop";
    private const int MaxIdempotencyKeyLength = 128;

    private readonly List<SharedUser> _fallbackUsers = ReplicaApiBootstrapUsers.GetDefaultUsers().ToList();

    public EfCoreLanOrderStore(IDbContextFactory<ReplicaDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public IReadOnlyList<SharedUser> GetUsers(bool includeInactive = false)
    {
        try
        {
            using var db = _dbContextFactory.CreateDbContext();
            var query = db.Users
                .AsNoTracking()
                .OrderBy(x => x.UserName)
                .AsQueryable();
            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            var users = query.ToList();

            if (users.Count == 0)
                return _fallbackUsers.Select(CloneUser).ToList();

            return users.Select(MapUser).ToList();
        }
        catch
        {
            return _fallbackUsers.Select(CloneUser).ToList();
        }
    }

    public UserOperationResult UpsertUser(UpsertUserRequest request, string actor)
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

        using var db = _dbContextFactory.CreateDbContext();
        var existing = db.Users.FirstOrDefault(x => x.UserName == normalizedName);
        var nextIsActive = normalizedIsActive ?? existing?.IsActive ?? true;

        if (existing != null)
        {
            var otherActiveAdminsCount = db.Users.Count(x =>
                x.UserName != existing.UserName
                && x.IsActive
                && x.Role == ReplicaApiRoles.Admin);
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
            db.SaveChanges();
            return UserOperationResult.Success(MapUser(existing));
        }

        var created = new UserRecord
        {
            UserName = normalizedName,
            Role = normalizedRole,
            IsActive = nextIsActive,
            UpdatedAt = DateTime.Now
        };
        db.Users.Add(created);
        db.SaveChanges();
        return UserOperationResult.Success(MapUser(created));
    }

    public IReadOnlyList<SharedOrder> GetOrders(string createdBy)
    {
        using var db = _dbContextFactory.CreateDbContext();

        var orders = db.Orders
            .AsNoTracking()
            .OrderByDescending(x => x.OrderDate)
            .ThenByDescending(x => x.ArrivalDate)
            .ThenBy(x => x.InternalId)
            .ToList();

        if (orders.Count == 0)
            return new List<SharedOrder>();

        var orderIds = orders.Select(x => x.InternalId).ToList();
        var items = db.OrderItems
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderInternalId))
            .OrderBy(x => x.OrderInternalId)
            .ThenBy(x => x.SequenceNo)
            .ThenBy(x => x.ItemId)
            .ToList();

        var mapped = MapOrders(orders, items);
        if (string.IsNullOrWhiteSpace(createdBy))
            return mapped;

        var filter = createdBy.Trim();
        return mapped.Where(x =>
                string.Equals(x.CreatedById, filter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.CreatedByUser, filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public bool TryGetOrder(string orderId, out SharedOrder order)
    {
        order = new SharedOrder();
        if (string.IsNullOrWhiteSpace(orderId))
            return false;

        using var db = _dbContextFactory.CreateDbContext();
        var orderRecord = db.Orders
            .AsNoTracking()
            .FirstOrDefault(x => x.InternalId == orderId.Trim());

        if (orderRecord == null)
            return false;

        var itemRecords = db.OrderItems
            .AsNoTracking()
            .Where(x => x.OrderInternalId == orderRecord.InternalId)
            .OrderBy(x => x.SequenceNo)
            .ThenBy(x => x.ItemId)
            .ToList();

        order = MapOrder(orderRecord, itemRecords);
        return true;
    }

    public SharedOrder CreateOrder(CreateOrderRequest request, string actor)
    {
        var result = TryCreateOrder(request, actor, idempotencyKey: null);
        if (result.IsSuccess && result.Order != null)
            return CloneOrder(result.Order);

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error)
            ? "create-order failed"
            : result.Error);
    }

    public StoreOperationResult TryCreateOrder(CreateOrderRequest request, string actor, string? idempotencyKey)
    {
        request ??= new CreateOrderRequest();
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var requestFingerprint = BuildWriteRequestFingerprint(
            commandName: CreateOrderCommandName,
            orderId: string.Empty,
            actor: normalizedActor,
            requestPayload: new
            {
                request.OrderNumber,
                request.UserName,
                request.CreatedById,
                request.CreatedByUser,
                request.Status,
                request.Keyword,
                request.FolderName,
                request.StartMode,
                request.TopologyMarker,
                request.PitStopAction,
                request.ImposingAction,
                request.ArrivalDate,
                request.ManagerOrderDate,
                request.Items
            });

        return ExecuteWriteCommandWithOptionalIdempotency(
            commandName: CreateOrderCommandName,
            orderId: string.Empty,
            actor: normalizedActor,
            idempotencyKey: idempotencyKey,
            requestFingerprint: requestFingerprint,
            executeCore: () => TryCreateOrderCore(request, normalizedActor));
    }

    private StoreOperationResult TryCreateOrderCore(CreateOrderRequest request, string actor)
    {
        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var now = DateTime.Now;
        var managerOrderDate = request.ManagerOrderDate ?? DateTime.Today;
        var arrivalDate = request.ArrivalDate ?? now;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var order = new SharedOrder
        {
            InternalId = Guid.NewGuid().ToString("N"),
            OrderNumber = request.OrderNumber?.Trim() ?? string.Empty,
            UserName = request.UserName?.Trim() ?? string.Empty,
            CreatedById = request.CreatedById?.Trim() ?? normalizedActor,
            CreatedByUser = request.CreatedByUser?.Trim() ?? normalizedActor,
            Status = ReplicaApiWorkflowStatusNormalizer.NormalizeOrDefault(request.Status),
            Keyword = request.Keyword?.Trim() ?? string.Empty,
            FolderName = request.FolderName?.Trim() ?? string.Empty,
            StartMode = request.StartMode,
            TopologyMarker = request.TopologyMarker,
            PitStopAction = request.PitStopAction?.Trim() ?? "-",
            ImposingAction = request.ImposingAction?.Trim() ?? "-",
            ManagerOrderDate = managerOrderDate == default ? DateTime.Today : managerOrderDate,
            ArrivalDate = arrivalDate == default ? now : arrivalDate,
            LastStatusAt = now,
            LastStatusSource = "api",
            LastStatusReason = "create-order",
            Version = 1,
            Items = new List<SharedOrderItem>()
        };

        db.Orders.Add(BuildOrderRecord(order));

        var incomingItems = request.Items ?? new List<SharedOrderItem>();
        for (var i = 0; i < incomingItems.Count; i++)
        {
            var item = CloneItem(incomingItems[i]);
            item.ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? Guid.NewGuid().ToString("N") : item.ItemId.Trim();
            item.SequenceNo = i;
            item.Version = 1;
            item.UpdatedAt = now;
            item.FileStatus = ReplicaApiWorkflowStatusNormalizer.NormalizeOrDefault(item.FileStatus);
            order.Items.Add(item);

            db.OrderItems.Add(BuildOrderItemRecord(order.InternalId, item));
            db.OrderEvents.Add(BuildEventRecord(order.InternalId, item.ItemId, "add-item", "api", normalizedActor, new { item_id = item.ItemId, sequence_no = item.SequenceNo, order_version = order.Version }));
        }

        UpsertUser(db, order.CreatedByUser);
        UpsertUser(db, order.UserName);
        db.OrderEvents.Add(BuildEventRecord(order.InternalId, string.Empty, "add-order", "api", normalizedActor, new { order_id = order.InternalId, version = order.Version }));

        db.SaveChanges();
        tx.Commit();

        return StoreOperationResult.Success(CloneOrder(order));
    }

    public StoreOperationResult TryDeleteOrder(string orderId, DeleteOrderRequest request, string actor)
    {
        return TryDeleteOrder(orderId, request, actor, idempotencyKey: null);
    }

    public StoreOperationResult TryDeleteOrder(string orderId, DeleteOrderRequest request, string actor, string? idempotencyKey)
    {
        request ??= new DeleteOrderRequest();
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var requestFingerprint = BuildWriteRequestFingerprint(
            commandName: DeleteOrderCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            requestPayload: new { request.ExpectedVersion });

        return ExecuteWriteCommandWithOptionalIdempotency(
            commandName: DeleteOrderCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            idempotencyKey: idempotencyKey,
            requestFingerprint: requestFingerprint,
            executeCore: () => TryDeleteOrderCore(normalizedOrderId, request, normalizedActor));
    }

    private StoreOperationResult TryDeleteOrderCore(string orderId, DeleteOrderRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == orderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (request.ExpectedVersion > 0 && orderRecord.Version != request.ExpectedVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var snapshot = LoadOrder(db, orderRecord.InternalId) ?? DeserializeOrder(orderRecord);
        var lockRecord = db.OrderRunLocks.FirstOrDefault(x => x.OrderInternalId == orderRecord.InternalId);
        if (lockRecord != null)
            db.OrderRunLocks.Remove(lockRecord);

        db.OrderEvents.Add(BuildEventRecord(orderRecord.InternalId, string.Empty, "delete-order", "api", actor, new
        {
            order_id = orderRecord.InternalId,
            order_version = orderRecord.Version
        }));
        db.Orders.Remove(orderRecord);

        try
        {
            db.SaveChanges();
            tx.Commit();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");
        }

        return StoreOperationResult.Success(snapshot);
    }

    public StoreOperationResult TryUpdateOrder(string orderId, UpdateOrderRequest request, string actor)
    {
        return TryUpdateOrder(orderId, request, actor, idempotencyKey: null);
    }

    public StoreOperationResult TryUpdateOrder(string orderId, UpdateOrderRequest request, string actor, string? idempotencyKey)
    {
        request ??= new UpdateOrderRequest();
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var requestFingerprint = BuildWriteRequestFingerprint(
            commandName: UpdateOrderCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            requestPayload: request);

        return ExecuteWriteCommandWithOptionalIdempotency(
            commandName: UpdateOrderCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            idempotencyKey: idempotencyKey,
            requestFingerprint: requestFingerprint,
            executeCore: () => TryUpdateOrderCore(normalizedOrderId, request, normalizedActor));
    }

    private StoreOperationResult TryUpdateOrderCore(string orderId, UpdateOrderRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == orderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (orderRecord.Version != request.ExpectedVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var order = DeserializeOrder(orderRecord);

        if (request.OrderNumber != null)
            order.OrderNumber = request.OrderNumber.Trim();
        if (request.ManagerOrderDate.HasValue)
            order.ManagerOrderDate = request.ManagerOrderDate.Value;
        if (request.UserName != null)
            order.UserName = request.UserName.Trim();
        if (request.Status != null)
        {
            order.Status = ReplicaApiWorkflowStatusNormalizer.NormalizeOrDefault(request.Status);
            order.LastStatusAt = DateTime.Now;
            order.LastStatusSource = "api";
            order.LastStatusReason = "patch-order";
        }
        if (request.Keyword != null)
            order.Keyword = request.Keyword.Trim();
        if (request.FolderName != null)
            order.FolderName = request.FolderName.Trim();
        if (request.PitStopAction != null)
            order.PitStopAction = request.PitStopAction.Trim();
        if (request.ImposingAction != null)
            order.ImposingAction = request.ImposingAction.Trim();

        order.Version = orderRecord.Version + 1;
        ApplyOrderRecord(orderRecord, order);

        UpsertUser(db, order.UserName);
        db.OrderEvents.Add(BuildEventRecord(order.InternalId, string.Empty, "update-order", "api", actor, new { order_id = order.InternalId, version = order.Version }));

        try
        {
            db.SaveChanges();
            tx.Commit();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");
        }
        catch (DbUpdateException)
        {
            return StoreOperationResult.Conflict(orderRecord.Version, "run already active");
        }

        var updated = LoadOrder(db, orderRecord.InternalId) ?? order;
        return StoreOperationResult.Success(updated);
    }

    public StoreOperationResult TryAddItem(string orderId, AddOrderItemRequest request, string actor)
    {
        return TryAddItem(orderId, request, actor, idempotencyKey: null);
    }

    public StoreOperationResult TryAddItem(string orderId, AddOrderItemRequest request, string actor, string? idempotencyKey)
    {
        request ??= new AddOrderItemRequest();
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var requestFingerprint = BuildWriteRequestFingerprint(
            commandName: AddItemCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            requestPayload: new
            {
                request.ExpectedOrderVersion,
                request.Item
            });

        return ExecuteWriteCommandWithOptionalIdempotency(
            commandName: AddItemCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            idempotencyKey: idempotencyKey,
            requestFingerprint: requestFingerprint,
            executeCore: () => TryAddItemCore(normalizedOrderId, request, normalizedActor));
    }

    private StoreOperationResult TryAddItemCore(string orderId, AddOrderItemRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == orderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var order = DeserializeOrder(orderRecord);

        var nextSequenceNo = db.OrderItems.LongCount(x => x.OrderInternalId == orderRecord.InternalId);
        var item = CloneItem(request.Item);
        item.ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? Guid.NewGuid().ToString("N") : item.ItemId.Trim();
        item.SequenceNo = nextSequenceNo;
        item.Version = 1;
        item.UpdatedAt = DateTime.Now;
        item.FileStatus = ReplicaApiWorkflowStatusNormalizer.NormalizeOrDefault(item.FileStatus);

        db.OrderItems.Add(BuildOrderItemRecord(orderRecord.InternalId, item));

        order.Version = orderRecord.Version + 1;
        ApplyOrderRecord(orderRecord, order);

        db.OrderEvents.Add(BuildEventRecord(orderRecord.InternalId, item.ItemId, "add-item", "api", actor, new { item_id = item.ItemId, sequence_no = item.SequenceNo, version = order.Version }));

        try
        {
            db.SaveChanges();
            tx.Commit();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");
        }

        var updated = LoadOrder(db, orderRecord.InternalId) ?? order;
        return StoreOperationResult.Success(updated);
    }

    public StoreOperationResult TryUpdateItem(string orderId, string itemId, UpdateOrderItemRequest request, string actor)
    {
        return TryUpdateItem(orderId, itemId, request, actor, idempotencyKey: null);
    }

    public StoreOperationResult TryUpdateItem(string orderId, string itemId, UpdateOrderItemRequest request, string actor, string? idempotencyKey)
    {
        request ??= new UpdateOrderItemRequest();
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var normalizedItemId = itemId?.Trim() ?? string.Empty;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var requestFingerprint = BuildWriteRequestFingerprint(
            commandName: UpdateItemCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            requestPayload: new
            {
                item_id = normalizedItemId,
                request.ExpectedOrderVersion,
                request.ExpectedItemVersion,
                request.ClientFileLabel,
                request.Variant,
                request.FileStatus,
                request.LastReason,
                request.SourcePath,
                request.PreparedPath,
                request.PrintPath,
                request.SourceFileHash,
                request.PreparedFileHash,
                request.PrintFileHash,
                request.PitStopAction,
                request.ImposingAction
            });

        return ExecuteWriteCommandWithOptionalIdempotency(
            commandName: UpdateItemCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            idempotencyKey: idempotencyKey,
            requestFingerprint: requestFingerprint,
            executeCore: () => TryUpdateItemCore(normalizedOrderId, normalizedItemId, request, normalizedActor));
    }

    private StoreOperationResult TryUpdateItemCore(string orderId, string itemId, UpdateOrderItemRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(itemId))
            return StoreOperationResult.BadRequest("order id and item id are required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == orderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var itemRecord = db.OrderItems.FirstOrDefault(x => x.OrderInternalId == orderId && x.ItemId == itemId);
        if (itemRecord == null)
            return StoreOperationResult.NotFound();

        if (itemRecord.Version != request.ExpectedItemVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "item version mismatch");

        var item = DeserializeItem(itemRecord);

        if (request.ClientFileLabel != null)
            item.ClientFileLabel = request.ClientFileLabel.Trim();
        if (request.Variant != null)
            item.Variant = request.Variant.Trim();
            if (request.FileStatus != null)
                item.FileStatus = ReplicaApiWorkflowStatusNormalizer.NormalizeOrDefault(request.FileStatus);
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

        item.UpdatedAt = DateTime.Now;
        item.Version = itemRecord.Version + 1;
        ApplyOrderItemRecord(itemRecord, item);

        var order = DeserializeOrder(orderRecord);
        order.Version = orderRecord.Version + 1;
        ApplyOrderRecord(orderRecord, order);

        db.OrderEvents.Add(BuildEventRecord(orderRecord.InternalId, item.ItemId, "update-item", "api", actor, new { item_id = item.ItemId, item_version = item.Version, order_version = order.Version }));

        try
        {
            db.SaveChanges();
            tx.Commit();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StoreOperationResult.Conflict(orderRecord.Version, "item/order version mismatch");
        }

        var updated = LoadOrder(db, orderRecord.InternalId) ?? order;
        return StoreOperationResult.Success(updated);
    }

    public StoreOperationResult TryDeleteItem(string orderId, string itemId, DeleteOrderItemRequest request, string actor)
    {
        return TryDeleteItem(orderId, itemId, request, actor, idempotencyKey: null);
    }

    public StoreOperationResult TryDeleteItem(string orderId, string itemId, DeleteOrderItemRequest request, string actor, string? idempotencyKey)
    {
        request ??= new DeleteOrderItemRequest();
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var normalizedItemId = itemId?.Trim() ?? string.Empty;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var requestFingerprint = BuildWriteRequestFingerprint(
            commandName: DeleteItemCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            requestPayload: new
            {
                item_id = normalizedItemId,
                request.ExpectedOrderVersion,
                request.ExpectedItemVersion
            });

        return ExecuteWriteCommandWithOptionalIdempotency(
            commandName: DeleteItemCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            idempotencyKey: idempotencyKey,
            requestFingerprint: requestFingerprint,
            executeCore: () => TryDeleteItemCore(normalizedOrderId, normalizedItemId, request, normalizedActor));
    }

    private StoreOperationResult TryDeleteItemCore(string orderId, string itemId, DeleteOrderItemRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(itemId))
            return StoreOperationResult.BadRequest("order id and item id are required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == orderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var itemRecord = db.OrderItems.FirstOrDefault(x => x.OrderInternalId == orderId && x.ItemId == itemId);
        if (itemRecord == null)
            return StoreOperationResult.NotFound();

        if (request.ExpectedItemVersion > 0 && itemRecord.Version != request.ExpectedItemVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "item version mismatch");

        db.OrderItems.Remove(itemRecord);
        db.SaveChanges();

        var remainingRecords = db.OrderItems
            .Where(x => x.OrderInternalId == orderId && x.ItemId != itemId)
            .OrderBy(x => x.SequenceNo)
            .ThenBy(x => x.ItemId)
            .ToList();
        for (var i = 0; i < remainingRecords.Count; i++)
        {
            var row = remainingRecords[i];
            var mapped = DeserializeItem(row);
            mapped.SequenceNo = i;
            mapped.Version = row.Version + 1;
            mapped.UpdatedAt = DateTime.Now;
            ApplyOrderItemRecord(row, mapped);
        }

        var order = DeserializeOrder(orderRecord);
        order.Version = orderRecord.Version + 1;
        ApplyOrderRecord(orderRecord, order);

        db.OrderEvents.Add(BuildEventRecord(orderRecord.InternalId, itemId, "delete-item", "api", actor, new { item_id = itemId, order_version = order.Version }));

        try
        {
            db.SaveChanges();
            tx.Commit();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StoreOperationResult.Conflict(orderRecord.Version, "item/order version mismatch");
        }

        var updated = LoadOrder(db, orderRecord.InternalId) ?? order;
        return StoreOperationResult.Success(updated);
    }

    public StoreOperationResult TryReorderItems(string orderId, ReorderOrderItemsRequest request, string actor)
    {
        return TryReorderItems(orderId, request, actor, idempotencyKey: null);
    }

    public StoreOperationResult TryReorderItems(string orderId, ReorderOrderItemsRequest request, string actor, string? idempotencyKey)
    {
        request ??= new ReorderOrderItemsRequest();
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var normalizedItemIds = (request.OrderedItemIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();
        var requestFingerprint = BuildWriteRequestFingerprint(
            commandName: ReorderItemsCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            requestPayload: new
            {
                request.ExpectedOrderVersion,
                ordered_item_ids = normalizedItemIds
            });

        return ExecuteWriteCommandWithOptionalIdempotency(
            commandName: ReorderItemsCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            idempotencyKey: idempotencyKey,
            requestFingerprint: requestFingerprint,
            executeCore: () =>
            {
                var coreRequest = new ReorderOrderItemsRequest
                {
                    ExpectedOrderVersion = request.ExpectedOrderVersion,
                    OrderedItemIds = normalizedItemIds
                };
                return TryReorderItemsCore(normalizedOrderId, coreRequest, normalizedActor);
            });
    }

    private StoreOperationResult TryReorderItemsCore(string orderId, ReorderOrderItemsRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == orderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var itemRecords = db.OrderItems
            .Where(x => x.OrderInternalId == orderId)
            .OrderBy(x => x.SequenceNo)
            .ThenBy(x => x.ItemId)
            .ToList();

        var normalizedIds = (request.OrderedItemIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();

        if (normalizedIds.Count != itemRecords.Count)
            return StoreOperationResult.BadRequest("ordered item ids count mismatch");

        var uniqueIds = new HashSet<string>(normalizedIds, StringComparer.Ordinal);
        if (uniqueIds.Count != normalizedIds.Count)
            return StoreOperationResult.BadRequest("ordered item ids must be unique");

        var itemsById = itemRecords.ToDictionary(x => x.ItemId, x => x, StringComparer.Ordinal);
        foreach (var id in normalizedIds)
        {
            if (!itemsById.ContainsKey(id))
                return StoreOperationResult.BadRequest($"unknown item id: {id}");
        }

        var maxSequence = itemRecords.Count == 0 ? 0L : itemRecords.Max(x => x.SequenceNo);
        var tempOffset = maxSequence + itemRecords.Count + 100;
        foreach (var row in itemRecords)
            row.SequenceNo += tempOffset;

        db.SaveChanges();

        for (var i = 0; i < normalizedIds.Count; i++)
        {
            var row = itemsById[normalizedIds[i]];
            row.SequenceNo = i;
            row.Version += 1;
            row.UpdatedAt = DateTime.Now;
        }

        var order = DeserializeOrder(orderRecord);
        order.Version = orderRecord.Version + 1;
        ApplyOrderRecord(orderRecord, order);

        db.OrderEvents.Add(BuildEventRecord(orderRecord.InternalId, string.Empty, "topology", "api", actor, new { operation = "reorder-items", order_version = order.Version }));

        try
        {
            db.SaveChanges();
            tx.Commit();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StoreOperationResult.Conflict(orderRecord.Version, "order/item version mismatch");
        }

        var updated = LoadOrder(db, orderRecord.InternalId) ?? order;
        return StoreOperationResult.Success(updated);
    }

    public StoreOperationResult TryStartRun(string orderId, RunOrderRequest request, string actor)
    {
        return TryStartRun(orderId, request, actor, idempotencyKey: null);
    }

    public StoreOperationResult TryStopRun(string orderId, StopOrderRequest request, string actor)
    {
        return TryStopRun(orderId, request, actor, idempotencyKey: null);
    }

    public StoreOperationResult TryStartRun(string orderId, RunOrderRequest request, string actor, string? idempotencyKey)
    {
        request ??= new RunOrderRequest();
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var requestFingerprint = BuildWriteRequestFingerprint(
            commandName: RunCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            requestPayload: new { request.ExpectedOrderVersion });

        return ExecuteWriteCommandWithOptionalIdempotency(
            commandName: RunCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            idempotencyKey: idempotencyKey,
            requestFingerprint: requestFingerprint,
            executeCore: () => TryStartRunCore(normalizedOrderId, request, normalizedActor));
    }

    public StoreOperationResult TryStopRun(string orderId, StopOrderRequest request, string actor, string? idempotencyKey)
    {
        request ??= new StopOrderRequest();
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var requestFingerprint = BuildWriteRequestFingerprint(
            commandName: StopCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            requestPayload: new { request.ExpectedOrderVersion });

        return ExecuteWriteCommandWithOptionalIdempotency(
            commandName: StopCommandName,
            orderId: normalizedOrderId,
            actor: normalizedActor,
            idempotencyKey: idempotencyKey,
            requestFingerprint: requestFingerprint,
            executeCore: () => TryStopRunCore(normalizedOrderId, request, normalizedActor));
    }

    private StoreOperationResult ExecuteWriteCommandWithOptionalIdempotency(
        string commandName,
        string orderId,
        string actor,
        string? idempotencyKey,
        string requestFingerprint,
        Func<StoreOperationResult> executeCore)
    {
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
            return executeCore();

        if (TryGetStoredWriteCommandResult(commandName, normalizedKey, requestFingerprint, out var cachedResult, out var mismatchError))
        {
            if (string.IsNullOrWhiteSpace(mismatchError))
            {
                ReplicaApiObservability.RecordIdempotency(commandName, IdempotencyTelemetryOutcome.Hit);
                return cachedResult;
            }

            ReplicaApiObservability.RecordIdempotency(commandName, IdempotencyTelemetryOutcome.Mismatch);
            return StoreOperationResult.BadRequest(mismatchError);
        }

        ReplicaApiObservability.RecordIdempotency(commandName, IdempotencyTelemetryOutcome.Miss);
        var executed = executeCore();
        var stored = TryStoreWriteCommandResult(
            commandName: commandName,
            idempotencyKey: normalizedKey,
            requestFingerprint: requestFingerprint,
            actor: normalizedActor,
            orderInternalId: normalizedOrderId,
            result: executed,
            out var storeError);

        if (!string.IsNullOrWhiteSpace(storeError))
        {
            ReplicaApiObservability.RecordIdempotency(commandName, IdempotencyTelemetryOutcome.Mismatch);
            return StoreOperationResult.BadRequest(storeError);
        }

        return stored;
    }

    private StoreOperationResult TryStartRunCore(string orderId, RunOrderRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == orderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (request.ExpectedOrderVersion > 0 && orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var lockRecord = db.OrderRunLocks.FirstOrDefault(x => x.OrderInternalId == orderId);
        if (lockRecord != null && lockRecord.IsActive)
            return StoreOperationResult.Conflict(orderRecord.Version, "run already active");

        var leaseToken = Guid.NewGuid().ToString("N");
        if (lockRecord == null)
        {
            db.OrderRunLocks.Add(new OrderRunLockRecord
            {
                OrderInternalId = orderId,
                IsActive = true,
                LeaseToken = leaseToken,
                LeaseOwner = actor ?? string.Empty,
                StartedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        }
        else
        {
            lockRecord.IsActive = true;
            lockRecord.LeaseToken = leaseToken;
            lockRecord.LeaseOwner = actor ?? string.Empty;
            lockRecord.StartedAt = DateTime.Now;
            lockRecord.UpdatedAt = DateTime.Now;
        }

        var order = DeserializeOrder(orderRecord);
        order.Version = orderRecord.Version + 1;
        order.Status = "Processing";
        order.LastStatusAt = DateTime.Now;
        order.LastStatusSource = "api";
        order.LastStatusReason = "run-started";
        ApplyOrderRecord(orderRecord, order);

        db.OrderEvents.Add(BuildEventRecord(orderRecord.InternalId, string.Empty, "run", "api", actor ?? string.Empty, new
        {
            lease_token = leaseToken,
            order_version = order.Version
        }));

        try
        {
            db.SaveChanges();
            tx.Commit();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");
        }

        var updated = LoadOrder(db, orderRecord.InternalId) ?? order;
        return StoreOperationResult.Success(updated);
    }

    private StoreOperationResult TryStopRunCore(string orderId, StopOrderRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == orderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (request.ExpectedOrderVersion > 0 && orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var lockRecord = db.OrderRunLocks.FirstOrDefault(x => x.OrderInternalId == orderId);
        if (lockRecord == null || !lockRecord.IsActive)
            return StoreOperationResult.BadRequest("run is not active");

        lockRecord.IsActive = false;
        lockRecord.UpdatedAt = DateTime.Now;

        var order = DeserializeOrder(orderRecord);
        order.Version = orderRecord.Version + 1;
        order.Status = "Cancelled";
        order.LastStatusAt = DateTime.Now;
        order.LastStatusSource = "api";
        order.LastStatusReason = "run-stopped";
        ApplyOrderRecord(orderRecord, order);

        db.OrderEvents.Add(BuildEventRecord(orderRecord.InternalId, string.Empty, "stop", "api", actor, new
        {
            lease_token = lockRecord.LeaseToken,
            order_version = order.Version
        }));

        try
        {
            db.SaveChanges();
            tx.Commit();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");
        }

        var updated = LoadOrder(db, orderRecord.InternalId) ?? order;
        return StoreOperationResult.Success(updated);
    }

    private bool TryGetStoredWriteCommandResult(
        string commandName,
        string idempotencyKey,
        string requestFingerprint,
        out StoreOperationResult cachedResult,
        out string mismatchError)
    {
        cachedResult = StoreOperationResult.BadRequest(string.Empty);
        mismatchError = string.Empty;

        using var db = _dbContextFactory.CreateDbContext();
        var entry = db.OrderWriteIdempotency
            .AsNoTracking()
            .FirstOrDefault(x =>
                x.CommandName == commandName
                && x.IdempotencyKey == idempotencyKey);
        if (entry == null)
            return false;

        if (!string.Equals(entry.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            mismatchError = "idempotency key reuse with different request payload";
            return true;
        }

        cachedResult = DeserializeStoredWriteCommandResult(entry);
        return true;
    }

    private StoreOperationResult TryStoreWriteCommandResult(
        string commandName,
        string idempotencyKey,
        string requestFingerprint,
        string actor,
        string orderInternalId,
        StoreOperationResult result,
        out string storeError)
    {
        storeError = string.Empty;
        using var db = _dbContextFactory.CreateDbContext();

        var effectiveOrderInternalId = !string.IsNullOrWhiteSpace(result.Order?.InternalId)
            ? result.Order!.InternalId.Trim()
            : (orderInternalId ?? string.Empty).Trim();
        var entry = new OrderWriteIdempotencyRecord
        {
            CommandName = commandName,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            Actor = actor ?? string.Empty,
            OrderInternalId = effectiveOrderInternalId,
            ResultKind = ToResultKind(result),
            Error = result.Error ?? string.Empty,
            CurrentVersion = result.CurrentVersion,
            ResponseOrderJson = result.Order == null
                ? "{}"
                : JsonSerializer.Serialize(result.Order),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        db.OrderWriteIdempotency.Add(entry);
        try
        {
            db.SaveChanges();
            return result;
        }
        catch (DbUpdateException)
        {
            if (TryGetStoredWriteCommandResult(commandName, idempotencyKey, requestFingerprint, out var cached, out var mismatchError))
            {
                storeError = mismatchError;
                return cached;
            }

            return result;
        }
    }

    private static string ToResultKind(StoreOperationResult result)
    {
        if (result.IsSuccess)
            return "success";
        if (result.IsNotFound)
            return "not_found";
        if (result.IsConflict)
            return "conflict";
        return "bad_request";
    }

    private static StoreOperationResult DeserializeStoredWriteCommandResult(OrderWriteIdempotencyRecord entry)
    {
        var kind = entry.ResultKind?.Trim().ToLowerInvariant() ?? string.Empty;
        return kind switch
        {
            "success" => StoreOperationResult.Success(DeserializeStoredOrder(entry.ResponseOrderJson)),
            "not_found" => StoreOperationResult.NotFound(),
            "conflict" => StoreOperationResult.Conflict(entry.CurrentVersion, entry.Error),
            _ => StoreOperationResult.BadRequest(entry.Error)
        };
    }

    private static SharedOrder DeserializeStoredOrder(string payloadJson)
    {
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                var order = JsonSerializer.Deserialize<SharedOrder>(payloadJson);
                if (order != null)
                    return order;
            }
            catch
            {
                // keep fallback below
            }
        }

        return new SharedOrder();
    }

    private static string NormalizeIdempotencyKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return string.Empty;

        var trimmed = rawKey.Trim();
        return trimmed.Length <= MaxIdempotencyKeyLength ? trimmed : trimmed[..MaxIdempotencyKeyLength];
    }

    private static string BuildWriteRequestFingerprint(string commandName, string orderId, string actor, object? requestPayload)
    {
        var payloadJson = requestPayload == null
            ? string.Empty
            : JsonSerializer.Serialize(requestPayload);
        var source = $"{commandName}|{orderId}|{actor}|{payloadJson}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static SharedOrder? LoadOrder(ReplicaDbContext db, string orderInternalId)
    {
        var orderRecord = db.Orders
            .AsNoTracking()
            .FirstOrDefault(x => x.InternalId == orderInternalId);
        if (orderRecord == null)
            return null;

        var itemRecords = db.OrderItems
            .AsNoTracking()
            .Where(x => x.OrderInternalId == orderInternalId)
            .OrderBy(x => x.SequenceNo)
            .ThenBy(x => x.ItemId)
            .ToList();

        return MapOrder(orderRecord, itemRecords);
    }

    private static List<SharedOrder> MapOrders(List<OrderRecord> orderRecords, List<OrderItemRecord> itemRecords)
    {
        var itemsByOrder = itemRecords
            .GroupBy(x => x.OrderInternalId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);

        var mapped = new List<SharedOrder>(orderRecords.Count);
        foreach (var orderRecord in orderRecords)
        {
            var items = itemsByOrder.TryGetValue(orderRecord.InternalId, out var grouped)
                ? grouped
                : new List<OrderItemRecord>();
            mapped.Add(MapOrder(orderRecord, items));
        }

        return mapped;
    }

    private static SharedOrder MapOrder(OrderRecord orderRecord, List<OrderItemRecord> itemRecords)
    {
        var order = DeserializeOrder(orderRecord);
        order.Items = itemRecords.Select(DeserializeItem).ToList();
        return order;
    }

    private static SharedOrder DeserializeOrder(OrderRecord record)
    {
        SharedOrder? order = null;
        if (!string.IsNullOrWhiteSpace(record.PayloadJson))
        {
            try
            {
                order = JsonSerializer.Deserialize<SharedOrder>(record.PayloadJson);
            }
            catch
            {
                order = null;
            }
        }

        order ??= new SharedOrder();
        order.InternalId = string.IsNullOrWhiteSpace(order.InternalId) ? record.InternalId : order.InternalId;
        order.OrderNumber = string.IsNullOrWhiteSpace(order.OrderNumber) ? record.OrderNumber : order.OrderNumber;
        order.UserName = string.IsNullOrWhiteSpace(order.UserName) ? record.UserName : order.UserName;
        order.Status = string.IsNullOrWhiteSpace(order.Status) ? record.Status : order.Status;
        order.ArrivalDate = order.ArrivalDate == default ? record.ArrivalDate : order.ArrivalDate;
        order.ManagerOrderDate = order.ManagerOrderDate == default ? record.OrderDate : order.ManagerOrderDate;
        order.StartMode = order.StartMode == SharedOrderStartMode.Unknown
            ? (SharedOrderStartMode)record.StartMode
            : order.StartMode;
        order.TopologyMarker = order.TopologyMarker == SharedOrderTopologyMarker.Unknown
            ? (SharedOrderTopologyMarker)record.TopologyMarker
            : order.TopologyMarker;
        order.Version = record.Version;
        order.Items ??= new List<SharedOrderItem>();

        return order;
    }

    private static SharedOrderItem DeserializeItem(OrderItemRecord record)
    {
        SharedOrderItem? item = null;
        if (!string.IsNullOrWhiteSpace(record.PayloadJson))
        {
            try
            {
                item = JsonSerializer.Deserialize<SharedOrderItem>(record.PayloadJson);
            }
            catch
            {
                item = null;
            }
        }

        item ??= new SharedOrderItem();
        item.ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? record.ItemId : item.ItemId;
        item.SequenceNo = record.SequenceNo;
        item.Version = record.Version;
        item.UpdatedAt = record.UpdatedAt;
        item.ClientFileLabel ??= string.Empty;
        item.Variant ??= string.Empty;
        item.SourcePath ??= string.Empty;
        item.PreparedPath ??= string.Empty;
        item.PrintPath ??= string.Empty;
        item.SourceFileHash ??= string.Empty;
        item.PreparedFileHash ??= string.Empty;
        item.PrintFileHash ??= string.Empty;
        item.FileStatus ??= string.Empty;
        item.LastReason ??= string.Empty;
        item.PitStopAction ??= "-";
        item.ImposingAction ??= "-";

        return item;
    }

    private static OrderRecord BuildOrderRecord(SharedOrder order)
    {
        return new OrderRecord
        {
            InternalId = order.InternalId,
            OrderNumber = order.OrderNumber ?? string.Empty,
            UserName = order.UserName ?? string.Empty,
            Status = order.Status ?? string.Empty,
            ArrivalDate = order.ArrivalDate == default ? DateTime.Now : order.ArrivalDate,
            OrderDate = order.ManagerOrderDate == default ? DateTime.Today : order.ManagerOrderDate,
            StartMode = (int)order.StartMode,
            TopologyMarker = (int)order.TopologyMarker,
            PayloadJson = JsonSerializer.Serialize(order),
            Version = order.Version <= 0 ? 1 : order.Version,
            UpdatedAt = DateTime.Now
        };
    }

    private static void ApplyOrderRecord(OrderRecord target, SharedOrder source)
    {
        target.OrderNumber = source.OrderNumber ?? string.Empty;
        target.UserName = source.UserName ?? string.Empty;
        target.Status = source.Status ?? string.Empty;
        target.ArrivalDate = source.ArrivalDate == default ? DateTime.Now : source.ArrivalDate;
        target.OrderDate = source.ManagerOrderDate == default ? DateTime.Today : source.ManagerOrderDate;
        target.StartMode = (int)source.StartMode;
        target.TopologyMarker = (int)source.TopologyMarker;
        target.PayloadJson = JsonSerializer.Serialize(source);
        target.Version = source.Version <= 0 ? target.Version : source.Version;
        target.UpdatedAt = DateTime.Now;
    }

    private static OrderItemRecord BuildOrderItemRecord(string orderInternalId, SharedOrderItem item)
    {
        return new OrderItemRecord
        {
            ItemId = item.ItemId,
            OrderInternalId = orderInternalId,
            SequenceNo = item.SequenceNo,
            PayloadJson = JsonSerializer.Serialize(item),
            Version = item.Version <= 0 ? 1 : item.Version,
            UpdatedAt = item.UpdatedAt == default ? DateTime.Now : item.UpdatedAt
        };
    }

    private static void ApplyOrderItemRecord(OrderItemRecord target, SharedOrderItem source)
    {
        target.SequenceNo = source.SequenceNo;
        target.PayloadJson = JsonSerializer.Serialize(source);
        target.Version = source.Version <= 0 ? target.Version : source.Version;
        target.UpdatedAt = source.UpdatedAt == default ? DateTime.Now : source.UpdatedAt;
    }

    private static void UpsertUser(ReplicaDbContext db, string userName, string role = ReplicaApiRoles.Operator)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return;

        var normalized = userName.Trim();
        var existing = db.Users.FirstOrDefault(x => x.UserName == normalized);
        if (existing == null)
        {
            db.Users.Add(new UserRecord
            {
                UserName = normalized,
                Role = ReplicaApiRoles.Normalize(role),
                IsActive = true,
                UpdatedAt = DateTime.Now
            });
            return;
        }

        existing.Role = string.IsNullOrWhiteSpace(existing.Role)
            ? ReplicaApiRoles.Normalize(role)
            : ReplicaApiRoles.Normalize(existing.Role);
        existing.IsActive = true;
        existing.UpdatedAt = DateTime.Now;
    }

    private static OrderEventRecord BuildEventRecord(string orderId, string itemId, string eventType, string eventSource, string actor, object payload)
    {
        return new OrderEventRecord
        {
            OrderInternalId = orderId ?? string.Empty,
            ItemId = itemId ?? string.Empty,
            EventType = eventType ?? string.Empty,
            EventSource = eventSource ?? string.Empty,
            PayloadJson = JsonSerializer.Serialize(new
            {
                actor = actor ?? string.Empty,
                payload
            }),
            CreatedAt = DateTime.Now
        };
    }

    private static string BuildUserId(string userName)
    {
        var normalized = string.IsNullOrWhiteSpace(userName) ? "unknown" : userName.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "u-" + Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    private static SharedUser MapUser(UserRecord source)
    {
        return new SharedUser
        {
            Id = BuildUserId(source.UserName),
            Name = source.UserName,
            Role = ReplicaApiRoles.Normalize(source.Role),
            IsActive = source.IsActive,
            UpdatedAt = source.UpdatedAt
        };
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
