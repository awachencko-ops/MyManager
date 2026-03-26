using System.Collections.Generic;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderStorageVersionSyncServiceTests
{
    [Fact]
    public void SyncLocalVersions_UpdatesOnlyMatchingInternalIds()
    {
        var service = new OrderStorageVersionSyncService();
        var localOrders = new List<OrderData>
        {
            new() { InternalId = "order-1", StorageVersion = 1 },
            new() { InternalId = "order-2", StorageVersion = 2 },
            new() { InternalId = "order-3", StorageVersion = 3 }
        };
        var storageOrders = new List<OrderData>
        {
            new() { InternalId = "order-1", StorageVersion = 10 },
            new() { InternalId = "order-2", StorageVersion = 2 },
            new() { InternalId = "order-x", StorageVersion = 99 }
        };

        var updatedCount = service.SyncLocalVersions(localOrders, storageOrders);

        Assert.Equal(1, updatedCount);
        Assert.Equal(10, localOrders[0].StorageVersion);
        Assert.Equal(2, localOrders[1].StorageVersion);
        Assert.Equal(3, localOrders[2].StorageVersion);
    }

    [Fact]
    public void SyncLocalVersions_IgnoresOrdersWithEmptyInternalId()
    {
        var service = new OrderStorageVersionSyncService();
        var localOrders = new List<OrderData>
        {
            new() { InternalId = "", StorageVersion = 1 },
            new() { InternalId = "order-2", StorageVersion = 2 }
        };
        var storageOrders = new List<OrderData>
        {
            new() { InternalId = "", StorageVersion = 11 },
            new() { InternalId = "order-2", StorageVersion = 22 }
        };

        var updatedCount = service.SyncLocalVersions(localOrders, storageOrders);

        Assert.Equal(1, updatedCount);
        Assert.Equal(1, localOrders[0].StorageVersion);
        Assert.Equal(22, localOrders[1].StorageVersion);
    }

    [Fact]
    public void SyncLocalVersions_UpdatesMatchingItemVersions()
    {
        var service = new OrderStorageVersionSyncService();
        var localOrders = new List<OrderData>
        {
            new()
            {
                InternalId = "order-1",
                StorageVersion = 1,
                Items = new List<OrderFileItem>
                {
                    new() { ItemId = "item-a", StorageVersion = 1 },
                    new() { ItemId = "item-b", StorageVersion = 2 }
                }
            }
        };
        var storageOrders = new List<OrderData>
        {
            new()
            {
                InternalId = "order-1",
                StorageVersion = 1,
                Items = new List<OrderFileItem>
                {
                    new() { ItemId = "item-a", StorageVersion = 10 },
                    new() { ItemId = "item-b", StorageVersion = 2 }
                }
            }
        };

        var updatedCount = service.SyncLocalVersions(localOrders, storageOrders);

        Assert.Equal(1, updatedCount);
        Assert.Equal(10, localOrders[0].Items[0].StorageVersion);
        Assert.Equal(2, localOrders[0].Items[1].StorageVersion);
    }

    [Fact]
    public void SyncLocalVersions_IgnoresItemsWithEmptyItemId()
    {
        var service = new OrderStorageVersionSyncService();
        var localOrders = new List<OrderData>
        {
            new()
            {
                InternalId = "order-1",
                StorageVersion = 1,
                Items = new List<OrderFileItem>
                {
                    new() { ItemId = "", StorageVersion = 3 },
                    new() { ItemId = "item-x", StorageVersion = 4 }
                }
            }
        };
        var storageOrders = new List<OrderData>
        {
            new()
            {
                InternalId = "order-1",
                StorageVersion = 1,
                Items = new List<OrderFileItem>
                {
                    new() { ItemId = "", StorageVersion = 30 },
                    new() { ItemId = "item-x", StorageVersion = 44 }
                }
            }
        };

        var updatedCount = service.SyncLocalVersions(localOrders, storageOrders);

        Assert.Equal(1, updatedCount);
        Assert.Equal(3, localOrders[0].Items[0].StorageVersion);
        Assert.Equal(44, localOrders[0].Items[1].StorageVersion);
    }
}
