using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Replica.Api.Contracts;
using Replica.Api.Controllers;
using Replica.Api.Services;
using Replica.Shared;
using Replica.Shared.Models;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrdersControllerActorValidationTests
{
    [Fact]
    public void CreateOrder_WithoutActorHeader_ReturnsUnauthorized()
    {
        var store = new StubLanOrderStore();
        var controller = CreateController(store);

        var result = controller.CreateOrder(new CreateOrderRequest { OrderNumber = "1001" });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public void CreateOrder_WithUnknownActorAndKnownUsers_ReturnsForbidden()
    {
        var store = new StubLanOrderStore();
        store.Users.Add(new SharedUser { Name = "operator1", IsActive = true });
        var controller = CreateController(store, actor: "operator2", strictActorValidation: true);

        var result = controller.CreateOrder(new CreateOrderRequest { OrderNumber = "1002" });

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public void CreateOrder_WithInactiveActor_ReturnsForbidden()
    {
        var store = new StubLanOrderStore();
        store.Users.Add(new SharedUser { Name = "operator1", IsActive = false });
        var controller = CreateController(store, actor: "operator1", strictActorValidation: true);

        var result = controller.CreateOrder(new CreateOrderRequest { OrderNumber = "1003" });

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public void CreateOrder_WithHeaderAndEmptyUsers_AllowsAndUsesActor()
    {
        var store = new StubLanOrderStore();
        var controller = CreateController(store, actor: "operator3");

        var result = controller.CreateOrder(new CreateOrderRequest { OrderNumber = "1004" });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var order = Assert.IsType<SharedOrder>(created.Value);
        Assert.Equal("operator3", store.LastActor);
        Assert.Equal("operator3", order.CreatedByUser);
        Assert.Equal("operator3", order.CreatedById);
    }

    [Fact]
    public void CreateOrder_WithEncodedActorHeader_AllowsAndUsesDecodedActor()
    {
        var store = new StubLanOrderStore();
        var controller = CreateController(store, encodedActor: "Сергей");

        var result = controller.CreateOrder(new CreateOrderRequest { OrderNumber = "1005" });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var order = Assert.IsType<SharedOrder>(created.Value);
        Assert.Equal("Сергей", store.LastActor);
        Assert.Equal("Сергей", order.CreatedByUser);
        Assert.Equal("Сергей", order.CreatedById);
    }

    [Fact]
    public void CreateOrder_WithBothHeaders_PrefersDecodedActor()
    {
        var store = new StubLanOrderStore();
        var controller = CreateController(store, actor: CurrentUserHeaderCodec.BuildAsciiFallback("Сергей"), encodedActor: "Сергей");

        var result = controller.CreateOrder(new CreateOrderRequest { OrderNumber = "1005A" });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var order = Assert.IsType<SharedOrder>(created.Value);
        Assert.Equal("Сергей", store.LastActor);
        Assert.Equal("Сергей", order.CreatedByUser);
        Assert.Equal("Сергей", order.CreatedById);
    }

    [Fact]
    public void CreateOrder_WithLegacyBootstrapUser_AllowsNewActor()
    {
        var store = new StubLanOrderStore();
        store.Users.Add(new SharedUser { Name = "Сервер \"Таудеми\"", IsActive = true });
        var controller = CreateController(store, actor: "Andrew");

        var result = controller.CreateOrder(new CreateOrderRequest { OrderNumber = "1006" });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var order = Assert.IsType<SharedOrder>(created.Value);
        Assert.Equal("Andrew", store.LastActor);
        Assert.Equal("Andrew", order.CreatedByUser);
        Assert.Equal("Andrew", order.CreatedById);
    }

    [Fact]
    public void CreateOrder_WithUnknownActorAndKnownUsers_NonStrict_Allows()
    {
        var store = new StubLanOrderStore();
        store.Users.Add(new SharedUser { Name = "operator1", IsActive = true });
        var controller = CreateController(store, actor: "operator2", strictActorValidation: false);

        var result = controller.CreateOrder(new CreateOrderRequest { OrderNumber = "1007" });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var order = Assert.IsType<SharedOrder>(created.Value);
        Assert.Equal("operator2", store.LastActor);
        Assert.Equal("operator2", order.CreatedByUser);
        Assert.Equal("operator2", order.CreatedById);
    }

    private static OrdersController CreateController(
        StubLanOrderStore store,
        string? actor = null,
        string? encodedActor = null,
        bool strictActorValidation = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicaApi:StrictActorValidation"] = strictActorValidation ? "true" : "false"
            })
            .Build();

        var controller = new OrdersController(store, NullLogger<OrdersController>.Instance, configuration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        if (!string.IsNullOrWhiteSpace(actor))
            controller.ControllerContext.HttpContext.Request.Headers[CurrentUserHeaderCodec.HeaderName] = actor;
        if (!string.IsNullOrWhiteSpace(encodedActor))
            controller.ControllerContext.HttpContext.Request.Headers[CurrentUserHeaderCodec.EncodedHeaderName] = CurrentUserHeaderCodec.Encode(encodedActor);

        return controller;
    }

    private sealed class StubLanOrderStore : ILanOrderStore
    {
        public List<SharedUser> Users { get; } = new();
        public string LastActor { get; private set; } = string.Empty;

        public IReadOnlyList<SharedUser> GetUsers() => Users;

        public IReadOnlyList<SharedOrder> GetOrders(string createdBy)
        {
            return Array.Empty<SharedOrder>();
        }

        public bool TryGetOrder(string orderId, out SharedOrder order)
        {
            order = null!;
            return false;
        }

        public SharedOrder CreateOrder(CreateOrderRequest request, string actor)
        {
            LastActor = actor;
            return new SharedOrder
            {
                InternalId = "order-1",
                OrderNumber = request.OrderNumber,
                CreatedByUser = request.CreatedByUser,
                CreatedById = request.CreatedById,
                UserName = request.UserName,
                Version = 1
            };
        }

        public StoreOperationResult TryUpdateOrder(string orderId, UpdateOrderRequest request, string actor)
        {
            throw new NotImplementedException();
        }

        public StoreOperationResult TryDeleteOrder(string orderId, DeleteOrderRequest request, string actor)
        {
            throw new NotImplementedException();
        }

        public StoreOperationResult TryAddItem(string orderId, AddOrderItemRequest request, string actor)
        {
            throw new NotImplementedException();
        }

        public StoreOperationResult TryUpdateItem(string orderId, string itemId, UpdateOrderItemRequest request, string actor)
        {
            throw new NotImplementedException();
        }

        public StoreOperationResult TryDeleteItem(string orderId, string itemId, DeleteOrderItemRequest request, string actor)
        {
            throw new NotImplementedException();
        }

        public StoreOperationResult TryReorderItems(string orderId, ReorderOrderItemsRequest request, string actor)
        {
            throw new NotImplementedException();
        }

        public StoreOperationResult TryStartRun(string orderId, RunOrderRequest request, string actor)
        {
            throw new NotImplementedException();
        }

        public StoreOperationResult TryStopRun(string orderId, StopOrderRequest request, string actor)
        {
            throw new NotImplementedException();
        }
    }
}
