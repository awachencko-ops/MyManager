using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Replica;

public sealed record OrdersHistoryPostLoadResult(
    bool TopologyChanged,
    bool UsersNormalized,
    bool MetadataChanged,
    IReadOnlyList<string> MigrationLog)
{
    public bool Changed => TopologyChanged || UsersNormalized || MetadataChanged;
}

public sealed record OrdersHistoryPreSaveResult(
    bool TopologyChanged,
    bool MetadataChanged)
{
    public bool Changed => TopologyChanged || MetadataChanged;
}

public sealed class OrdersHistoryMaintenanceService
{
    private readonly Func<DateTime> _nowProvider;

    public OrdersHistoryMaintenanceService(Func<DateTime>? nowProvider = null)
    {
        _nowProvider = nowProvider ?? (() => DateTime.Now);
    }

    public OrdersHistoryPostLoadResult ApplyPostLoad(
        IList<OrderData> orders,
        Func<string?, string> normalizeUserName,
        int hashBackfillBudget = 32,
        Action<OrderData, string>? onTopologyIssue = null)
    {
        if (orders == null)
            throw new ArgumentNullException(nameof(orders));
        if (normalizeUserName == null)
            throw new ArgumentNullException(nameof(normalizeUserName));

        var usersNormalized = false;
        var metadataChanged = false;
        var migrationLog = new List<string>();
        var remainingHashBackfillBudget = Math.Max(0, hashBackfillBudget);

        foreach (var order in orders.Where(order => order != null))
        {
            if (string.IsNullOrWhiteSpace(order.InternalId))
            {
                order.InternalId = Guid.NewGuid().ToString("N");
                metadataChanged = true;
            }

            if (order.ArrivalDate == default)
            {
                order.ArrivalDate = order.OrderDate != default ? order.OrderDate : _nowProvider();
                metadataChanged = true;
            }

            if (remainingHashBackfillBudget > 0
                && PopulateKnownFileHashes(order, migrationLog, ref remainingHashBackfillBudget))
            {
                metadataChanged = true;
            }

            if (PopulateKnownFileSizes(order, migrationLog))
                metadataChanged = true;

            var normalizedUserName = normalizeUserName(order.UserName);
            if (string.Equals(order.UserName, normalizedUserName, StringComparison.Ordinal))
                continue;

            order.UserName = normalizedUserName;
            usersNormalized = true;
        }

        var topologyChanged = NormalizeOrderTopologyInHistory(orders, onTopologyIssue);
        return new OrdersHistoryPostLoadResult(
            topologyChanged,
            usersNormalized,
            metadataChanged,
            migrationLog);
    }

    public OrdersHistoryPreSaveResult ApplyPreSave(IList<OrderData> orders)
    {
        if (orders == null)
            throw new ArgumentNullException(nameof(orders));

        var topologyChanged = NormalizeOrderTopologyInHistory(orders, onTopologyIssue: null);
        var metadataChanged = PopulateKnownFileSizesInHistory(orders);
        return new OrdersHistoryPreSaveResult(topologyChanged, metadataChanged);
    }

    public bool NormalizeOrderTopologyInHistory(
        IList<OrderData> orders,
        Action<OrderData, string>? onTopologyIssue)
    {
        if (orders == null || orders.Count == 0)
            return false;

        var changed = false;
        foreach (var order in orders.Where(order => order != null))
        {
            var result = OrderTopologyService.Normalize(order);
            if (result.Changed)
                changed = true;

            if (onTopologyIssue == null || result.Issues.Count == 0)
                continue;

            foreach (var issue in result.Issues)
                onTopologyIssue(order, issue);
        }

        return changed;
    }

    public bool BackfillMissingFileHashesIncrementally(IList<OrderData> orders, int maxFilesToHash)
    {
        if (orders == null || maxFilesToHash <= 0 || orders.Count == 0)
            return false;

        var remainingBudget = maxFilesToHash;
        var changed = false;
        foreach (var order in orders.Where(order => order != null))
        {
            if (remainingBudget <= 0)
                break;

            changed |= PopulateKnownFileHashes(order, migrationLog: null, ref remainingBudget);
        }

        return changed;
    }

    private static bool PopulateKnownFileSizesInHistory(IList<OrderData> orders)
    {
        var changed = false;
        foreach (var order in orders.Where(order => order != null))
            changed |= PopulateKnownFileSizes(order, migrationLog: null);

        return changed;
    }

    private static long? TryGetExistingFileSize(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            return new FileInfo(path).Length;
        }
        catch
        {
            return null;
        }
    }

    private static bool PopulateKnownFileSizes(OrderData order, List<string>? migrationLog)
    {
        if (order == null)
            return false;

        var changed = false;
        var sourceSize = TryGetExistingFileSize(order.SourcePath);
        if (sourceSize != null && order.SourceFileSizeBytes != sourceSize)
        {
            order.SourceFileSizeBytes = sourceSize;
            changed = true;
            migrationLog?.Add($"MIGRATION | order={order.Id} | SourceFileSizeBytes={sourceSize} | path={order.SourcePath}");
        }

        var preparedSize = TryGetExistingFileSize(order.PreparedPath);
        if (preparedSize != null && order.PreparedFileSizeBytes != preparedSize)
        {
            order.PreparedFileSizeBytes = preparedSize;
            changed = true;
            migrationLog?.Add($"MIGRATION | order={order.Id} | PreparedFileSizeBytes={preparedSize} | path={order.PreparedPath}");
        }

        var printSize = TryGetExistingFileSize(order.PrintPath);
        if (printSize != null && order.PrintFileSizeBytes != printSize)
        {
            order.PrintFileSizeBytes = printSize;
            changed = true;
            migrationLog?.Add($"MIGRATION | order={order.Id} | PrintFileSizeBytes={printSize} | path={order.PrintPath}");
        }

        if (order.Items == null)
            return changed;

        foreach (var item in order.Items.Where(item => item != null))
        {
            var itemSourceSize = TryGetExistingFileSize(item.SourcePath);
            if (itemSourceSize != null && item.SourceFileSizeBytes != itemSourceSize)
            {
                item.SourceFileSizeBytes = itemSourceSize;
                changed = true;
                migrationLog?.Add($"MIGRATION | order={order.Id} | item={item.ClientFileLabel} | SourceFileSizeBytes={itemSourceSize} | path={item.SourcePath}");
            }

            var itemPreparedSize = TryGetExistingFileSize(item.PreparedPath);
            if (itemPreparedSize != null && item.PreparedFileSizeBytes != itemPreparedSize)
            {
                item.PreparedFileSizeBytes = itemPreparedSize;
                changed = true;
                migrationLog?.Add($"MIGRATION | order={order.Id} | item={item.ClientFileLabel} | PreparedFileSizeBytes={itemPreparedSize} | path={item.PreparedPath}");
            }

            var itemPrintSize = TryGetExistingFileSize(item.PrintPath);
            if (itemPrintSize != null && item.PrintFileSizeBytes != itemPrintSize)
            {
                item.PrintFileSizeBytes = itemPrintSize;
                changed = true;
                migrationLog?.Add($"MIGRATION | order={order.Id} | item={item.ClientFileLabel} | PrintFileSizeBytes={itemPrintSize} | path={item.PrintPath}");
            }
        }

        return changed;
    }

    private static bool PopulateKnownFileHashes(OrderData order, List<string>? migrationLog, ref int remainingBudget)
    {
        if (order == null || remainingBudget <= 0)
            return false;

        var changed = false;
        changed |= PopulateHashForPath(order, order.SourcePath, order.SourceFileHash, stageName: "Source", migrationLog, ref remainingBudget, valueSetter: hash => order.SourceFileHash = hash);
        changed |= PopulateHashForPath(order, order.PreparedPath, order.PreparedFileHash, stageName: "Prepared", migrationLog, ref remainingBudget, valueSetter: hash => order.PreparedFileHash = hash);
        changed |= PopulateHashForPath(order, order.PrintPath, order.PrintFileHash, stageName: "Print", migrationLog, ref remainingBudget, valueSetter: hash => order.PrintFileHash = hash);

        if (order.Items == null || remainingBudget <= 0)
            return changed;

        foreach (var item in order.Items.Where(item => item != null))
        {
            if (remainingBudget <= 0)
                break;

            changed |= PopulateHashForPath(order, item.SourcePath, item.SourceFileHash, stageName: $"item={item.ClientFileLabel} | Source", migrationLog, ref remainingBudget, valueSetter: hash => item.SourceFileHash = hash);
            changed |= PopulateHashForPath(order, item.PreparedPath, item.PreparedFileHash, stageName: $"item={item.ClientFileLabel} | Prepared", migrationLog, ref remainingBudget, valueSetter: hash => item.PreparedFileHash = hash);
            changed |= PopulateHashForPath(order, item.PrintPath, item.PrintFileHash, stageName: $"item={item.ClientFileLabel} | Print", migrationLog, ref remainingBudget, valueSetter: hash => item.PrintFileHash = hash);
        }

        return changed;
    }

    private static bool PopulateHashForPath(
        OrderData order,
        string? path,
        string? currentHash,
        string stageName,
        List<string>? migrationLog,
        ref int remainingBudget,
        Action<string> valueSetter)
    {
        if (remainingBudget <= 0)
            return false;

        if (!string.IsNullOrWhiteSpace(currentHash))
            return false;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (!FileHashService.TryComputeSha256(path, out var hash, out _))
            return false;

        valueSetter(hash);
        remainingBudget--;
        migrationLog?.Add($"MIGRATION | order={order.Id} | {stageName}FileHash={hash} | path={path}");
        return true;
    }
}
