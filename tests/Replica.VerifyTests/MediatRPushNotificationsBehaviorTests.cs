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

public sealed class MediatRPushNotificationsBehaviorTests
{
    [Fact]
    public async Task CreateOrder_WhenSuccessful_PublishesOrderUpdated()
    {
        var publisher = new ProbeOrderPushPublisher();
        var mediator = BuildMediator(publisher);

        var result = await mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest { OrderNumber = "MDR-PUSH-1001" },
            Actor: "Administrator",
            IdempotencyKey: ""));

        Assert.True(result.IsSuccess);
        Assert.Single(publisher.UpdatedOrderIds);
        Assert.Empty(publisher.DeletedOrderIds);
    }

    [Fact]
    public async Task DeleteOrder_WhenSuccessful_PublishesOrderDeleted()
    {
        var publisher = new ProbeOrderPushPublisher();
        var mediator = BuildMediator(publisher);

        var created = await mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest { OrderNumber = "MDR-PUSH-1002" },
            Actor: "Administrator",
            IdempotencyKey: ""));
        Assert.True(created.IsSuccess);

        var deleted = await mediator.Send(new DeleteOrderCommand(
            created.Order!.InternalId,
            new DeleteOrderRequest { ExpectedVersion = created.Order.Version },
            Actor: "Administrator",
            IdempotencyKey: ""));

        Assert.True(deleted.IsSuccess);
        Assert.Contains(created.Order.InternalId, publisher.DeletedOrderIds);
    }

    [Fact]
    public async Task UpsertUser_WhenSuccessful_PublishesForceRefresh()
    {
        var publisher = new ProbeOrderPushPublisher();
        var mediator = BuildMediator(publisher);

        var result = await mediator.Send(new UpsertUserCommand(
            new UpsertUserRequest
            {
                Name = "push-operator",
                Role = ReplicaApiRoles.Operator,
                IsActive = true
            },
            Actor: "Administrator"));

        Assert.True(result.IsSuccess);
        Assert.Contains("users-changed", publisher.ForceRefreshReasons);
    }

    private static IMediator BuildMediator(ProbeOrderPushPublisher publisher)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILanOrderStore, InMemoryLanOrderStore>();
        services.AddSingleton<IReplicaOrderPushPublisher>(publisher);
        services.AddMediatR(typeof(CreateOrderCommand).Assembly);
        services.AddReplicaApiCommandPipeline();

        return services
            .BuildServiceProvider()
            .GetRequiredService<IMediator>();
    }

    private sealed class ProbeOrderPushPublisher : IReplicaOrderPushPublisher
    {
        public List<string> UpdatedOrderIds { get; } = [];
        public List<string> DeletedOrderIds { get; } = [];
        public List<string> ForceRefreshReasons { get; } = [];

        public Task PublishOrderUpdatedAsync(string orderId, CancellationToken cancellationToken)
        {
            UpdatedOrderIds.Add(orderId ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task PublishOrderDeletedAsync(string orderId, CancellationToken cancellationToken)
        {
            DeletedOrderIds.Add(orderId ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task PublishForceRefreshAsync(string reason, CancellationToken cancellationToken)
        {
            ForceRefreshReasons.Add(reason ?? string.Empty);
            return Task.CompletedTask;
        }
    }
}
