using System.Text.Json;
using System.Text.Json.Serialization;
using Replica.Shared.Models;

namespace Replica.Api.Infrastructure;

public sealed class ReplicaApiReconciliationReportBuilder
{
    public ReplicaApiReconciliationReport Build(
        IReadOnlyCollection<SharedOrder> pgOrders,
        IReadOnlyCollection<SharedOrder> jsonOrders)
    {
        var pgById = NormalizeOrders(pgOrders);
        var jsonById = NormalizeOrders(jsonOrders);

        var missingInPg = jsonById.Keys.Except(pgById.Keys, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var missingInJson = pgById.Keys.Except(jsonById.Keys, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();

        var versionMismatches = new List<ReplicaApiReconciliationVersionMismatch>();
        var payloadMismatches = new List<ReplicaApiReconciliationPayloadMismatch>();

        foreach (var orderId in pgById.Keys.Intersect(jsonById.Keys, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
        {
            var pgOrder = pgById[orderId];
            var jsonOrder = jsonById[orderId];

            if (pgOrder.Version != jsonOrder.Version)
            {
                versionMismatches.Add(new ReplicaApiReconciliationVersionMismatch(
                    Scope: "order",
                    InternalId: orderId,
                    ItemId: string.Empty,
                    PgVersion: pgOrder.Version,
                    JsonVersion: jsonOrder.Version));
            }

            AddFieldMismatchIfNeeded(payloadMismatches, orderId, string.Empty, "OrderNumber", pgOrder.OrderNumber, jsonOrder.OrderNumber);
            AddFieldMismatchIfNeeded(payloadMismatches, orderId, string.Empty, "Status", pgOrder.Status, jsonOrder.Status);
            AddFieldMismatchIfNeeded(payloadMismatches, orderId, string.Empty, "UserName", pgOrder.UserName, jsonOrder.UserName);
            AddFieldMismatchIfNeeded(payloadMismatches, orderId, string.Empty, "Keyword", pgOrder.Keyword, jsonOrder.Keyword);
            AddFieldMismatchIfNeeded(payloadMismatches, orderId, string.Empty, "FolderName", pgOrder.FolderName, jsonOrder.FolderName);
            AddFieldMismatchIfNeeded(payloadMismatches, orderId, string.Empty, "CreatedById", pgOrder.CreatedById, jsonOrder.CreatedById);
            AddFieldMismatchIfNeeded(payloadMismatches, orderId, string.Empty, "CreatedByUser", pgOrder.CreatedByUser, jsonOrder.CreatedByUser);

            var pgItems = NormalizeItems(pgOrder.Items);
            var jsonItems = NormalizeItems(jsonOrder.Items);

            foreach (var missingItemInPg in jsonItems.Keys.Except(pgItems.Keys, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                payloadMismatches.Add(new ReplicaApiReconciliationPayloadMismatch(
                    InternalId: orderId,
                    ItemId: missingItemInPg,
                    Field: "item-missing-in-pg",
                    PgValue: string.Empty,
                    JsonValue: "exists"));
            }

            foreach (var missingItemInJson in pgItems.Keys.Except(jsonItems.Keys, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                payloadMismatches.Add(new ReplicaApiReconciliationPayloadMismatch(
                    InternalId: orderId,
                    ItemId: missingItemInJson,
                    Field: "item-missing-in-json",
                    PgValue: "exists",
                    JsonValue: string.Empty));
            }

            foreach (var itemId in pgItems.Keys.Intersect(jsonItems.Keys, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                var pgItem = pgItems[itemId];
                var jsonItem = jsonItems[itemId];

                if (pgItem.Version != jsonItem.Version)
                {
                    versionMismatches.Add(new ReplicaApiReconciliationVersionMismatch(
                        Scope: "item",
                        InternalId: orderId,
                        ItemId: itemId,
                        PgVersion: pgItem.Version,
                        JsonVersion: jsonItem.Version));
                }

                AddFieldMismatchIfNeeded(payloadMismatches, orderId, itemId, "FileStatus", pgItem.FileStatus, jsonItem.FileStatus);
                AddFieldMismatchIfNeeded(payloadMismatches, orderId, itemId, "Variant", pgItem.Variant, jsonItem.Variant);
                AddFieldMismatchIfNeeded(payloadMismatches, orderId, itemId, "ClientFileLabel", pgItem.ClientFileLabel, jsonItem.ClientFileLabel);
            }
        }

        var summary = new ReplicaApiReconciliationSummary(
            MissingInPg: missingInPg.Count,
            MissingInJson: missingInJson.Count,
            VersionMismatch: versionMismatches.Count,
            PayloadMismatch: payloadMismatches.Count,
            IsZeroDiff: missingInPg.Count == 0
                        && missingInJson.Count == 0
                        && versionMismatches.Count == 0
                        && payloadMismatches.Count == 0);

        return new ReplicaApiReconciliationReport(
            GeneratedAtUtc: DateTime.UtcNow,
            PgOrdersTotal: pgById.Count,
            JsonOrdersTotal: jsonById.Count,
            MissingInPg: missingInPg,
            MissingInJson: missingInJson,
            VersionMismatch: versionMismatches,
            PayloadMismatch: payloadMismatches,
            Summary: summary);
    }

    private static Dictionary<string, SharedOrder> NormalizeOrders(IReadOnlyCollection<SharedOrder> source)
    {
        var result = new Dictionary<string, SharedOrder>(StringComparer.Ordinal);
        if (source == null)
            return result;

        foreach (var order in source)
        {
            if (order == null)
                continue;

            var key = (order.InternalId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            result[key] = order;
        }

        return result;
    }

    private static Dictionary<string, SharedOrderItem> NormalizeItems(IReadOnlyCollection<SharedOrderItem> source)
    {
        var result = new Dictionary<string, SharedOrderItem>(StringComparer.Ordinal);
        if (source == null)
            return result;

        foreach (var item in source)
        {
            if (item == null)
                continue;

            var key = (item.ItemId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            result[key] = item;
        }

        return result;
    }

    private static void AddFieldMismatchIfNeeded(
        ICollection<ReplicaApiReconciliationPayloadMismatch> destination,
        string internalId,
        string itemId,
        string field,
        string? pgValue,
        string? jsonValue)
    {
        var normalizedPgValue = (pgValue ?? string.Empty).Trim();
        var normalizedJsonValue = (jsonValue ?? string.Empty).Trim();
        if (string.Equals(normalizedPgValue, normalizedJsonValue, StringComparison.Ordinal))
            return;

        destination.Add(new ReplicaApiReconciliationPayloadMismatch(
            InternalId: internalId,
            ItemId: itemId ?? string.Empty,
            Field: field,
            PgValue: normalizedPgValue,
            JsonValue: normalizedJsonValue));
    }
}

public static class ReplicaApiReconciliationReportIo
{
    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<ReplicaApiReconciliationReport> BuildFromFilesAsync(
        string pgSnapshotPath,
        string jsonSnapshotPath,
        CancellationToken cancellationToken = default)
    {
        var pgOrders = await LoadOrdersFromSnapshotAsync(pgSnapshotPath, cancellationToken);
        var jsonOrders = await LoadOrdersFromSnapshotAsync(jsonSnapshotPath, cancellationToken);
        return new ReplicaApiReconciliationReportBuilder().Build(pgOrders, jsonOrders);
    }

    public static async Task WriteReportAsync(
        string reportPath,
        ReplicaApiReconciliationReport report,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
            throw new ArgumentException("report path is required", nameof(reportPath));
        if (report == null)
            throw new ArgumentNullException(nameof(report));

        var directoryPath = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
            Directory.CreateDirectory(directoryPath);

        var json = JsonSerializer.Serialize(report, WriteJsonOptions);
        await File.WriteAllTextAsync(reportPath, json, cancellationToken);
    }

    public static async Task<IReadOnlyList<SharedOrder>> LoadOrdersFromSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath))
            throw new ArgumentException("snapshot path is required", nameof(snapshotPath));

        await using var stream = File.OpenRead(snapshotPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<SharedOrder>>(root.GetRawText()) ?? [];
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGetOrdersProperty(root, out var ordersElement))
                return JsonSerializer.Deserialize<List<SharedOrder>>(ordersElement.GetRawText()) ?? [];

            return [];
        }

        return [];
    }

    private static bool TryGetOrdersProperty(JsonElement root, out JsonElement ordersElement)
    {
        if (root.TryGetProperty("Orders", out ordersElement))
            return ordersElement.ValueKind == JsonValueKind.Array;
        if (root.TryGetProperty("orders", out ordersElement))
            return ordersElement.ValueKind == JsonValueKind.Array;

        ordersElement = default;
        return false;
    }
}

public sealed record ReplicaApiReconciliationReport(
    [property: JsonPropertyName("generated_at_utc")] DateTime GeneratedAtUtc,
    [property: JsonPropertyName("pg_orders_total")] int PgOrdersTotal,
    [property: JsonPropertyName("json_orders_total")] int JsonOrdersTotal,
    [property: JsonPropertyName("missing_in_pg")] IReadOnlyList<string> MissingInPg,
    [property: JsonPropertyName("missing_in_json")] IReadOnlyList<string> MissingInJson,
    [property: JsonPropertyName("version_mismatch")] IReadOnlyList<ReplicaApiReconciliationVersionMismatch> VersionMismatch,
    [property: JsonPropertyName("payload_mismatch")] IReadOnlyList<ReplicaApiReconciliationPayloadMismatch> PayloadMismatch,
    [property: JsonPropertyName("summary")] ReplicaApiReconciliationSummary Summary);

public sealed record ReplicaApiReconciliationSummary(
    [property: JsonPropertyName("missing_in_pg")] int MissingInPg,
    [property: JsonPropertyName("missing_in_json")] int MissingInJson,
    [property: JsonPropertyName("version_mismatch")] int VersionMismatch,
    [property: JsonPropertyName("payload_mismatch")] int PayloadMismatch,
    [property: JsonPropertyName("is_zero_diff")] bool IsZeroDiff);

public sealed record ReplicaApiReconciliationVersionMismatch(
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("internal_id")] string InternalId,
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("pg_version")] long PgVersion,
    [property: JsonPropertyName("json_version")] long JsonVersion);

public sealed record ReplicaApiReconciliationPayloadMismatch(
    [property: JsonPropertyName("internal_id")] string InternalId,
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("pg_value")] string PgValue,
    [property: JsonPropertyName("json_value")] string JsonValue);
