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
using Replica.Shared.Models;

namespace Replica.Api.Services;

public sealed class EfCoreLanOrderStore : ILanOrderStore
{
    private readonly IDbContextFactory<ReplicaDbContext> _dbContextFactory;
    private const string RunCommandName = "run";
    private const string StopCommandName = "stop";
    private const int MaxIdempotencyKeyLength = 128;

    private readonly List<SharedUser> _fallbackUsers =
    [
        new() { Id = "u-admin", Name = "Administrator", Role = "Admin", IsActive = true },
        new() { Id = "u-operator-1", Name = "Operator 1", Role = "Operator", IsActive = true },
        new() { Id = "u-operator-2", Name = "Operator 2", Role = "Operator", IsActive = true }
    ];

    public EfCoreLanOrderStore(IDbContextFactory<ReplicaDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public IReadOnlyList<SharedUser> GetUsers()
    {
        try
        {
            using var db = _dbContextFactory.CreateDbContext();
            var users = db.Users
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.UserName)
                .ToList();

            if (users.Count == 0)
                return _fallbackUsers.Select(CloneUser).ToList();

            return users.Select(x => new SharedUser
            {
                Id = BuildUserId(x.UserName),
                Name = x.UserName,
                Role = "Operator",
                IsActive = x.IsActive,
                UpdatedAt = x.UpdatedAt
            }).ToList();
        }
        catch
        {
            return _fallbackUsers.Select(CloneUser).ToList();
        }
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
            Status = request.Status?.Trim() ?? "Waiting",
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
            item.FileStatus = string.IsNullOrWhiteSpace(item.FileStatus) ? "Waiting" : item.FileStatus.Trim();
            order.Items.Add(item);

            db.OrderItems.Add(BuildOrderItemRecord(order.InternalId, item));
            db.OrderEvents.Add(BuildEventRecord(order.InternalId, item.ItemId, "add-item", "api", normalizedActor, new { item_id = item.ItemId, sequence_no = item.SequenceNo, order_version = order.Version }));
        }

        UpsertUser(db, order.CreatedByUser);
        UpsertUser(db, order.UserName);
        db.OrderEvents.Add(BuildEventRecord(order.InternalId, string.Empty, "add-order", "api", normalizedActor, new { order_id = order.InternalId, version = order.Version }));

        db.SaveChanges();
        tx.Commit();

        return CloneOrder(order);
    }

    public StoreOperationResult TryUpdateOrder(string orderId, UpdateOrderRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == orderId.Trim());
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
            order.Status = request.Status.Trim();
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
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == orderId.Trim());
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
        item.FileStatus = string.IsNullOrWhiteSpace(item.FileStatus) ? "Waiting" : item.FileStatus.Trim();

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
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(itemId))
            return StoreOperationResult.BadRequest("order id and item id are required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var normalizedOrderId = orderId.Trim();
        var normalizedItemId = itemId.Trim();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == normalizedOrderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var itemRecord = db.OrderItems.FirstOrDefault(x => x.OrderInternalId == normalizedOrderId && x.ItemId == normalizedItemId);
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
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(itemId))
            return StoreOperationResult.BadRequest("order id and item id are required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var normalizedOrderId = orderId.Trim();
        var normalizedItemId = itemId.Trim();

        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == normalizedOrderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var itemRecord = db.OrderItems.FirstOrDefault(x => x.OrderInternalId == normalizedOrderId && x.ItemId == normalizedItemId);
        if (itemRecord == null)
            return StoreOperationResult.NotFound();

        if (request.ExpectedItemVersion > 0 && itemRecord.Version != request.ExpectedItemVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "item version mismatch");

        db.OrderItems.Remove(itemRecord);
        db.SaveChanges();

        var remainingRecords = db.OrderItems
            .Where(x => x.OrderInternalId == normalizedOrderId && x.ItemId != normalizedItemId)
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

        db.OrderEvents.Add(BuildEventRecord(orderRecord.InternalId, normalizedItemId, "delete-item", "api", actor, new { item_id = normalizedItemId, order_version = order.Version }));

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
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var normalizedOrderId = orderId.Trim();
        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == normalizedOrderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var itemRecords = db.OrderItems
            .Where(x => x.OrderInternalId == normalizedOrderId)
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
            row.SequenceNo = row.SequenceNo + tempOffset;

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
        return ExecuteRunCommandWithOptionalIdempotency(
            commandName: RunCommandName,
            orderId: orderId,
            expectedOrderVersion: request.ExpectedOrderVersion,
            actor: actor,
            idempotencyKey: idempotencyKey,
            executeCore: () => TryStartRunCore(orderId, request, actor));
    }

    public StoreOperationResult TryStopRun(string orderId, StopOrderRequest request, string actor, string? idempotencyKey)
    {
        request ??= new StopOrderRequest();
        return ExecuteRunCommandWithOptionalIdempotency(
            commandName: StopCommandName,
            orderId: orderId,
            expectedOrderVersion: request.ExpectedOrderVersion,
            actor: actor,
            idempotencyKey: idempotencyKey,
            executeCore: () => TryStopRunCore(orderId, request, actor));
    }

    private StoreOperationResult ExecuteRunCommandWithOptionalIdempotency(
        string commandName,
        string orderId,
        long expectedOrderVersion,
        string actor,
        string? idempotencyKey,
        Func<StoreOperationResult> executeCore)
    {
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var normalizedActor = actor?.Trim() ?? string.Empty;
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
            return executeCore();

        var requestFingerprint = BuildRunRequestFingerprint(commandName, normalizedOrderId, normalizedActor, expectedOrderVersion);

        if (TryGetStoredRunCommandResult(normalizedOrderId, commandName, normalizedKey, requestFingerprint, out var cachedResult, out var mismatchError))
            return string.IsNullOrWhiteSpace(mismatchError)
                ? cachedResult
                : StoreOperationResult.BadRequest(mismatchError);

        var executed = executeCore();
        var stored = TryStoreRunCommandResult(
            normalizedOrderId,
            commandName,
            normalizedKey,
            requestFingerprint,
            normalizedActor,
            executed,
            out var storeError);

        if (!string.IsNullOrWhiteSpace(storeError))
            return StoreOperationResult.BadRequest(storeError);

        return stored;
    }

    private StoreOperationResult TryStartRunCore(string orderId, RunOrderRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var db = _dbContextFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        var normalizedOrderId = orderId.Trim();
        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == normalizedOrderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (request.ExpectedOrderVersion > 0 && orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var lockRecord = db.OrderRunLocks.FirstOrDefault(x => x.OrderInternalId == normalizedOrderId);
        if (lockRecord != null && lockRecord.IsActive)
            return StoreOperationResult.Conflict(orderRecord.Version, "run already active");

        var leaseToken = Guid.NewGuid().ToString("N");
        if (lockRecord == null)
        {
            db.OrderRunLocks.Add(new OrderRunLockRecord
            {
                OrderInternalId = normalizedOrderId,
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

        var normalizedOrderId = orderId.Trim();
        var orderRecord = db.Orders.FirstOrDefault(x => x.InternalId == normalizedOrderId);
        if (orderRecord == null)
            return StoreOperationResult.NotFound();

        if (request.ExpectedOrderVersion > 0 && orderRecord.Version != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(orderRecord.Version, "order version mismatch");

        var lockRecord = db.OrderRunLocks.FirstOrDefault(x => x.OrderInternalId == normalizedOrderId);
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

    private bool TryGetStoredRunCommandResult(
        string orderInternalId,
        string commandName,
        string idempotencyKey,
        string requestFingerprint,
        out StoreOperationResult cachedResult,
        out string mismatchError)
    {
        cachedResult = StoreOperationResult.BadRequest(string.Empty);
        mismatchError = string.Empty;

        using var db = _dbContextFactory.CreateDbContext();
        var entry = db.OrderRunIdempotency
            .AsNoTracking()
            .FirstOrDefault(x =>
                x.OrderInternalId == orderInternalId
                && x.CommandName == commandName
                && x.IdempotencyKey == idempotencyKey);
        if (entry == null)
            return false;

        if (!string.Equals(entry.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            mismatchError = "idempotency key reuse with different request payload";
            return true;
        }

        cachedResult = DeserializeStoredRunCommandResult(entry);
        return true;
    }

    private StoreOperationResult TryStoreRunCommandResult(
        string orderInternalId,
        string commandName,
        string idempotencyKey,
        string requestFingerprint,
        string actor,
        StoreOperationResult result,
        out string storeError)
    {
        storeError = string.Empty;
        using var db = _dbContextFactory.CreateDbContext();

        var entry = new OrderRunIdempotencyRecord
        {
            OrderInternalId = orderInternalId,
            CommandName = commandName,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            Actor = actor ?? string.Empty,
            ResultKind = ToResultKind(result),
            Error = result.Error ?? string.Empty,
            CurrentVersion = result.CurrentVersion,
            ResponseOrderJson = result.Order == null
                ? "{}"
                : JsonSerializer.Serialize(result.Order),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        db.OrderRunIdempotency.Add(entry);
        try
        {
            db.SaveChanges();
            return result;
        }
        catch (DbUpdateException)
        {
            if (TryGetStoredRunCommandResult(orderInternalId, commandName, idempotencyKey, requestFingerprint, out var cached, out var mismatchError))
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

    private static StoreOperationResult DeserializeStoredRunCommandResult(OrderRunIdempotencyRecord entry)
    {
        var kind = entry.ResultKind?.Trim().ToLowerInvariant() ?? string.Empty;
        return kind switch
        {
            "success" => StoreOperationResult.Success(DeserializeStoredRunOrder(entry.ResponseOrderJson)),
            "not_found" => StoreOperationResult.NotFound(),
            "conflict" => StoreOperationResult.Conflict(entry.CurrentVersion, entry.Error),
            _ => StoreOperationResult.BadRequest(entry.Error)
        };
    }

    private static SharedOrder DeserializeStoredRunOrder(string payloadJson)
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

    private static string BuildRunRequestFingerprint(string commandName, string orderId, string actor, long expectedOrderVersion)
    {
        var source = $"{commandName}|{orderId}|{actor}|{expectedOrderVersion}";
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

    private static void UpsertUser(ReplicaDbContext db, string userName)
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
                IsActive = true,
                UpdatedAt = DateTime.Now
            });
            return;
        }

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
