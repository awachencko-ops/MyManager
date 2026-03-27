using Replica.Api.Application.Orders.Queries;
using Replica.Api.Application.Users.Queries;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Xunit;

namespace Replica.VerifyTests;

public sealed class MediatRReadQueryHandlersTests
{
    [Fact]
    public async Task GetOrdersQueryHandler_ReturnsCreatedOrder()
    {
        var store = new InMemoryLanOrderStore();
        store.CreateOrder(new CreateOrderRequest
        {
            OrderNumber = "MDR-RQ-1001",
            CreatedByUser = "operator-a",
            CreatedById = "operator-a"
        }, actor: "operator-a");

        var handler = new GetOrdersQueryHandler(store);
        var result = await handler.Handle(new GetOrdersQuery("operator-a"), cancellationToken: default);

        Assert.Single(result);
        Assert.Equal("MDR-RQ-1001", result[0].OrderNumber);
    }

    [Fact]
    public async Task GetOrderByIdQueryHandler_WhenOrderMissing_ReturnsNull()
    {
        var store = new InMemoryLanOrderStore();
        var handler = new GetOrderByIdQueryHandler(store);

        var result = await handler.Handle(new GetOrderByIdQuery("missing"), cancellationToken: default);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetUsersQueryHandler_IncludeInactive_ReturnsInactiveUser()
    {
        var store = new InMemoryLanOrderStore();
        var upsert = store.UpsertUser(new UpsertUserRequest
        {
            Name = "disabled-rq",
            Role = ReplicaApiRoles.Operator,
            IsActive = false
        }, actor: "Administrator");
        Assert.True(upsert.IsSuccess);

        var handler = new GetUsersQueryHandler(store);
        var result = await handler.Handle(new GetUsersQuery(IncludeInactive: true), cancellationToken: default);

        Assert.Contains(result, user => user.Name == "disabled-rq" && !user.IsActive);
    }
}

