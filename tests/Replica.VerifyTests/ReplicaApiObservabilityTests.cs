using System.Linq;
using Replica.Api.Infrastructure;
using Xunit;

namespace Replica.VerifyTests;

public sealed class ReplicaApiObservabilityTests
{
    [Theory]
    [InlineData("/api/orders", "/api/orders")]
    [InlineData("/api/orders/abc123", "/api/orders/{id}")]
    [InlineData("/api/orders/abc123/items", "/api/orders/{id}/items")]
    [InlineData("/api/orders/abc123/items/reorder", "/api/orders/{id}/items/reorder")]
    [InlineData("/api/orders/abc123/items/item-7", "/api/orders/{id}/items/{itemId}")]
    [InlineData("/api/users", "/api/users")]
    [InlineData("/api/custom/12345", "/api/custom/{id}")]
    [InlineData("/hubs/orders", "/hubs/orders")]
    [InlineData("/hubs/orders/negotiate", "/hubs/orders/negotiate")]
    public void NormalizeRequestPath_NormalizesKnownRoutes(string rawPath, string expected)
    {
        var normalized = ReplicaApiObservability.NormalizeRequestPath(rawPath);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void RecordWriteAndIdempotency_UpdatesGlobalAndCommandCounters()
    {
        const string commandName = "delete-order";
        var before = ReplicaApiObservability.GetSnapshot();
        var beforeCommand = GetCommandSnapshot(before, commandName);

        ReplicaApiObservability.RecordWriteCommand(commandName, "success");
        ReplicaApiObservability.RecordWriteCommand(commandName, "conflict");
        ReplicaApiObservability.RecordIdempotency(commandName, IdempotencyTelemetryOutcome.Hit);
        ReplicaApiObservability.RecordIdempotency(commandName, IdempotencyTelemetryOutcome.Miss);

        var after = ReplicaApiObservability.GetSnapshot();
        var afterCommand = GetCommandSnapshot(after, commandName);

        Assert.True(after.WriteCommandsTotal >= before.WriteCommandsTotal + 2);
        Assert.True(after.WriteSuccess >= before.WriteSuccess + 1);
        Assert.True(after.WriteConflict >= before.WriteConflict + 1);
        Assert.True(after.IdempotencyHits >= before.IdempotencyHits + 1);
        Assert.True(after.IdempotencyMisses >= before.IdempotencyMisses + 1);

        Assert.True(afterCommand.WriteTotal >= beforeCommand.WriteTotal + 2);
        Assert.True(afterCommand.WriteSuccess >= beforeCommand.WriteSuccess + 1);
        Assert.True(afterCommand.WriteConflict >= beforeCommand.WriteConflict + 1);
        Assert.True(afterCommand.IdempotencyHit >= beforeCommand.IdempotencyHit + 1);
        Assert.True(afterCommand.IdempotencyMiss >= beforeCommand.IdempotencyMiss + 1);
    }

    [Fact]
    public void RecordHttpRequest_TracksRouteAndLatency()
    {
        const string commandKey = "POST /api/orders/{id}/items/reorder";
        var before = ReplicaApiObservability.GetSnapshot();
        var beforeCommand = GetCommandSnapshot(before, commandKey);
        var beforeBuckets = before.HttpLatencyBuckets.Values.Sum();

        ReplicaApiObservability.RecordHttpRequest("POST", "/api/orders/abc123/items/reorder", 200, 42);

        var after = ReplicaApiObservability.GetSnapshot();
        var afterCommand = GetCommandSnapshot(after, commandKey);
        var afterBuckets = after.HttpLatencyBuckets.Values.Sum();

        Assert.True(after.HttpRequestsTotal >= before.HttpRequestsTotal + 1);
        Assert.True(afterBuckets >= beforeBuckets + 1);
        Assert.True(afterCommand.HttpCount >= beforeCommand.HttpCount + 1);
    }

    [Fact]
    public void RecordHttpRequest_ForSignalRHub_DoesNotAffectLatencySloSample()
    {
        const string commandKey = "GET /hubs/orders";
        var before = ReplicaApiObservability.GetSnapshot();
        var beforeCommand = GetCommandSnapshot(before, commandKey);
        var beforeBuckets = before.HttpLatencyBuckets.Values.Sum();

        ReplicaApiObservability.RecordHttpRequest("GET", "/hubs/orders", 200, 12000);

        var after = ReplicaApiObservability.GetSnapshot();
        var afterCommand = GetCommandSnapshot(after, commandKey);
        var afterBuckets = after.HttpLatencyBuckets.Values.Sum();

        Assert.True(after.HttpRequestsTotal >= before.HttpRequestsTotal + 1);
        Assert.True(afterCommand.HttpCount >= beforeCommand.HttpCount + 1);
        Assert.Equal(before.HttpLatencySampleTotal, after.HttpLatencySampleTotal);
        Assert.Equal(beforeBuckets, afterBuckets);
    }

    [Fact]
    public void RecordPushPublishAndFailure_UpdatesPushCounters()
    {
        var before = ReplicaApiObservability.GetSnapshot();

        ReplicaApiObservability.RecordPushPublished("OrderUpdated");
        ReplicaApiObservability.RecordPushPublished("OrderDeleted");
        ReplicaApiObservability.RecordPushPublished("ForceRefresh");
        ReplicaApiObservability.RecordPushPublishFailure("OrderUpdated");
        ReplicaApiObservability.RecordPushPublishFailure("ForceRefresh");

        var after = ReplicaApiObservability.GetSnapshot();

        Assert.True(after.PushPublishedTotal >= before.PushPublishedTotal + 3);
        Assert.True(after.PushPublishFailuresTotal >= before.PushPublishFailuresTotal + 2);
        Assert.True(after.PushOrderUpdatedPublished >= before.PushOrderUpdatedPublished + 1);
        Assert.True(after.PushOrderDeletedPublished >= before.PushOrderDeletedPublished + 1);
        Assert.True(after.PushForceRefreshPublished >= before.PushForceRefreshPublished + 1);
        Assert.True(after.PushOrderUpdatedFailures >= before.PushOrderUpdatedFailures + 1);
        Assert.True(after.PushForceRefreshFailures >= before.PushForceRefreshFailures + 1);
    }

    private static ReplicaApiCommandMetricsSnapshot GetCommandSnapshot(ReplicaApiObservabilitySnapshot snapshot, string command)
    {
        return snapshot.Commands.TryGetValue(command, out var value)
            ? value
            : new ReplicaApiCommandMetricsSnapshot();
    }
}
