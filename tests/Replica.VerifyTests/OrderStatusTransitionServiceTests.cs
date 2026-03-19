using System;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderStatusTransitionServiceTests
{
    [Fact]
    public void Apply_UpdatesOrderAndReturnsTransition()
    {
        var service = new OrderStatusTransitionService();
        var order = new OrderData
        {
            Status = WorkflowStatusNames.Waiting,
            LastStatusSource = OrderStatusSourceNames.Ui,
            LastStatusReason = string.Empty,
            LastStatusAt = DateTime.Now.AddMinutes(-5)
        };

        var transition = service.Apply(
            order,
            WorkflowStatusNames.Processing,
            OrderStatusSourceNames.Ui,
            "manual-run");

        Assert.True(transition.Changed);
        Assert.Equal(WorkflowStatusNames.Waiting, transition.OldStatus);
        Assert.Equal(WorkflowStatusNames.Processing, transition.NewStatus);
        Assert.Equal(OrderStatusSourceNames.Ui, order.LastStatusSource);
        Assert.Equal("manual-run", order.LastStatusReason);
        Assert.Equal(WorkflowStatusNames.Processing, order.Status);
    }

    [Fact]
    public void Apply_ReturnsNotChanged_WhenNoTransition()
    {
        var service = new OrderStatusTransitionService();
        var order = new OrderData
        {
            Status = WorkflowStatusNames.Waiting,
            LastStatusSource = OrderStatusSourceNames.Ui,
            LastStatusReason = "same"
        };

        var transition = service.Apply(order, WorkflowStatusNames.Waiting, OrderStatusSourceNames.Ui, "same");

        Assert.False(transition.Changed);
    }

    [Theory]
    [InlineData("stage-1", "Найден исходный файл")]
    [InlineData("stage-2", "Найден файл подготовки")]
    [InlineData("stage-3", "Найден печатный файл")]
    public void NormalizeFileSyncReason_MapsKnownStages(string rawReason, string expected)
    {
        var normalized = OrderStatusTransitionService.NormalizeFileSyncReason(OrderStatusSourceNames.FileSync, rawReason);
        Assert.Equal(expected, normalized);
    }
}
