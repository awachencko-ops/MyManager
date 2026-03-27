using Microsoft.AspNetCore.Mvc;
using Replica.Api.Controllers;
using Replica.Api.Infrastructure;

namespace Replica.VerifyTests;

public sealed class DiagnosticsControllerTests
{
    [Fact]
    public void GetPushDiagnostics_ReturnsCurrentPushSnapshot()
    {
        var controller = new DiagnosticsController();
        var before = ReplicaApiObservability.GetSnapshot();

        ReplicaApiObservability.RecordPushPublished("OrderUpdated");
        ReplicaApiObservability.RecordPushPublishFailure("ForceRefresh");

        var actionResult = controller.GetPushDiagnostics();
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<PushDiagnosticsDto>(ok.Value);

        Assert.True(payload.PublishedTotal >= before.PushPublishedTotal + 1);
        Assert.True(payload.PublishFailuresTotal >= before.PushPublishFailuresTotal + 1);
        Assert.True(payload.OrderUpdatedPublished >= before.PushOrderUpdatedPublished + 1);
        Assert.True(payload.ForceRefreshFailures >= before.PushForceRefreshFailures + 1);
    }
}
