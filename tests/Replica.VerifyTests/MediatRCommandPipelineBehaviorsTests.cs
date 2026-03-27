using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Application.Behaviors;
using Replica.Api.Application.Orders.Commands;
using Replica.Api.Application.Users.Commands;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Xunit;

namespace Replica.VerifyTests;

public sealed class MediatRCommandPipelineBehaviorsTests
{
    [Fact]
    public async Task PipelineValidation_WhenCreateOrderHasNoOrderNumber_ReturnsBadRequest()
    {
        var mediator = BuildMediator();

        var result = await mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest { OrderNumber = "" },
            Actor: "Administrator",
            IdempotencyKey: ""));

        Assert.True(result.IsBadRequest);
        Assert.Equal("order number is required", result.Error);
    }

    [Fact]
    public async Task PipelineValidation_WhenUpsertUserHasNoName_ReturnsBadRequest()
    {
        var mediator = BuildMediator();

        var result = await mediator.Send(new UpsertUserCommand(
            new UpsertUserRequest { Name = " ", Role = ReplicaApiRoles.Admin },
            Actor: "Administrator"));

        Assert.True(result.IsBadRequest);
        Assert.Equal("user name is required", result.Error);
    }

    [Fact]
    public async Task PipelineTelemetry_WhenCreateOrderSucceeds_RecordsWriteMetric()
    {
        var mediator = BuildMediator();
        var before = ReplicaApiObservability.GetSnapshot();
        var beforeCommandTotal = GetCommandWriteTotal(before, "create-order");

        var result = await mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest { OrderNumber = "MDR-PIPE-001" },
            Actor: "Administrator",
            IdempotencyKey: ""));

        var after = ReplicaApiObservability.GetSnapshot();
        var afterCommandTotal = GetCommandWriteTotal(after, "create-order");

        Assert.True(result.IsSuccess);
        Assert.True(after.WriteCommandsTotal >= before.WriteCommandsTotal + 1);
        Assert.True(afterCommandTotal >= beforeCommandTotal + 1);
    }

    [Fact]
    public async Task PipelineIdempotency_WhenKeyIsTooLong_ReturnsBadRequest()
    {
        var mediator = BuildMediator();
        var idempotencyKey = new string('k', 129);

        var result = await mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest { OrderNumber = "MDR-PIPE-002" },
            Actor: "Administrator",
            IdempotencyKey: idempotencyKey));

        Assert.True(result.IsBadRequest);
        Assert.Equal("idempotency key length must be <= 128", result.Error);
    }

    private static IMediator BuildMediator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILanOrderStore, InMemoryLanOrderStore>();
        services.AddMediatR(typeof(CreateOrderCommand).Assembly);
        services.AddReplicaApiCommandPipeline();

        return services
            .BuildServiceProvider()
            .GetRequiredService<IMediator>();
    }

    private static long GetCommandWriteTotal(ReplicaApiObservabilitySnapshot snapshot, string commandName)
    {
        if (!snapshot.Commands.TryGetValue(commandName, out var commandSnapshot))
            return 0;

        return commandSnapshot.WriteTotal;
    }
}
