using Replica.Api.Application.Orders.Commands;
using Replica.Api.Application.Users.Commands;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Xunit;

namespace Replica.VerifyTests;

public sealed class MediatRWriteCommandHandlersTests
{
    [Fact]
    public async Task CreateOrderCommandHandler_WhenUsingInMemoryStore_ReturnsSuccess()
    {
        var store = new InMemoryLanOrderStore();
        var handler = new CreateOrderCommandHandler(store);

        var result = await handler.Handle(
            new CreateOrderCommand(
                new CreateOrderRequest { OrderNumber = "MDR-1001" },
                Actor: "Administrator",
                IdempotencyKey: ""),
            cancellationToken: default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Order);
        Assert.Equal("MDR-1001", result.Order!.OrderNumber);
    }

    [Fact]
    public async Task DeleteOrderCommandHandler_WhenOrderExists_ReturnsSuccess()
    {
        var store = new InMemoryLanOrderStore();
        var created = store.CreateOrder(new CreateOrderRequest
        {
            OrderNumber = "MDR-1002"
        }, actor: "Administrator");
        var handler = new DeleteOrderCommandHandler(store);

        var result = await handler.Handle(
            new DeleteOrderCommand(
                created.InternalId,
                new DeleteOrderRequest { ExpectedVersion = created.Version },
                Actor: "Administrator",
                IdempotencyKey: ""),
            cancellationToken: default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Order);
        Assert.Equal(created.InternalId, result.Order!.InternalId);
    }

    [Fact]
    public async Task UpsertUserCommandHandler_WhenActorIsOperator_ReturnsBadRequest()
    {
        var store = new InMemoryLanOrderStore();
        var handler = new UpsertUserCommandHandler(store);

        var result = await handler.Handle(
            new UpsertUserCommand(
                new UpsertUserRequest
                {
                    Name = "new-admin",
                    Role = ReplicaApiRoles.Admin,
                    IsActive = true
                },
                Actor: "Operator 1"),
            cancellationToken: default);

        Assert.True(result.IsBadRequest);
        Assert.Equal("actor role is not allowed", result.Error);
    }
}
