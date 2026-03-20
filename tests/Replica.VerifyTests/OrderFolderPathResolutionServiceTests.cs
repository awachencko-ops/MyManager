using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderFolderPathResolutionServiceTests
{
    [Fact]
    public void ResolvePreferredOrderFolder_WhenFolderNamePresent_UsesOrdersRoot()
    {
        var service = new OrderFolderPathResolutionService();
        var order = new OrderData
        {
            InternalId = "order-1",
            FolderName = "2026-03-20 №123"
        };

        var result = service.ResolvePreferredOrderFolder(order, @"C:\orders-root", @"C:\temp-root");

        Assert.Equal(Path.Combine(@"C:\orders-root", "2026-03-20 №123"), result);
    }

    [Fact]
    public void ResolveBrowseFolderPath_ForSingleOrder_UsesPreferredFolder()
    {
        var service = new OrderFolderPathResolutionService();
        var order = new OrderData
        {
            InternalId = "order-2",
            PrintPath = Path.Combine(@"C:\orders-root", "o-2", "3. печать", "print.pdf"),
            Items = new List<OrderFileItem>()
        };

        var resolution = service.ResolveBrowseFolderPath(order, @"C:\orders-root", @"C:\temp-root");

        Assert.True(resolution.Success);
        Assert.Equal(Path.Combine(@"C:\orders-root", "o-2", "3. печать"), resolution.FolderPath);
    }

    [Fact]
    public void ResolveBrowseFolderPath_ForGroupOrderWithDifferentRoots_ReturnsMismatchReason()
    {
        var service = new OrderFolderPathResolutionService();
        var order = new OrderData
        {
            InternalId = "order-3",
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Items = new List<OrderFileItem>
            {
                new() { ItemId = "item-1", SequenceNo = 0, SourcePath = @"C:\orders\a\item1.pdf" },
                new() { ItemId = "item-2", SequenceNo = 1, SourcePath = @"D:\orders\b\item2.pdf" }
            }
        };

        var resolution = service.ResolveBrowseFolderPath(order, @"C:\orders-root", @"C:\temp-root");

        Assert.False(resolution.Success);
        Assert.Equal("Пути не совпадают", resolution.Reason);
        Assert.True(string.IsNullOrWhiteSpace(resolution.FolderPath));
    }

    [Fact]
    public void ResolveBrowseFolderPath_ForGroupOrderWithSharedDirectory_ReturnsCommonFolder()
    {
        var service = new OrderFolderPathResolutionService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-folder-path-" + Guid.NewGuid().ToString("N"));
        var commonRoot = Path.Combine(tempRoot, "group-root");
        var dir1 = Path.Combine(commonRoot, "a");
        var dir2 = Path.Combine(commonRoot, "b");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        try
        {
            var order = new OrderData
            {
                InternalId = "order-4",
                FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
                Items = new List<OrderFileItem>
                {
                    new() { ItemId = "item-1", SequenceNo = 0, SourcePath = Path.Combine(dir1, "item1.pdf") },
                    new() { ItemId = "item-2", SequenceNo = 1, SourcePath = Path.Combine(dir2, "item2.pdf") }
                }
            };

            var resolution = service.ResolveBrowseFolderPath(order, @"C:\orders-root", tempRoot);

            Assert.True(resolution.Success);
            Assert.Equal(commonRoot.ToUpperInvariant(), resolution.FolderPath.ToUpperInvariant());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }
}
