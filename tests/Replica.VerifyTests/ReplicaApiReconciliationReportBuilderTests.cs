using System.Text.Json;
using Replica.Api.Infrastructure;
using Replica.Shared.Models;
using Xunit;

namespace Replica.VerifyTests;

public sealed class ReplicaApiReconciliationReportBuilderTests
{
    [Fact]
    public void Build_WhenSnapshotsDiffer_PopulatesAllMismatchBuckets()
    {
        var pgOrders = new List<SharedOrder>
        {
            new()
            {
                InternalId = "ord-1",
                OrderNumber = "1001",
                Status = "Processing",
                Version = 7,
                Items =
                [
                    new SharedOrderItem
                    {
                        ItemId = "item-1",
                        Version = 4,
                        FileStatus = "Done",
                        Variant = "A"
                    }
                ]
            },
            new()
            {
                InternalId = "ord-2",
                OrderNumber = "1002",
                Status = "Waiting",
                Version = 1
            }
        };

        var jsonOrders = new List<SharedOrder>
        {
            new()
            {
                InternalId = "ord-1",
                OrderNumber = "1001",
                Status = "Waiting",
                Version = 6,
                Items =
                [
                    new SharedOrderItem
                    {
                        ItemId = "item-1",
                        Version = 2,
                        FileStatus = "Queued",
                        Variant = "A"
                    },
                    new SharedOrderItem
                    {
                        ItemId = "item-2",
                        Version = 1,
                        FileStatus = "Queued"
                    }
                ]
            },
            new()
            {
                InternalId = "ord-3",
                OrderNumber = "1003",
                Status = "Waiting",
                Version = 1
            }
        };

        var report = new ReplicaApiReconciliationReportBuilder().Build(pgOrders, jsonOrders);

        Assert.Contains("ord-3", report.MissingInPg);
        Assert.Contains("ord-2", report.MissingInJson);

        Assert.Contains(report.VersionMismatch, mismatch =>
            mismatch.Scope == "order"
            && mismatch.InternalId == "ord-1"
            && mismatch.PgVersion == 7
            && mismatch.JsonVersion == 6);
        Assert.Contains(report.VersionMismatch, mismatch =>
            mismatch.Scope == "item"
            && mismatch.InternalId == "ord-1"
            && mismatch.ItemId == "item-1"
            && mismatch.PgVersion == 4
            && mismatch.JsonVersion == 2);

        Assert.Contains(report.PayloadMismatch, mismatch =>
            mismatch.InternalId == "ord-1"
            && mismatch.ItemId == string.Empty
            && mismatch.Field == "Status"
            && mismatch.PgValue == "Processing"
            && mismatch.JsonValue == "Waiting");
        Assert.Contains(report.PayloadMismatch, mismatch =>
            mismatch.InternalId == "ord-1"
            && mismatch.ItemId == "item-2"
            && mismatch.Field == "item-missing-in-pg");

        Assert.False(report.Summary.IsZeroDiff);
        Assert.True(report.Summary.MissingInPg > 0);
        Assert.True(report.Summary.MissingInJson > 0);
        Assert.True(report.Summary.VersionMismatch > 0);
        Assert.True(report.Summary.PayloadMismatch > 0);
    }

    [Fact]
    public async Task LoadOrdersFromSnapshotAsync_SupportsArrayAndEnvelopeShapes()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "replica-reconcile-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var arrayPath = Path.Combine(tempDirectory, "pg-snapshot.json");
        var envelopePath = Path.Combine(tempDirectory, "json-snapshot.json");

        try
        {
            await File.WriteAllTextAsync(arrayPath,
                "[{\"InternalId\":\"ord-1\",\"OrderNumber\":\"1001\",\"Version\":1}]");
            await File.WriteAllTextAsync(envelopePath,
                "{\"GeneratedAtUtc\":\"2026-03-27T00:00:00Z\",\"Orders\":[{\"InternalId\":\"ord-2\",\"OrderNumber\":\"1002\",\"Version\":2}]}");

            var arrayOrders = await ReplicaApiReconciliationReportIo.LoadOrdersFromSnapshotAsync(arrayPath);
            var envelopeOrders = await ReplicaApiReconciliationReportIo.LoadOrdersFromSnapshotAsync(envelopePath);

            Assert.Single(arrayOrders);
            Assert.Equal("ord-1", arrayOrders[0].InternalId);
            Assert.Single(envelopeOrders);
            Assert.Equal("ord-2", envelopeOrders[0].InternalId);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task BuildFromFilesAndWriteReportAsync_WritesMachineReadableJsonArtifact()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "replica-reconcile-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var pgPath = Path.Combine(tempDirectory, "pg-snapshot.json");
        var jsonPath = Path.Combine(tempDirectory, "json-snapshot.json");
        var reportPath = Path.Combine(tempDirectory, "reconciliation-report.json");

        try
        {
            await File.WriteAllTextAsync(pgPath,
                "[{\"InternalId\":\"ord-1\",\"OrderNumber\":\"1001\",\"Version\":1}]");
            await File.WriteAllTextAsync(jsonPath,
                "{\"orders\":[{\"InternalId\":\"ord-1\",\"OrderNumber\":\"1001\",\"Version\":1}]}");

            var report = await ReplicaApiReconciliationReportIo.BuildFromFilesAsync(pgPath, jsonPath);
            await ReplicaApiReconciliationReportIo.WriteReportAsync(reportPath, report);

            Assert.True(File.Exists(reportPath));

            await using var reportStream = File.OpenRead(reportPath);
            using var document = await JsonDocument.ParseAsync(reportStream);
            var root = document.RootElement;

            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(summary.TryGetProperty("is_zero_diff", out var isZeroDiff));
            Assert.True(isZeroDiff.GetBoolean());
            Assert.True(root.TryGetProperty("missing_in_pg", out _));
            Assert.True(root.TryGetProperty("missing_in_json", out _));
            Assert.True(root.TryGetProperty("version_mismatch", out _));
            Assert.True(root.TryGetProperty("payload_mismatch", out _));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
