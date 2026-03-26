using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Shared.Models;

namespace Replica.Api.Services;

public sealed class PostgreSqlLanOrderStore : ILanOrderStore
{
    private const string OrdersTable = "orders";
    private const string ItemsTable = "order_items";
    private const string EventsTable = "order_events";
    private const string UsersTable = "users";

    private readonly string _connectionString;

    private readonly List<SharedUser> _fallbackUsers = ReplicaApiBootstrapUsers.GetDefaultUsers().ToList();

    public PostgreSqlLanOrderStore(string connectionString)
    {
        _connectionString = connectionString ?? string.Empty;
    }

    public IReadOnlyList<SharedUser> GetUsers(bool includeInactive = false)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return _fallbackUsers.Select(CloneUser).ToList();

        try
        {
            using var connection = OpenConnection();
            EnsureSchema(connection);

            var users = new List<SharedUser>();
            var whereClause = includeInactive ? string.Empty : " where is_active = true";
            using var cmd = new NpgsqlCommand(
                $"select user_name, role, is_active, updated_at from {UsersTable}{whereClause} order by user_name asc;",
                connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var userName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var role = reader.IsDBNull(1) ? ReplicaApiRoles.Operator : reader.GetString(1);
                var isActive = !reader.IsDBNull(2) && reader.GetBoolean(2);
                var updatedAt = reader.IsDBNull(3) ? DateTime.Now : reader.GetDateTime(3);
                if (string.IsNullOrWhiteSpace(userName))
                    continue;

                users.Add(new SharedUser
                {
                    Id = BuildUserId(userName),
                    Name = userName,
                    Role = ReplicaApiRoles.Normalize(role),
                    IsActive = isActive,
                    UpdatedAt = updatedAt
                });
            }

            return users.Count > 0
                ? users
                : _fallbackUsers.Select(CloneUser).ToList();
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

        using var connection = OpenConnection();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();

        var existing = TryLoadUser(connection, tx, normalizedName);
        var nextIsActive = normalizedIsActive ?? existing?.IsActive ?? true;
        if (existing != null)
        {
            var otherActiveAdminsCount = CountOtherActiveAdmins(connection, tx, existing.Name);
            if (UserManagementRules.WouldRemoveLastActiveAdmin(
                existing.Role,
                existing.IsActive,
                normalizedRole,
                nextIsActive,
                otherActiveAdminsCount))
            {
                return UserOperationResult.BadRequest("at least one active admin is required");
            }
        }

        using var cmd = new NpgsqlCommand(
            $"""
            insert into {UsersTable}(user_name, role, is_active, updated_at)
            values (@user_name, @role, @is_active, now())
            on conflict (user_name) do update
            set role = @role,
                is_active = @is_active,
                updated_at = now();
            """,
            connection,
            tx);
        cmd.Parameters.AddWithValue("user_name", normalizedName);
        cmd.Parameters.AddWithValue("role", normalizedRole);
        cmd.Parameters.AddWithValue("is_active", nextIsActive);
        cmd.ExecuteNonQuery();

        tx.Commit();
        return UserOperationResult.Success(new SharedUser
        {
            Id = BuildUserId(normalizedName),
            Name = normalizedName,
            Role = normalizedRole,
            IsActive = nextIsActive,
            UpdatedAt = DateTime.Now
        });
    }

    public IReadOnlyList<SharedOrder> GetOrders(string createdBy)
    {
        using var connection = OpenConnection();
        EnsureSchema(connection);

        var orders = LoadOrders(connection, tx: null, onlyOrderId: null);
        if (string.IsNullOrWhiteSpace(createdBy))
            return orders;

        var filter = createdBy.Trim();
        return orders
            .Where(x =>
                string.Equals(x.CreatedById, filter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.CreatedByUser, filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public bool TryGetOrder(string orderId, out SharedOrder order)
    {
        order = new SharedOrder();
        if (string.IsNullOrWhiteSpace(orderId))
            return false;

        using var connection = OpenConnection();
        EnsureSchema(connection);

        var loaded = LoadOrderById(connection, tx: null, orderId.Trim());
        if (loaded == null)
            return false;

        order = loaded;
        return true;
    }

    public SharedOrder CreateOrder(CreateOrderRequest request, string actor)
    {
        using var connection = OpenConnection();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();

        var now = DateTime.Now;
        var managerOrderDate = request.ManagerOrderDate ?? DateTime.Today;
        var arrivalDate = request.ArrivalDate ?? now;
        var order = new SharedOrder
        {
            InternalId = Guid.NewGuid().ToString("N"),
            OrderNumber = request.OrderNumber?.Trim() ?? string.Empty,
            UserName = request.UserName?.Trim() ?? string.Empty,
            CreatedById = request.CreatedById?.Trim() ?? actor?.Trim() ?? string.Empty,
            CreatedByUser = request.CreatedByUser?.Trim() ?? actor?.Trim() ?? string.Empty,
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

        InsertOrder(connection, tx, order);

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

            InsertItem(connection, tx, order.InternalId, item);
            AppendEvent(connection, tx, order.InternalId, item.ItemId, "add-item", "api", actor ?? string.Empty, new { item_id = item.ItemId, sequence_no = item.SequenceNo, order_version = order.Version });
        }

        UpsertUser(connection, tx, order.CreatedByUser);
        UpsertUser(connection, tx, order.UserName);
        AppendEvent(connection, tx, order.InternalId, string.Empty, "add-order", "api", actor ?? string.Empty, new { order_id = order.InternalId, version = order.Version });

        tx.Commit();
        return CloneOrder(order);
    }

    public StoreOperationResult TryDeleteOrder(string orderId, DeleteOrderRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        request ??= new DeleteOrderRequest();

        using var connection = OpenConnection();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();

        if (!TryLoadOrderForUpdate(connection, tx, orderId.Trim(), out var order, out var currentVersion))
            return StoreOperationResult.NotFound();

        if (request.ExpectedVersion > 0 && currentVersion != request.ExpectedVersion)
            return StoreOperationResult.Conflict(currentVersion, "order version mismatch");

        var snapshot = LoadOrderById(connection, tx, order.InternalId) ?? order;

        AppendEvent(connection, tx, order.InternalId, string.Empty, "delete-order", "api", actor, new
        {
            order_id = order.InternalId,
            order_version = currentVersion
        });
        DeleteOrder(connection, tx, order.InternalId, currentVersion);

        tx.Commit();
        return StoreOperationResult.Success(snapshot);
    }

    public StoreOperationResult TryUpdateOrder(string orderId, UpdateOrderRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var connection = OpenConnection();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();

        if (!TryLoadOrderForUpdate(connection, tx, orderId.Trim(), out var order, out var currentVersion))
            return StoreOperationResult.NotFound();

        if (currentVersion != request.ExpectedVersion)
            return StoreOperationResult.Conflict(currentVersion, "order version mismatch");

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

        UpdateOrder(connection, tx, order, currentVersion);
        AppendEvent(connection, tx, order.InternalId, string.Empty, "update-order", "api", actor, new { order_id = order.InternalId, version = currentVersion + 1 });
        UpsertUser(connection, tx, order.UserName);

        var updated = LoadOrderById(connection, tx, order.InternalId) ?? order;
        tx.Commit();
        return StoreOperationResult.Success(updated);
    }

    public StoreOperationResult TryAddItem(string orderId, AddOrderItemRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var connection = OpenConnection();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();

        if (!TryLoadOrderForUpdate(connection, tx, orderId.Trim(), out var order, out var currentVersion))
            return StoreOperationResult.NotFound();

        if (currentVersion != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(currentVersion, "order version mismatch");

        var nextSequenceNo = GetItemCount(connection, tx, order.InternalId);
        var item = CloneItem(request.Item);
        item.ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? Guid.NewGuid().ToString("N") : item.ItemId.Trim();
        item.SequenceNo = nextSequenceNo;
        item.Version = 1;
        item.UpdatedAt = DateTime.Now;
        item.FileStatus = ReplicaApiWorkflowStatusNormalizer.NormalizeOrDefault(item.FileStatus);

        InsertItem(connection, tx, order.InternalId, item);
        UpdateOrder(connection, tx, order, currentVersion);

        AppendEvent(connection, tx, order.InternalId, item.ItemId, "add-item", "api", actor, new { item_id = item.ItemId, sequence_no = item.SequenceNo, version = currentVersion + 1 });

        var updated = LoadOrderById(connection, tx, order.InternalId);
        tx.Commit();

        return StoreOperationResult.Success(updated ?? order);
    }

    public StoreOperationResult TryUpdateItem(string orderId, string itemId, UpdateOrderItemRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(itemId))
            return StoreOperationResult.BadRequest("order id and item id are required");

        using var connection = OpenConnection();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();

        if (!TryLoadOrderForUpdate(connection, tx, orderId.Trim(), out var order, out var currentVersion))
            return StoreOperationResult.NotFound();

        if (currentVersion != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(currentVersion, "order version mismatch");

        if (!TryLoadItemForUpdate(connection, tx, order.InternalId, itemId.Trim(), out var item, out var itemVersion))
            return StoreOperationResult.NotFound();

        if (itemVersion != request.ExpectedItemVersion)
            return StoreOperationResult.Conflict(currentVersion, "item version mismatch");

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

        UpdateItem(connection, tx, order.InternalId, item, itemVersion);
        UpdateOrder(connection, tx, order, currentVersion);
        AppendEvent(connection, tx, order.InternalId, item.ItemId, "update-item", "api", actor, new { item_id = item.ItemId, item_version = itemVersion + 1, order_version = currentVersion + 1 });

        var updated = LoadOrderById(connection, tx, order.InternalId);
        tx.Commit();

        return StoreOperationResult.Success(updated ?? order);
    }

    public StoreOperationResult TryDeleteItem(string orderId, string itemId, DeleteOrderItemRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(itemId))
            return StoreOperationResult.BadRequest("order id and item id are required");

        using var connection = OpenConnection();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();

        if (!TryLoadOrderForUpdate(connection, tx, orderId.Trim(), out var order, out var currentVersion))
            return StoreOperationResult.NotFound();

        if (currentVersion != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(currentVersion, "order version mismatch");

        var normalizedItemId = itemId.Trim();
        if (!TryLoadItemForUpdate(connection, tx, order.InternalId, normalizedItemId, out _, out var itemVersion))
            return StoreOperationResult.NotFound();

        if (request.ExpectedItemVersion > 0 && itemVersion != request.ExpectedItemVersion)
            return StoreOperationResult.Conflict(currentVersion, "item version mismatch");

        DeleteItem(connection, tx, order.InternalId, normalizedItemId);

        var remainingItemIds = GetOrderItemIds(connection, tx, order.InternalId);
        for (var i = 0; i < remainingItemIds.Count; i++)
            UpdateItemSequence(connection, tx, order.InternalId, remainingItemIds[i], i);

        UpdateOrder(connection, tx, order, currentVersion);
        AppendEvent(connection, tx, order.InternalId, normalizedItemId, "delete-item", "api", actor, new { item_id = normalizedItemId, order_version = currentVersion + 1 });

        var updated = LoadOrderById(connection, tx, order.InternalId);
        tx.Commit();

        return StoreOperationResult.Success(updated ?? order);
    }

    public StoreOperationResult TryReorderItems(string orderId, ReorderOrderItemsRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return StoreOperationResult.BadRequest("order id is required");

        using var connection = OpenConnection();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();

        if (!TryLoadOrderForUpdate(connection, tx, orderId.Trim(), out var order, out var currentVersion))
            return StoreOperationResult.NotFound();

        if (currentVersion != request.ExpectedOrderVersion)
            return StoreOperationResult.Conflict(currentVersion, "order version mismatch");

        var dbItemIds = GetOrderItemIds(connection, tx, order.InternalId);
        var normalizedIds = (request.OrderedItemIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();

        if (normalizedIds.Count != dbItemIds.Count)
            return StoreOperationResult.BadRequest("ordered item ids count mismatch");

        var uniqueIds = new HashSet<string>(normalizedIds, StringComparer.Ordinal);
        if (uniqueIds.Count != normalizedIds.Count)
            return StoreOperationResult.BadRequest("ordered item ids must be unique");

        foreach (var id in normalizedIds)
        {
            if (!dbItemIds.Contains(id, StringComparer.Ordinal))
                return StoreOperationResult.BadRequest($"unknown item id: {id}");
        }

        for (var i = 0; i < normalizedIds.Count; i++)
            UpdateItemSequence(connection, tx, order.InternalId, normalizedIds[i], i);

        UpdateOrder(connection, tx, order, currentVersion);
        AppendEvent(connection, tx, order.InternalId, string.Empty, "topology", "api", actor, new { operation = "reorder-items", order_version = currentVersion + 1 });

        var updated = LoadOrderById(connection, tx, order.InternalId);
        tx.Commit();

        return StoreOperationResult.Success(updated ?? order);
    }

    public StoreOperationResult TryStartRun(string orderId, RunOrderRequest request, string actor)
    {
        return StoreOperationResult.BadRequest("deprecated store implementation: use EfCoreLanOrderStore");
    }

    public StoreOperationResult TryStopRun(string orderId, StopOrderRequest request, string actor)
    {
        return StoreOperationResult.BadRequest("deprecated store implementation: use EfCoreLanOrderStore");
    }

    private NpgsqlConnection OpenConnection()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("ReplicaDb connection string is empty");

        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void InsertOrder(NpgsqlConnection connection, NpgsqlTransaction tx, SharedOrder order)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            insert into {OrdersTable}
            (internal_id, order_number, user_name, status, arrival_date, order_date, start_mode, topology_marker, payload_json, version, updated_at)
            values
            (@internal_id, @order_number, @user_name, @status, @arrival_date, @order_date, @start_mode, @topology_marker, cast(@payload_json as jsonb), 1, now());
            """,
            connection,
            tx);

        cmd.Parameters.AddWithValue("internal_id", order.InternalId);
        cmd.Parameters.AddWithValue("order_number", order.OrderNumber ?? string.Empty);
        cmd.Parameters.AddWithValue("user_name", order.UserName ?? string.Empty);
        cmd.Parameters.AddWithValue("status", order.Status ?? string.Empty);
        cmd.Parameters.AddWithValue("arrival_date", order.ArrivalDate == default ? DateTime.Now : order.ArrivalDate);
        cmd.Parameters.AddWithValue("order_date", order.ManagerOrderDate == default ? DateTime.Today : order.ManagerOrderDate);
        cmd.Parameters.AddWithValue("start_mode", (int)order.StartMode);
        cmd.Parameters.AddWithValue("topology_marker", (int)order.TopologyMarker);
        cmd.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(order));
        cmd.ExecuteNonQuery();
    }

    private static void DeleteOrder(NpgsqlConnection connection, NpgsqlTransaction tx, string orderInternalId, long knownVersion)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            delete from {OrdersTable}
            where internal_id = @internal_id and version = @known_version;
            """,
            connection,
            tx);
        cmd.Parameters.AddWithValue("internal_id", orderInternalId);
        cmd.Parameters.AddWithValue("known_version", knownVersion);

        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("concurrency conflict: order version mismatch");
    }

    private static void UpdateOrder(NpgsqlConnection connection, NpgsqlTransaction tx, SharedOrder order, long knownVersion)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            update {OrdersTable}
            set
                order_number = @order_number,
                user_name = @user_name,
                status = @status,
                arrival_date = @arrival_date,
                order_date = @order_date,
                start_mode = @start_mode,
                topology_marker = @topology_marker,
                payload_json = cast(@payload_json as jsonb),
                version = version + 1,
                updated_at = now()
            where internal_id = @internal_id and version = @known_version;
            """,
            connection,
            tx);

        cmd.Parameters.AddWithValue("internal_id", order.InternalId);
        cmd.Parameters.AddWithValue("order_number", order.OrderNumber ?? string.Empty);
        cmd.Parameters.AddWithValue("user_name", order.UserName ?? string.Empty);
        cmd.Parameters.AddWithValue("status", order.Status ?? string.Empty);
        cmd.Parameters.AddWithValue("arrival_date", order.ArrivalDate == default ? DateTime.Now : order.ArrivalDate);
        cmd.Parameters.AddWithValue("order_date", order.ManagerOrderDate == default ? DateTime.Today : order.ManagerOrderDate);
        cmd.Parameters.AddWithValue("start_mode", (int)order.StartMode);
        cmd.Parameters.AddWithValue("topology_marker", (int)order.TopologyMarker);
        cmd.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(order));
        cmd.Parameters.AddWithValue("known_version", knownVersion);

        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("concurrency conflict: order version mismatch");
    }

    private static void InsertItem(NpgsqlConnection connection, NpgsqlTransaction tx, string orderInternalId, SharedOrderItem item)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            insert into {ItemsTable}
            (item_id, order_internal_id, sequence_no, payload_json, version, updated_at)
            values
            (@item_id, @order_internal_id, @sequence_no, cast(@payload_json as jsonb), 1, now());
            """,
            connection,
            tx);

        cmd.Parameters.AddWithValue("item_id", item.ItemId);
        cmd.Parameters.AddWithValue("order_internal_id", orderInternalId);
        cmd.Parameters.AddWithValue("sequence_no", item.SequenceNo);
        cmd.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(item));
        cmd.ExecuteNonQuery();
    }
    private static void UpdateItem(NpgsqlConnection connection, NpgsqlTransaction tx, string orderInternalId, SharedOrderItem item, long knownVersion)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            update {ItemsTable}
            set
                sequence_no = @sequence_no,
                payload_json = cast(@payload_json as jsonb),
                version = version + 1,
                updated_at = now()
            where item_id = @item_id and order_internal_id = @order_internal_id and version = @known_version;
            """,
            connection,
            tx);

        cmd.Parameters.AddWithValue("item_id", item.ItemId);
        cmd.Parameters.AddWithValue("order_internal_id", orderInternalId);
        cmd.Parameters.AddWithValue("sequence_no", item.SequenceNo);
        cmd.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(item));
        cmd.Parameters.AddWithValue("known_version", knownVersion);

        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("concurrency conflict: item version mismatch");
    }

    private static long GetItemCount(NpgsqlConnection connection, NpgsqlTransaction tx, string orderInternalId)
    {
        using var cmd = new NpgsqlCommand(
            $"select count(*) from {ItemsTable} where order_internal_id = @order_internal_id;",
            connection,
            tx);
        cmd.Parameters.AddWithValue("order_internal_id", orderInternalId);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    private static List<string> GetOrderItemIds(NpgsqlConnection connection, NpgsqlTransaction tx, string orderInternalId)
    {
        var ids = new List<string>();
        using var cmd = new NpgsqlCommand(
            $"select item_id from {ItemsTable} where order_internal_id = @order_internal_id order by sequence_no asc, item_id asc;",
            connection,
            tx);
        cmd.Parameters.AddWithValue("order_internal_id", orderInternalId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var itemId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(itemId))
                ids.Add(itemId);
        }

        return ids;
    }

    private static void UpdateItemSequence(NpgsqlConnection connection, NpgsqlTransaction tx, string orderInternalId, string itemId, int sequenceNo)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            update {ItemsTable}
            set sequence_no = @sequence_no,
                version = version + 1,
                updated_at = now()
            where order_internal_id = @order_internal_id and item_id = @item_id;
            """,
            connection,
            tx);
        cmd.Parameters.AddWithValue("sequence_no", sequenceNo);
        cmd.Parameters.AddWithValue("order_internal_id", orderInternalId);
        cmd.Parameters.AddWithValue("item_id", itemId);

        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"item not found: {itemId}");
    }

    private static void DeleteItem(NpgsqlConnection connection, NpgsqlTransaction tx, string orderInternalId, string itemId)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            delete from {ItemsTable}
            where order_internal_id = @order_internal_id and item_id = @item_id;
            """,
            connection,
            tx);

        cmd.Parameters.AddWithValue("order_internal_id", orderInternalId);
        cmd.Parameters.AddWithValue("item_id", itemId);

        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"item not found: {itemId}");
    }

    private static SharedUser? TryLoadUser(NpgsqlConnection connection, NpgsqlTransaction tx, string userName)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            select user_name, role, is_active, updated_at
            from {UsersTable}
            where user_name = @user_name
            limit 1;
            """,
            connection,
            tx);
        cmd.Parameters.AddWithValue("user_name", userName);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var loadedUserName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        var loadedRole = reader.IsDBNull(1) ? ReplicaApiRoles.Operator : reader.GetString(1);
        var loadedIsActive = !reader.IsDBNull(2) && reader.GetBoolean(2);
        var loadedUpdatedAt = reader.IsDBNull(3) ? DateTime.Now : reader.GetDateTime(3);

        return new SharedUser
        {
            Id = BuildUserId(loadedUserName),
            Name = loadedUserName,
            Role = ReplicaApiRoles.Normalize(loadedRole),
            IsActive = loadedIsActive,
            UpdatedAt = loadedUpdatedAt
        };
    }

    private static int CountOtherActiveAdmins(NpgsqlConnection connection, NpgsqlTransaction tx, string excludedUserName)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            select count(*)
            from {UsersTable}
            where user_name <> @excluded_user_name
              and is_active = true
              and role = @admin_role;
            """,
            connection,
            tx);
        cmd.Parameters.AddWithValue("excluded_user_name", excludedUserName);
        cmd.Parameters.AddWithValue("admin_role", ReplicaApiRoles.Admin);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static void UpsertUser(NpgsqlConnection connection, NpgsqlTransaction tx, string userName, string role = ReplicaApiRoles.Operator)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return;

        using var cmd = new NpgsqlCommand(
            $"""
            insert into {UsersTable}(user_name, role, is_active, updated_at)
            values (@user_name, @role, true, now())
            on conflict (user_name) do update
            set role = case
                    when coalesce({UsersTable}.role, '') = '' then excluded.role
                    else {UsersTable}.role
                end,
                is_active = true,
                updated_at = now();
            """,
            connection,
            tx);
        cmd.Parameters.AddWithValue("user_name", userName.Trim());
        cmd.Parameters.AddWithValue("role", ReplicaApiRoles.Normalize(role));
        cmd.ExecuteNonQuery();
    }

    private static void AppendEvent(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        string orderInternalId,
        string itemId,
        string eventType,
        string eventSource,
        string actor,
        object payload)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            insert into {EventsTable}
            (order_internal_id, item_id, event_type, event_source, payload_json, created_at)
            values
            (@order_internal_id, @item_id, @event_type, @event_source, cast(@payload_json as jsonb), now());
            """,
            connection,
            tx);

        cmd.Parameters.AddWithValue("order_internal_id", orderInternalId ?? string.Empty);
        cmd.Parameters.AddWithValue("item_id", itemId ?? string.Empty);
        cmd.Parameters.AddWithValue("event_type", eventType ?? string.Empty);
        cmd.Parameters.AddWithValue("event_source", eventSource ?? string.Empty);
        cmd.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(new
        {
            actor = actor ?? string.Empty,
            payload
        }));
        cmd.ExecuteNonQuery();
    }

    private static bool TryLoadOrderForUpdate(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        string orderId,
        out SharedOrder order,
        out long currentVersion)
    {
        order = new SharedOrder();
        currentVersion = 0;

        using var cmd = new NpgsqlCommand(
            $"select version, payload_json::text from {OrdersTable} where internal_id = @internal_id for update;",
            connection,
            tx);
        cmd.Parameters.AddWithValue("internal_id", orderId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return false;

        currentVersion = reader.IsDBNull(0) ? 0L : reader.GetInt64(0);
        var payloadJson = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        order = DeserializeOrder(payloadJson, orderId, currentVersion);
        return true;
    }

    private static bool TryLoadItemForUpdate(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        string orderId,
        string itemId,
        out SharedOrderItem item,
        out long currentVersion)
    {
        item = new SharedOrderItem();
        currentVersion = 0;

        using var cmd = new NpgsqlCommand(
            $"select version, payload_json::text from {ItemsTable} where order_internal_id = @order_internal_id and item_id = @item_id for update;",
            connection,
            tx);
        cmd.Parameters.AddWithValue("order_internal_id", orderId);
        cmd.Parameters.AddWithValue("item_id", itemId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return false;

        currentVersion = reader.IsDBNull(0) ? 0L : reader.GetInt64(0);
        var payloadJson = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        item = DeserializeItem(payloadJson, itemId, currentVersion);
        return true;
    }

    private static SharedOrder? LoadOrderById(NpgsqlConnection connection, NpgsqlTransaction? tx, string orderId)
    {
        return LoadOrders(connection, tx, orderId).FirstOrDefault();
    }

    private static List<SharedOrder> LoadOrders(NpgsqlConnection connection, NpgsqlTransaction? tx, string? onlyOrderId)
    {
        var ordersById = new Dictionary<string, SharedOrder>(StringComparer.Ordinal);

        var orderSql = string.IsNullOrWhiteSpace(onlyOrderId)
            ? $"select internal_id, version, payload_json::text from {OrdersTable} order by order_date desc, arrival_date desc, internal_id asc;"
            : $"select internal_id, version, payload_json::text from {OrdersTable} where internal_id = @internal_id order by internal_id asc;";

        using (var cmd = new NpgsqlCommand(orderSql, connection, tx))
        {
            if (!string.IsNullOrWhiteSpace(onlyOrderId))
                cmd.Parameters.AddWithValue("internal_id", onlyOrderId.Trim());

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var internalId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var version = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
                var payloadJson = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                if (string.IsNullOrWhiteSpace(internalId))
                    continue;

                var order = DeserializeOrder(payloadJson, internalId, version);
                order.Items = new List<SharedOrderItem>();
                ordersById[internalId] = order;
            }
        }

        if (ordersById.Count == 0)
            return new List<SharedOrder>();

        var orderIds = ordersById.Keys.ToList();
        var itemSql = string.IsNullOrWhiteSpace(onlyOrderId)
            ? $"select order_internal_id, item_id, version, payload_json::text from {ItemsTable} where order_internal_id = any(@order_ids) order by order_internal_id asc, sequence_no asc, item_id asc;"
            : $"select order_internal_id, item_id, version, payload_json::text from {ItemsTable} where order_internal_id = @order_internal_id order by sequence_no asc, item_id asc;";

        using (var cmd = new NpgsqlCommand(itemSql, connection, tx))
        {
            if (string.IsNullOrWhiteSpace(onlyOrderId))
                cmd.Parameters.AddWithValue("order_ids", orderIds);
            else
                cmd.Parameters.AddWithValue("order_internal_id", onlyOrderId.Trim());

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var orderInternalId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var itemId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var version = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
                var payloadJson = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                if (string.IsNullOrWhiteSpace(orderInternalId) || string.IsNullOrWhiteSpace(itemId))
                    continue;

                if (!ordersById.TryGetValue(orderInternalId, out var order))
                    continue;

                var item = DeserializeItem(payloadJson, itemId, version);
                order.Items.Add(item);
            }
        }

        return ordersById.Values.ToList();
    }
    private static SharedOrder DeserializeOrder(string payloadJson, string internalId, long version)
    {
        SharedOrder? order = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                order = JsonSerializer.Deserialize<SharedOrder>(payloadJson);
            }
            catch
            {
                order = null;
            }
        }

        order ??= new SharedOrder();
        order.InternalId = string.IsNullOrWhiteSpace(order.InternalId) ? internalId : order.InternalId;
        order.Version = version;
        order.OrderNumber ??= string.Empty;
        order.UserName ??= string.Empty;
        order.CreatedById ??= string.Empty;
        order.CreatedByUser ??= string.Empty;
        order.Status ??= string.Empty;
        order.Keyword ??= string.Empty;
        order.FolderName ??= string.Empty;
        order.PitStopAction ??= "-";
        order.ImposingAction ??= "-";
        order.Items ??= new List<SharedOrderItem>();
        return order;
    }

    private static SharedOrderItem DeserializeItem(string payloadJson, string itemId, long version)
    {
        SharedOrderItem? item = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                item = JsonSerializer.Deserialize<SharedOrderItem>(payloadJson);
            }
            catch
            {
                item = null;
            }
        }

        item ??= new SharedOrderItem();
        item.ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? itemId : item.ItemId;
        item.Version = version;
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

    private static void EnsureSchema(NpgsqlConnection connection)
    {
        using var cmd = new NpgsqlCommand(
            $"""
            create table if not exists {OrdersTable}
            (
                internal_id text primary key,
                order_number text not null default '',
                user_name text not null default '',
                status text not null default '',
                arrival_date timestamp without time zone not null default now(),
                order_date timestamp without time zone not null default now(),
                start_mode integer not null default 0,
                topology_marker integer not null default 0,
                payload_json jsonb not null,
                version bigint not null default 1,
                updated_at timestamp without time zone not null default now()
            );
            create table if not exists {ItemsTable}
            (
                item_id text primary key,
                order_internal_id text not null references {OrdersTable}(internal_id) on delete cascade,
                sequence_no bigint not null default 0,
                payload_json jsonb not null,
                version bigint not null default 1,
                updated_at timestamp without time zone not null default now(),
                constraint uq_order_items_sequence unique (order_internal_id, sequence_no),
                constraint ck_order_items_sequence_non_negative check (sequence_no >= 0)
            );
            create table if not exists {EventsTable}
            (
                event_id bigserial primary key,
                order_internal_id text,
                item_id text,
                event_type text not null,
                event_source text not null,
                payload_json jsonb not null default jsonb_build_object(),
                created_at timestamp without time zone not null default now()
            );
            create table if not exists {UsersTable}
            (
                user_name text primary key,
                role text not null default 'Operator',
                is_active boolean not null default true,
                updated_at timestamp without time zone not null default now()
            );
            alter table if exists {UsersTable} add column if not exists role text not null default 'Operator';
            update {UsersTable} set role = 'Operator' where role is null or btrim(role) = '';
            create index if not exists ix_orders_order_number on {OrdersTable}(order_number);
            create index if not exists ix_orders_arrival_date on {OrdersTable}(arrival_date);
            create index if not exists ix_order_items_order_internal_id on {ItemsTable}(order_internal_id);
            create index if not exists ix_order_events_order_internal_id on {EventsTable}(order_internal_id);
            """,
            connection);
        cmd.ExecuteNonQuery();
    }
}


