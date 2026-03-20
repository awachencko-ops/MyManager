using System;
using Xunit;

namespace Replica.VerifyTests;

public sealed class LogContextTests
{
    [Fact]
    public void BeginCorrelationScope_AssignsAndRestoresCorrelationId()
    {
        var before = LogContext.CorrelationId;
        using (LogContext.BeginCorrelationScope("corr-ctx-1"))
        {
            Assert.Equal("corr-ctx-1", LogContext.CorrelationId);
            var snapshot = LogContext.GetPropertiesSnapshot();
            Assert.True(snapshot.TryGetValue("correlation_id", out var value));
            Assert.Equal("corr-ctx-1", value);
        }

        Assert.Equal(before, LogContext.CorrelationId);
    }

    [Fact]
    public void BeginScope_NestedScopes_InnerOverridesOuterProperties()
    {
        using (LogContext.BeginCorrelationScope("corr-ctx-2"))
        using (LogContext.BeginScope(("component", "outer"), ("order_id", "1001")))
        {
            using (LogContext.BeginScope(("component", "inner"), ("item_id", "item-1")))
            {
                var innerSnapshot = LogContext.GetPropertiesSnapshot();
                Assert.Equal("corr-ctx-2", innerSnapshot["correlation_id"]);
                Assert.Equal("inner", innerSnapshot["component"]);
                Assert.Equal("1001", innerSnapshot["order_id"]);
                Assert.Equal("item-1", innerSnapshot["item_id"]);
            }

            var outerSnapshot = LogContext.GetPropertiesSnapshot();
            Assert.Equal("outer", outerSnapshot["component"]);
            Assert.Equal("1001", outerSnapshot["order_id"]);
            Assert.False(outerSnapshot.ContainsKey("item_id"));
        }
    }

    [Fact]
    public void EnsureCorrelationId_ProvidesUsableCorrelationValue()
    {
        var previous = LogContext.CorrelationId;
        using (LogContext.BeginCorrelationScope(string.Empty))
        {
            var value = LogContext.EnsureCorrelationId();
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.Equal(value, LogContext.CorrelationId);
            if (string.IsNullOrWhiteSpace(previous))
                Assert.StartsWith("replica-", value, StringComparison.Ordinal);
        }

        Assert.Equal(previous, LogContext.CorrelationId);
    }
}
