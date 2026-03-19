using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Npgsql;

namespace Replica
{
    public sealed class PostgreSqlOrdersRepository : IOrdersRepository
    {
        private const string OrdersTable = "orders";
        private const string ItemsTable = "order_items";
        private const string EventsTable = "order_events";
        private const string UsersTable = "users";

        private readonly string _connectionString;
        private readonly object _snapshotSync = new();
        private bool _snapshotInitialized;
        private Dictionary<string, long> _knownOrderVersions = new(StringComparer.Ordinal);
        private Dictionary<string, Dictionary<string, long>> _knownItemVersionsByOrder = new(StringComparer.Ordinal);

        public PostgreSqlOrdersRepository(string connectionString)
        {
            _connectionString = connectionString ?? string.Empty;
        }

        public string BackendName => "postgresql";

        public bool TryLoadAll(out List<OrderData> orders, out string error)
        {
            orders = new List<OrderData>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                error = "connection string is empty";
                return false;
            }

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                connection.Open();
                EnsureSchema(connection);

                var byId = new Dictionary<string, OrderData>(StringComparer.Ordinal);
                var orderVersions = new Dictionary<string, long>(StringComparer.Ordinal);
                var itemVersionsByOrder = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);

                using (var cmd = new NpgsqlCommand(
                    $"select internal_id, version, payload_json::text from {OrdersTable} order by order_date desc, arrival_date desc, internal_id asc;",
                    connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var internalId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                        var storageVersion = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
                        var payload = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        if (string.IsNullOrWhiteSpace(internalId) || string.IsNullOrWhiteSpace(payload))
                            continue;

                        var order = JsonSerializer.Deserialize<OrderData>(payload) ?? new OrderData();
                        order.InternalId = string.IsNullOrWhiteSpace(order.InternalId) ? internalId : order.InternalId;
                        order.StorageVersion = storageVersion;
                        order.Items = new List<OrderFileItem>();
                        byId[order.InternalId] = order;
                        orderVersions[order.InternalId] = storageVersion;
                    }
                }

                using (var cmd = new NpgsqlCommand(
                    $"select order_internal_id, item_id, version, payload_json::text from {ItemsTable} order by order_internal_id asc, sequence_no asc, item_id asc;",
                    connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var orderInternalId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                        var itemId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        var storageVersion = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
                        var payload = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                        if (string.IsNullOrWhiteSpace(orderInternalId)
                            || string.IsNullOrWhiteSpace(itemId)
                            || string.IsNullOrWhiteSpace(payload))
                        {
                            continue;
                        }

                        if (!byId.TryGetValue(orderInternalId, out var order))
                            continue;

                        var item = JsonSerializer.Deserialize<OrderFileItem>(payload) ?? new OrderFileItem();
                        item.ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? itemId : item.ItemId;
                        item.StorageVersion = storageVersion;
                        order.Items.Add(item);

                        if (!itemVersionsByOrder.TryGetValue(orderInternalId, out var itemVersions))
                        {
                            itemVersions = new Dictionary<string, long>(StringComparer.Ordinal);
                            itemVersionsByOrder[orderInternalId] = itemVersions;
                        }

                        itemVersions[item.ItemId] = storageVersion;
                    }
                }

                orders = byId.Values.ToList();
                SetSnapshot(orderVersions, itemVersionsByOrder, initialized: true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                orders = new List<OrderData>();
                return false;
            }
        }

        public bool TrySaveAll(IReadOnlyCollection<OrderData> orders, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                error = "connection string is empty";
                return false;
            }

            try
            {
                var normalizedOrders = NormalizeOrders(orders);

                using var connection = new NpgsqlConnection(_connectionString);
                connection.Open();
                EnsureSchema(connection);
                using var tx = connection.BeginTransaction();

                var currentOrderVersions = LoadCurrentOrderVersions(connection, tx);
                var currentItemVersionsByOrder = LoadCurrentItemVersions(connection, tx);
                var snapshot = CaptureSnapshot();

                if (!snapshot.Initialized)
                {
                    if (currentOrderVersions.Count > 0)
                    {
                        error = "concurrency conflict: LAN snapshot is not initialized; reload history before saving to avoid overwriting existing PostgreSQL data";
                        return false;
                    }

                    snapshot = new RepositorySnapshot(
                        initialized: true,
                        orderVersions: new Dictionary<string, long>(StringComparer.Ordinal),
                        itemVersionsByOrder: new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal));
                }

                if (TryDetectExternalChanges(snapshot, currentOrderVersions, currentItemVersionsByOrder, out var conflictReason))
                {
                    error = conflictReason;
                    return false;
                }

                var events = new List<OrderEventEntry>();
                var incomingOrderIds = new HashSet<string>(StringComparer.Ordinal);

                foreach (var order in normalizedOrders)
                {
                    incomingOrderIds.Add(order.InternalId);

                    if (snapshot.OrderVersions.TryGetValue(order.InternalId, out var knownOrderVersion))
                    {
                        order.StorageVersion = UpdateOrder(connection, tx, order, knownOrderVersion);
                        events.Add(BuildOrderEvent(order.InternalId, string.Empty, "update-order", order.StorageVersion));
                    }
                    else
                    {
                        order.StorageVersion = InsertOrder(connection, tx, order);
                        events.Add(BuildOrderEvent(order.InternalId, string.Empty, "add-order", order.StorageVersion));
                    }

                    var knownItems = snapshot.ItemVersionsByOrder.TryGetValue(order.InternalId, out var knownItemsById)
                        ? knownItemsById
                        : new Dictionary<string, long>(StringComparer.Ordinal);
                    var incomingItemIds = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var item in order.Items)
                    {
                        incomingItemIds.Add(item.ItemId);
                        if (knownItems.TryGetValue(item.ItemId, out var knownItemVersion))
                        {
                            item.StorageVersion = UpdateItem(connection, tx, order.InternalId, item, knownItemVersion);
                            events.Add(BuildItemEvent(order.InternalId, item.ItemId, "update-item", item.StorageVersion));
                        }
                        else
                        {
                            item.StorageVersion = InsertItem(connection, tx, order.InternalId, item);
                            events.Add(BuildItemEvent(order.InternalId, item.ItemId, "add-item", item.StorageVersion));
                        }
                    }

                    foreach (var knownItem in knownItems)
                    {
                        if (incomingItemIds.Contains(knownItem.Key))
                            continue;

                        DeleteItem(connection, tx, order.InternalId, knownItem.Key, knownItem.Value);
                        events.Add(BuildItemEvent(order.InternalId, knownItem.Key, "remove-item", knownItem.Value + 1));
                    }
                }

                foreach (var knownOrder in snapshot.OrderVersions)
                {
                    if (incomingOrderIds.Contains(knownOrder.Key))
                        continue;

                    DeleteOrder(connection, tx, knownOrder.Key, knownOrder.Value);
                    events.Add(BuildOrderEvent(knownOrder.Key, string.Empty, "delete-order", knownOrder.Value + 1));
                }

                SyncUsers(connection, tx, normalizedOrders);
                InsertEvents(connection, tx, events);

                tx.Commit();

                var nextSnapshot = BuildSnapshot(normalizedOrders);
                SetSnapshot(nextSnapshot.OrderVersions, nextSnapshot.ItemVersionsByOrder, initialized: true);
                return true;
            }
            catch (ConcurrencyConflictException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool TryAppendEvent(
            string orderInternalId,
            string itemId,
            string eventType,
            string eventSource,
            string payloadJson,
            out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                error = "connection string is empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(eventType))
            {
                error = "event type is empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(eventSource))
            {
                error = "event source is empty";
                return false;
            }

            var normalizedPayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson.Trim();

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                connection.Open();
                EnsureSchema(connection);

                using var cmd = new NpgsqlCommand(
                    $"""
                    insert into {EventsTable}
                    (order_internal_id, item_id, event_type, event_source, payload_json, created_at)
                    values
                    (@order_internal_id, @item_id, @event_type, @event_source, cast(@payload_json as jsonb), now());
                    """,
                    connection);
                cmd.Parameters.AddWithValue("order_internal_id", orderInternalId ?? string.Empty);
                cmd.Parameters.AddWithValue("item_id", itemId ?? string.Empty);
                cmd.Parameters.AddWithValue("event_type", eventType.Trim());
                cmd.Parameters.AddWithValue("event_source", eventSource.Trim());
                cmd.Parameters.AddWithValue("payload_json", normalizedPayloadJson);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static List<OrderData> NormalizeOrders(IReadOnlyCollection<OrderData> orders)
        {
            var normalizedOrders = new List<OrderData>();
            foreach (var order in (orders ?? Array.Empty<OrderData>()).Where(x => x != null))
            {
                order.InternalId = string.IsNullOrWhiteSpace(order.InternalId) ? Guid.NewGuid().ToString("N") : order.InternalId;
                order.OrderDate = order.OrderDate == default ? OrderData.PlaceholderOrderDate : order.OrderDate;
                order.ArrivalDate = order.ArrivalDate == default ? DateTime.Now : order.ArrivalDate;
                order.Items ??= new List<OrderFileItem>();

                var normalizedItems = order.Items
                    .Where(item => item != null)
                    .OrderBy(item => item.SequenceNo)
                    .ThenBy(item => item.ItemId ?? string.Empty, StringComparer.Ordinal)
                    .ToList();

                for (var i = 0; i < normalizedItems.Count; i++)
                {
                    var item = normalizedItems[i];
                    item.ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? Guid.NewGuid().ToString("N") : item.ItemId;
                    item.SequenceNo = i;
                    item.UpdatedAt = item.UpdatedAt == default ? DateTime.Now : item.UpdatedAt;
                }

                order.Items = normalizedItems;
                normalizedOrders.Add(order);
            }

            return normalizedOrders;
        }

        private static Dictionary<string, long> LoadCurrentOrderVersions(NpgsqlConnection connection, NpgsqlTransaction tx)
        {
            var versions = new Dictionary<string, long>(StringComparer.Ordinal);
            using var cmd = new NpgsqlCommand($"select internal_id, version from {OrdersTable};", connection, tx);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var internalId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var version = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
                if (string.IsNullOrWhiteSpace(internalId))
                    continue;

                versions[internalId] = version;
            }

            return versions;
        }

        private static Dictionary<string, Dictionary<string, long>> LoadCurrentItemVersions(NpgsqlConnection connection, NpgsqlTransaction tx)
        {
            var versions = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
            using var cmd = new NpgsqlCommand($"select order_internal_id, item_id, version from {ItemsTable};", connection, tx);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var orderInternalId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var itemId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var version = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
                if (string.IsNullOrWhiteSpace(orderInternalId) || string.IsNullOrWhiteSpace(itemId))
                    continue;

                if (!versions.TryGetValue(orderInternalId, out var itemVersions))
                {
                    itemVersions = new Dictionary<string, long>(StringComparer.Ordinal);
                    versions[orderInternalId] = itemVersions;
                }

                itemVersions[itemId] = version;
            }

            return versions;
        }

        private static bool TryDetectExternalChanges(
            RepositorySnapshot snapshot,
            Dictionary<string, long> currentOrderVersions,
            Dictionary<string, Dictionary<string, long>> currentItemVersionsByOrder,
            out string conflictReason)
        {
            conflictReason = string.Empty;
            if (!snapshot.Initialized)
                return false;

            foreach (var knownOrder in snapshot.OrderVersions)
            {
                if (!currentOrderVersions.TryGetValue(knownOrder.Key, out var currentOrderVersion))
                {
                    conflictReason = $"concurrency conflict: order '{knownOrder.Key}' was removed by another client";
                    return true;
                }

                if (currentOrderVersion != knownOrder.Value)
                {
                    conflictReason = $"concurrency conflict: order '{knownOrder.Key}' version changed ({knownOrder.Value} -> {currentOrderVersion})";
                    return true;
                }
            }

            foreach (var currentOrder in currentOrderVersions)
            {
                if (!snapshot.OrderVersions.ContainsKey(currentOrder.Key))
                {
                    conflictReason = $"concurrency conflict: order '{currentOrder.Key}' was added by another client";
                    return true;
                }
            }

            foreach (var knownItemsByOrder in snapshot.ItemVersionsByOrder)
            {
                var currentItems = currentItemVersionsByOrder.TryGetValue(knownItemsByOrder.Key, out var knownCurrentItems)
                    ? knownCurrentItems
                    : new Dictionary<string, long>(StringComparer.Ordinal);

                foreach (var knownItem in knownItemsByOrder.Value)
                {
                    if (!currentItems.TryGetValue(knownItem.Key, out var currentItemVersion))
                    {
                        conflictReason = $"concurrency conflict: item '{knownItem.Key}' in order '{knownItemsByOrder.Key}' was removed by another client";
                        return true;
                    }

                    if (currentItemVersion != knownItem.Value)
                    {
                        conflictReason = $"concurrency conflict: item '{knownItem.Key}' in order '{knownItemsByOrder.Key}' version changed ({knownItem.Value} -> {currentItemVersion})";
                        return true;
                    }
                }

                foreach (var currentItem in currentItems)
                {
                    if (!knownItemsByOrder.Value.ContainsKey(currentItem.Key))
                    {
                        conflictReason = $"concurrency conflict: item '{currentItem.Key}' in order '{knownItemsByOrder.Key}' was added by another client";
                        return true;
                    }
                }
            }

            return false;
        }

        private static long InsertOrder(NpgsqlConnection connection, NpgsqlTransaction tx, OrderData order)
        {
            using var cmd = new NpgsqlCommand(
                $"""
                insert into {OrdersTable}
                (internal_id, order_number, user_name, status, arrival_date, order_date, start_mode, topology_marker, payload_json, version, updated_at)
                values
                (@internal_id, @order_number, @user_name, @status, @arrival_date, @order_date, @start_mode, @topology_marker, cast(@payload_json as jsonb), 1, now())
                returning version;
                """,
                connection, tx);
            cmd.Parameters.AddWithValue("internal_id", order.InternalId);
            cmd.Parameters.AddWithValue("order_number", order.Id ?? string.Empty);
            cmd.Parameters.AddWithValue("user_name", order.UserName ?? string.Empty);
            cmd.Parameters.AddWithValue("status", order.Status ?? string.Empty);
            cmd.Parameters.AddWithValue("arrival_date", order.ArrivalDate);
            cmd.Parameters.AddWithValue("order_date", order.OrderDate);
            cmd.Parameters.AddWithValue("start_mode", (int)order.StartMode);
            cmd.Parameters.AddWithValue("topology_marker", (int)order.FileTopologyMarker);
            cmd.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(order));

            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? 1L : Convert.ToInt64(result);
        }

        private static long UpdateOrder(NpgsqlConnection connection, NpgsqlTransaction tx, OrderData order, long knownVersion)
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
                where internal_id = @internal_id and version = @known_version
                returning version;
                """,
                connection, tx);
            cmd.Parameters.AddWithValue("internal_id", order.InternalId);
            cmd.Parameters.AddWithValue("order_number", order.Id ?? string.Empty);
            cmd.Parameters.AddWithValue("user_name", order.UserName ?? string.Empty);
            cmd.Parameters.AddWithValue("status", order.Status ?? string.Empty);
            cmd.Parameters.AddWithValue("arrival_date", order.ArrivalDate);
            cmd.Parameters.AddWithValue("order_date", order.OrderDate);
            cmd.Parameters.AddWithValue("start_mode", (int)order.StartMode);
            cmd.Parameters.AddWithValue("topology_marker", (int)order.FileTopologyMarker);
            cmd.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(order));
            cmd.Parameters.AddWithValue("known_version", knownVersion);

            var result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value)
                throw new ConcurrencyConflictException($"concurrency conflict: order '{order.InternalId}' was modified by another client");

            return Convert.ToInt64(result);
        }

        private static void DeleteOrder(NpgsqlConnection connection, NpgsqlTransaction tx, string orderInternalId, long knownVersion)
        {
            using var cmd = new NpgsqlCommand(
                $"delete from {OrdersTable} where internal_id = @internal_id and version = @known_version;",
                connection, tx);
            cmd.Parameters.AddWithValue("internal_id", orderInternalId);
            cmd.Parameters.AddWithValue("known_version", knownVersion);

            if (cmd.ExecuteNonQuery() != 1)
                throw new ConcurrencyConflictException($"concurrency conflict: order '{orderInternalId}' delete version check failed");
        }

        private static long InsertItem(NpgsqlConnection connection, NpgsqlTransaction tx, string orderInternalId, OrderFileItem item)
        {
            using var cmd = new NpgsqlCommand(
                $"""
                insert into {ItemsTable}
                (item_id, order_internal_id, sequence_no, payload_json, version, updated_at)
                values
                (@item_id, @order_internal_id, @sequence_no, cast(@payload_json as jsonb), 1, now())
                returning version;
                """,
                connection, tx);
            cmd.Parameters.AddWithValue("item_id", item.ItemId);
            cmd.Parameters.AddWithValue("order_internal_id", orderInternalId);
            cmd.Parameters.AddWithValue("sequence_no", item.SequenceNo);
            cmd.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(item));

            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? 1L : Convert.ToInt64(result);
        }

        private static long UpdateItem(NpgsqlConnection connection, NpgsqlTransaction tx, string orderInternalId, OrderFileItem item, long knownVersion)
        {
            using var cmd = new NpgsqlCommand(
                $"""
                update {ItemsTable}
                set
                    sequence_no = @sequence_no,
                    payload_json = cast(@payload_json as jsonb),
                    version = version + 1,
                    updated_at = now()
                where item_id = @item_id and order_internal_id = @order_internal_id and version = @known_version
                returning version;
                """,
                connection, tx);
            cmd.Parameters.AddWithValue("item_id", item.ItemId);
            cmd.Parameters.AddWithValue("order_internal_id", orderInternalId);
            cmd.Parameters.AddWithValue("sequence_no", item.SequenceNo);
            cmd.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(item));
            cmd.Parameters.AddWithValue("known_version", knownVersion);

            var result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value)
            {
                throw new ConcurrencyConflictException(
                    $"concurrency conflict: item '{item.ItemId}' in order '{orderInternalId}' was modified by another client");
            }

            return Convert.ToInt64(result);
        }

        private static void DeleteItem(
            NpgsqlConnection connection,
            NpgsqlTransaction tx,
            string orderInternalId,
            string itemId,
            long knownVersion)
        {
            using var cmd = new NpgsqlCommand(
                $"delete from {ItemsTable} where item_id = @item_id and order_internal_id = @order_internal_id and version = @known_version;",
                connection, tx);
            cmd.Parameters.AddWithValue("item_id", itemId);
            cmd.Parameters.AddWithValue("order_internal_id", orderInternalId);
            cmd.Parameters.AddWithValue("known_version", knownVersion);

            if (cmd.ExecuteNonQuery() != 1)
            {
                throw new ConcurrencyConflictException(
                    $"concurrency conflict: item '{itemId}' in order '{orderInternalId}' delete version check failed");
            }
        }

        private static void SyncUsers(NpgsqlConnection connection, NpgsqlTransaction tx, List<OrderData> orders)
        {
            using (var deactivateCmd = new NpgsqlCommand(
                $"update {UsersTable} set is_active = false, updated_at = now() where is_active = true;",
                connection, tx))
            {
                deactivateCmd.ExecuteNonQuery();
            }

            var activeUsers = orders
                .Where(order => !string.IsNullOrWhiteSpace(order.UserName))
                .Select(order => order.UserName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var userName in activeUsers)
            {
                using var upsertCmd = new NpgsqlCommand(
                    $"""
                    insert into {UsersTable}(user_name, is_active, updated_at)
                    values (@user_name, true, now())
                    on conflict (user_name) do update
                    set is_active = true, updated_at = now();
                    """,
                    connection, tx);
                upsertCmd.Parameters.AddWithValue("user_name", userName);
                upsertCmd.ExecuteNonQuery();
            }
        }

        private static OrderEventEntry BuildOrderEvent(string orderInternalId, string itemId, string eventType, long storageVersion)
        {
            var payload = JsonSerializer.Serialize(new
            {
                order_internal_id = orderInternalId,
                event_type = eventType,
                version = storageVersion
            });
            return new OrderEventEntry(orderInternalId, itemId, eventType, "ui", payload);
        }

        private static OrderEventEntry BuildItemEvent(string orderInternalId, string itemId, string eventType, long storageVersion)
        {
            var payload = JsonSerializer.Serialize(new
            {
                order_internal_id = orderInternalId,
                item_id = itemId,
                event_type = eventType,
                version = storageVersion
            });
            return new OrderEventEntry(orderInternalId, itemId, eventType, "ui", payload);
        }

        private static void InsertEvents(NpgsqlConnection connection, NpgsqlTransaction tx, List<OrderEventEntry> events)
        {
            foreach (var entry in events)
            {
                using var cmd = new NpgsqlCommand(
                    $"""
                    insert into {EventsTable}
                    (order_internal_id, item_id, event_type, event_source, payload_json, created_at)
                    values
                    (@order_internal_id, @item_id, @event_type, @event_source, cast(@payload_json as jsonb), now());
                    """,
                    connection, tx);
                cmd.Parameters.AddWithValue("order_internal_id", entry.OrderInternalId);
                cmd.Parameters.AddWithValue("item_id", entry.ItemId);
                cmd.Parameters.AddWithValue("event_type", entry.EventType);
                cmd.Parameters.AddWithValue("event_source", entry.EventSource);
                cmd.Parameters.AddWithValue("payload_json", entry.PayloadJson);
                cmd.ExecuteNonQuery();
            }
        }

        private RepositorySnapshot CaptureSnapshot()
        {
            lock (_snapshotSync)
            {
                return new RepositorySnapshot(
                    initialized: _snapshotInitialized,
                    orderVersions: Clone(_knownOrderVersions),
                    itemVersionsByOrder: Clone(_knownItemVersionsByOrder));
            }
        }

        private void SetSnapshot(
            Dictionary<string, long> orderVersions,
            Dictionary<string, Dictionary<string, long>> itemVersionsByOrder,
            bool initialized)
        {
            lock (_snapshotSync)
            {
                _snapshotInitialized = initialized;
                _knownOrderVersions = Clone(orderVersions);
                _knownItemVersionsByOrder = Clone(itemVersionsByOrder);
            }
        }

        private static RepositorySnapshot BuildSnapshot(List<OrderData> orders)
        {
            var orderVersions = new Dictionary<string, long>(StringComparer.Ordinal);
            var itemVersionsByOrder = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);

            foreach (var order in orders)
            {
                orderVersions[order.InternalId] = order.StorageVersion;
                var itemsById = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var item in order.Items)
                    itemsById[item.ItemId] = item.StorageVersion;
                itemVersionsByOrder[order.InternalId] = itemsById;
            }

            return new RepositorySnapshot(
                initialized: true,
                orderVersions: orderVersions,
                itemVersionsByOrder: itemVersionsByOrder);
        }

        private static Dictionary<string, long> Clone(Dictionary<string, long> source)
        {
            return source.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        }

        private static Dictionary<string, Dictionary<string, long>> Clone(Dictionary<string, Dictionary<string, long>> source)
        {
            var clone = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
            foreach (var pair in source)
                clone[pair.Key] = pair.Value.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            return clone;
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
                    is_active boolean not null default true,
                    updated_at timestamp without time zone not null default now()
                );
                create index if not exists ix_orders_order_number on {OrdersTable}(order_number);
                create index if not exists ix_orders_arrival_date on {OrdersTable}(arrival_date);
                create index if not exists ix_order_items_order_internal_id on {ItemsTable}(order_internal_id);
                create index if not exists ix_order_events_order_internal_id on {EventsTable}(order_internal_id);
                """,
                connection);
            cmd.ExecuteNonQuery();
        }

        private sealed class RepositorySnapshot
        {
            public RepositorySnapshot(
                bool initialized,
                Dictionary<string, long> orderVersions,
                Dictionary<string, Dictionary<string, long>> itemVersionsByOrder)
            {
                Initialized = initialized;
                OrderVersions = orderVersions;
                ItemVersionsByOrder = itemVersionsByOrder;
            }

            public bool Initialized { get; }
            public Dictionary<string, long> OrderVersions { get; }
            public Dictionary<string, Dictionary<string, long>> ItemVersionsByOrder { get; }
        }

        private sealed class ConcurrencyConflictException : Exception
        {
            public ConcurrencyConflictException(string message) : base(message)
            {
            }
        }

        private sealed class OrderEventEntry
        {
            public OrderEventEntry(
                string orderInternalId,
                string itemId,
                string eventType,
                string eventSource,
                string payloadJson)
            {
                OrderInternalId = orderInternalId ?? string.Empty;
                ItemId = itemId ?? string.Empty;
                EventType = eventType ?? string.Empty;
                EventSource = eventSource ?? string.Empty;
                PayloadJson = payloadJson ?? "{}";
            }

            public string OrderInternalId { get; }
            public string ItemId { get; }
            public string EventType { get; }
            public string EventSource { get; }
            public string PayloadJson { get; }
        }
    }
}
