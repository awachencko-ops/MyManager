using System;
using System.Collections.Generic;
using System.Linq;

namespace Replica;

public sealed class OrderStorageVersionSyncService
{
    public int SyncLocalVersions(
        IReadOnlyCollection<OrderData> localOrders,
        IReadOnlyCollection<OrderData> storageOrders)
    {
        if (localOrders == null)
            throw new ArgumentNullException(nameof(localOrders));
        if (storageOrders == null)
            throw new ArgumentNullException(nameof(storageOrders));

        var storageByInternalId = storageOrders
            .Where(order => order != null && !string.IsNullOrWhiteSpace(order.InternalId))
            .GroupBy(order => order.InternalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var updatedCount = 0;
        foreach (var localOrder in localOrders.Where(order => order != null && !string.IsNullOrWhiteSpace(order.InternalId)))
        {
            if (!storageByInternalId.TryGetValue(localOrder.InternalId, out var storageOrder))
                continue;

            if (localOrder.StorageVersion == storageOrder.StorageVersion)
                continue;

            localOrder.StorageVersion = storageOrder.StorageVersion;
            updatedCount++;
        }

        return updatedCount;
    }
}
