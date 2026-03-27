using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Application.Behaviors;
using Replica.Api.Application.Orders.Commands;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Xunit;

namespace Replica.VerifyTests;

public sealed class ReplicaApiDualWriteIntegrationTests
{
    [Fact]
    public async Task CreateOrder_WhenDualWriteEnabled_WritesShadowSnapshotFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "replica-stage4-dualwrite-" + Guid.NewGuid().ToString("N"));
        var shadowPath = Path.Combine(tempDirectory, "history.shadow.json");

        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediator = BuildMediator(options =>
            {
                options.DualWriteEnabled = true;
                options.ShadowWriteFailurePolicy = ReplicaApiMigrationShadowWriteFailurePolicies.WarnOnly;
                options.ShadowHistoryFilePath = shadowPath;
            });

            var result = await mediator.Send(new CreateOrderCommand(
                new CreateOrderRequest { OrderNumber = "DW-INTEG-1001" },
                Actor: "Administrator",
                IdempotencyKey: string.Empty));

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Order);
            Assert.True(File.Exists(shadowPath));

            await using var stream = File.OpenRead(shadowPath);
            using var json = await JsonDocument.ParseAsync(stream);
            var root = json.RootElement;

            Assert.Equal("create-order", root.GetProperty("Command").GetString());
            Assert.Equal("Administrator", root.GetProperty("Actor").GetString());

            var orders = root.GetProperty("Orders").EnumerateArray().ToList();
            Assert.Contains(orders, element =>
                string.Equals(
                    element.GetProperty("InternalId").GetString(),
                    result.Order!.InternalId,
                    StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateOrder_WhenDualWriteDisabled_DoesNotWriteShadowSnapshotFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "replica-stage4-dualwrite-" + Guid.NewGuid().ToString("N"));
        var shadowPath = Path.Combine(tempDirectory, "history.shadow.json");

        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediator = BuildMediator(options =>
            {
                options.DualWriteEnabled = false;
                options.ShadowWriteFailurePolicy = ReplicaApiMigrationShadowWriteFailurePolicies.WarnOnly;
                options.ShadowHistoryFilePath = shadowPath;
            });

            var result = await mediator.Send(new CreateOrderCommand(
                new CreateOrderRequest { OrderNumber = "DW-INTEG-1002" },
                Actor: "Administrator",
                IdempotencyKey: string.Empty));

            Assert.True(result.IsSuccess);
            Assert.False(File.Exists(shadowPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static IMediator BuildMediator(Action<ReplicaApiMigrationOptions> configureMigration)
    {
        var services = new ServiceCollection();
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { TraceIdentifier = "dual-write-integration-correlation-id" }
        };

        services.AddSingleton<ILanOrderStore, InMemoryLanOrderStore>();
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddMediatR(typeof(CreateOrderCommand).Assembly);
        services.AddReplicaApiCommandPipeline();
        services.Configure(configureMigration);
        services.AddSingleton<IReplicaApiHistoryShadowWriter, FileReplicaApiHistoryShadowWriter>();

        return services
            .BuildServiceProvider()
            .GetRequiredService<IMediator>();
    }
}
