using System.Collections.Generic;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderRunFeedbackServiceTests
{
    [Fact]
    public void BuildServerSkippedPreview_ReturnsTrimmedPreviewWithOverflowSuffix()
    {
        var service = new OrderRunFeedbackService();
        var skipped = new List<string>
        {
            "  order-1: locked  ",
            "order-2: not found",
            "order-3: version conflict"
        };

        var preview = service.BuildServerSkippedPreview(skipped, previewLimit: 2);

        Assert.Equal(
            "order-1: locked" + System.Environment.NewLine
            + "order-2: not found" + System.Environment.NewLine
            + "... ещё: 1",
            preview);
    }

    [Fact]
    public void BuildSkippedDetails_IncludesLocalAndServerReasons()
    {
        var service = new OrderRunFeedbackService();
        var runPlan = new OrderRunStateService.RunPlan(
            RunnableOrders: new List<OrderData>(),
            OrdersWithoutNumber: new List<OrderData> { new() },
            AlreadyRunningOrders: new List<OrderData> { new() });

        var details = service.BuildSkippedDetails(runPlan, new[] { "server reject 1", "server reject 2" });

        Assert.Equal("без номера: 1, уже запущены: 1, сервер отклонил: 2", details);
    }

    [Fact]
    public void BuildExecutionErrorsPreview_UsesResolverAndFallbackMessage()
    {
        var service = new OrderRunFeedbackService();
        var errors = new List<OrderRunExecutionError>
        {
            new(new OrderData { Id = "100", InternalId = "internal-100" }, "disk full"),
            new(new OrderData { Id = "200", InternalId = "internal-200" }, " ")
        };

        var preview = service.BuildExecutionErrorsPreview(errors, order => order.Id ?? string.Empty);

        Assert.Equal(
            "100: disk full" + System.Environment.NewLine
            + "200: неизвестная ошибка",
            preview);
    }

    [Fact]
    public void BuildExecutionErrorsPreview_ReturnsEmptyWhenNoErrors()
    {
        var service = new OrderRunFeedbackService();

        var preview = service.BuildExecutionErrorsPreview(new List<OrderRunExecutionError>(), _ => "order");

        Assert.Equal(string.Empty, preview);
    }
}
