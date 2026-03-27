using System;
using System.Text.Json;
using Xunit;

namespace Replica.VerifyTests;

public sealed class LanOrderPushClientTests
{
    [Fact]
    public void Parse_OrderUpdated_ExtractsOrderIdAndTimestamp()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            orderId = "order-123",
            occurredAtUtc = "2026-03-27T11:22:33Z"
        });

        var parsed = LanOrderPushEventParser.Parse(
            LanOrderPushEventNames.OrderUpdated,
            payload,
            fallbackUtc: new DateTime(2026, 3, 27, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LanOrderPushEventNames.OrderUpdated, parsed.EventType);
        Assert.Equal("order-123", parsed.OrderId);
        Assert.Equal(string.Empty, parsed.Reason);
        Assert.Equal(new DateTime(2026, 3, 27, 11, 22, 33, DateTimeKind.Utc), parsed.OccurredAtUtc);
    }

    [Fact]
    public void Parse_ForceRefresh_ExtractsReasonAndClearsOrderId()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            orderId = "must-be-ignored",
            reason = "users-changed",
            occurredAtUtc = "2026-03-27T08:00:00Z"
        });

        var parsed = LanOrderPushEventParser.Parse(
            LanOrderPushEventNames.ForceRefresh,
            payload,
            fallbackUtc: new DateTime(2026, 3, 27, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LanOrderPushEventNames.ForceRefresh, parsed.EventType);
        Assert.Equal(string.Empty, parsed.OrderId);
        Assert.Equal("users-changed", parsed.Reason);
        Assert.Equal(new DateTime(2026, 3, 27, 8, 0, 0, DateTimeKind.Utc), parsed.OccurredAtUtc);
    }

    [Fact]
    public void Parse_InvalidPayload_FallsBackToProvidedUtc()
    {
        var fallbackUtc = new DateTime(2026, 3, 27, 7, 0, 0, DateTimeKind.Utc);

        var parsed = LanOrderPushEventParser.Parse(
            LanOrderPushEventNames.OrderDeleted,
            payload: "not-a-json-object",
            fallbackUtc: fallbackUtc);

        Assert.Equal(LanOrderPushEventNames.OrderDeleted, parsed.EventType);
        Assert.Equal(string.Empty, parsed.OrderId);
        Assert.Equal(string.Empty, parsed.Reason);
        Assert.Equal(fallbackUtc, parsed.OccurredAtUtc);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 30)]
    [InlineData(99, 30)]
    public void ReconnectPolicy_ReturnsExpectedDelaySeconds(int retryCount, int expectedSeconds)
    {
        var policy = new LanOrderPushReconnectPolicy();

        var delay = policy.ResolveDelay(retryCount);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }
}
