using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrdersHistoryMaintenanceServiceTests
{
    [Fact]
    public void ApplyPostLoad_NormalizesIdentityMetadataUsersAndTopology()
    {
        var fixedNow = new DateTime(2026, 3, 20, 16, 0, 0, DateTimeKind.Local);
        var service = new OrdersHistoryMaintenanceService(() => fixedNow);
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-history-postload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourcePath = Path.Combine(tempRoot, "source.pdf");
            File.WriteAllText(sourcePath, "source-content");

            var order = new OrderData
            {
                InternalId = string.Empty,
                OrderDate = default,
                ArrivalDate = default,
                UserName = " user-a ",
                SourcePath = sourcePath,
                FileTopologyMarker = OrderFileTopologyMarker.Unknown,
                Items = new List<OrderFileItem>()
            };

            var issues = new List<string>();
            var result = service.ApplyPostLoad(
                new List<OrderData> { order },
                rawUserName => string.IsNullOrWhiteSpace(rawUserName)
                    ? "DEFAULT"
                    : rawUserName.Trim().ToUpperInvariant(),
                hashBackfillBudget: 32,
                onTopologyIssue: (_, issue) => issues.Add(issue));

            Assert.True(result.Changed);
            Assert.True(result.MetadataChanged);
            Assert.True(result.UsersNormalized);
            Assert.True(result.TopologyChanged);
            Assert.NotEmpty(result.MigrationLog);
            Assert.NotEmpty(issues);

            Assert.False(string.IsNullOrWhiteSpace(order.InternalId));
            Assert.Equal(fixedNow, order.ArrivalDate);
            Assert.Equal("USER-A", order.UserName);
            Assert.NotNull(order.SourceFileSizeBytes);
            Assert.True(order.SourceFileSizeBytes > 0);
            Assert.False(string.IsNullOrWhiteSpace(order.SourceFileHash));
            Assert.Equal(OrderFileTopologyMarker.SingleOrder, order.FileTopologyMarker);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void BackfillMissingFileHashesIncrementally_RespectsBudget()
    {
        var service = new OrdersHistoryMaintenanceService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "replica-history-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourcePath = Path.Combine(tempRoot, "source.pdf");
            var preparedPath = Path.Combine(tempRoot, "prepared.pdf");
            var printPath = Path.Combine(tempRoot, "print.pdf");
            File.WriteAllText(sourcePath, "source");
            File.WriteAllText(preparedPath, "prepared");
            File.WriteAllText(printPath, "print");

            var order = new OrderData
            {
                InternalId = "order-1",
                SourcePath = sourcePath,
                PreparedPath = preparedPath,
                PrintPath = printPath,
                SourceFileHash = string.Empty,
                PreparedFileHash = string.Empty,
                PrintFileHash = string.Empty
            };

            var changed = service.BackfillMissingFileHashesIncrementally(
                new List<OrderData> { order },
                maxFilesToHash: 2);

            var hashesCount = 0;
            if (!string.IsNullOrWhiteSpace(order.SourceFileHash))
                hashesCount++;
            if (!string.IsNullOrWhiteSpace(order.PreparedFileHash))
                hashesCount++;
            if (!string.IsNullOrWhiteSpace(order.PrintFileHash))
                hashesCount++;

            Assert.True(changed);
            Assert.Equal(2, hashesCount);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void NormalizeOrderTopologyInHistory_ForMultiOrderWithOrderPath_ReportsIssue()
    {
        var service = new OrdersHistoryMaintenanceService();
        var order = new OrderData
        {
            InternalId = "order-2",
            FileTopologyMarker = OrderFileTopologyMarker.Unknown,
            SourcePath = @"C:\orders\source.pdf",
            Items = new List<OrderFileItem>
            {
                new() { ItemId = "item-1", SequenceNo = 0 },
                new() { ItemId = "item-2", SequenceNo = 1 }
            }
        };

        var issues = new List<string>();
        var changed = service.NormalizeOrderTopologyInHistory(
            new List<OrderData> { order },
            (_, issue) => issues.Add(issue));

        Assert.True(changed);
        Assert.Equal(OrderFileTopologyMarker.MultiOrder, order.FileTopologyMarker);
        Assert.Contains(issues, issue => issue.Contains("MultiOrder-заказ содержит пути", StringComparison.Ordinal));
    }
}
