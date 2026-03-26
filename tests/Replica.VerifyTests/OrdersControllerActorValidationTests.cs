using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Replica.Api.Contracts;
using Replica.Api.Controllers;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Replica.Shared;
using Replica.Shared.Models;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrdersControllerActorValidationTests
{
    [Fact]
    public void Resolve_WithoutActorHeader_ReturnsUnauthorized()
    {
        var httpContext = new DefaultHttpContext();

        var currentUser = ReplicaApiCurrentUserContext.Resolve(
            httpContext.Request,
            knownUsers: [],
            strictActorValidation: true);

        Assert.True(currentUser.HasFailure);
        Assert.Equal(StatusCodes.Status401Unauthorized, currentUser.FailureStatusCode);
    }

    [Fact]
    public void Resolve_WithUnknownActorAndKnownUsers_Strict_ReturnsForbidden()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[CurrentUserHeaderCodec.HeaderName] = "operator2";

        var currentUser = ReplicaApiCurrentUserContext.Resolve(
            httpContext.Request,
            knownUsers:
            [
                new SharedUser { Name = "operator1", Role = ReplicaApiRoles.Operator, IsActive = true }
            ],
            strictActorValidation: true);

        Assert.True(currentUser.HasFailure);
        Assert.Equal(StatusCodes.Status403Forbidden, currentUser.FailureStatusCode);
    }

    [Fact]
    public void Resolve_WithEncodedActorHeader_UsesDecodedActorAndRole()
    {
        var actorName = "\u0421\u0435\u0440\u0433\u0435\u0439";
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[CurrentUserHeaderCodec.HeaderName] = CurrentUserHeaderCodec.BuildAsciiFallback(actorName);
        httpContext.Request.Headers[CurrentUserHeaderCodec.EncodedHeaderName] = CurrentUserHeaderCodec.Encode(actorName);

        var currentUser = ReplicaApiCurrentUserContext.Resolve(
            httpContext.Request,
            knownUsers:
            [
                new SharedUser { Name = actorName, Role = ReplicaApiRoles.Admin, IsActive = true }
            ],
            strictActorValidation: true);

        Assert.False(currentUser.HasFailure);
        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(actorName, currentUser.Name);
        Assert.Equal(ReplicaApiRoles.Admin, currentUser.Role);
    }

    [Fact]
    public void Resolve_WithUnknownActor_NonStrict_AssignsOperatorRole()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[CurrentUserHeaderCodec.HeaderName] = "operator2";

        var currentUser = ReplicaApiCurrentUserContext.Resolve(
            httpContext.Request,
            knownUsers:
            [
                new SharedUser { Name = "operator1", Role = ReplicaApiRoles.Operator, IsActive = true }
            ],
            strictActorValidation: false);

        Assert.False(currentUser.HasFailure);
        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal("operator2", currentUser.Name);
        Assert.Equal(ReplicaApiRoles.Operator, currentUser.Role);
        Assert.False(currentUser.IsValidated);
    }

    [Fact]
    public void Resolve_WithLegacyBootstrapUser_AllowsActor()
    {
        var bootstrapUser = "\u0421\u0435\u0440\u0432\u0435\u0440 \"\u0422\u0430\u0443\u0434\u0435\u043c\u0438\"";
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[CurrentUserHeaderCodec.HeaderName] = "Andrew";

        var currentUser = ReplicaApiCurrentUserContext.Resolve(
            httpContext.Request,
            knownUsers:
            [
                new SharedUser { Name = bootstrapUser, Role = ReplicaApiRoles.Operator, IsActive = true }
            ],
            strictActorValidation: true);

        Assert.False(currentUser.HasFailure);
        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal("Andrew", currentUser.Name);
    }

    [Fact]
    public void ReplicaAuthorize_AdminEndpoint_RejectsOperator()
    {
        var httpContext = new DefaultHttpContext();
        ReplicaApiCurrentUserContext.Set(httpContext, new ReplicaApiCurrentUser
        {
            Name = "operator1",
            Role = ReplicaApiRoles.Operator,
            IsAuthenticated = true,
            IsValidated = true
        });

        var filter = new ReplicaAuthorizeAttribute(ReplicaApiRoles.Admin);
        var context = new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());

        filter.OnAuthorization(context);

        var forbidden = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public void ReplicaAuthorize_OperatorEndpoint_AllowsAdmin()
    {
        var httpContext = new DefaultHttpContext();
        ReplicaApiCurrentUserContext.Set(httpContext, new ReplicaApiCurrentUser
        {
            Name = "Administrator",
            Role = ReplicaApiRoles.Admin,
            IsAuthenticated = true,
            IsValidated = true
        });

        var filter = new ReplicaAuthorizeAttribute(ReplicaApiRoles.Operator);
        var context = new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());

        filter.OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void CreateOrder_UsesActorFromCurrentUserContext()
    {
        var store = new StubLanOrderStore();
        var controller = CreateController(store);
        ReplicaApiCurrentUserContext.Set(controller.ControllerContext.HttpContext, new ReplicaApiCurrentUser
        {
            Name = "operator3",
            Role = ReplicaApiRoles.Operator,
            IsAuthenticated = true,
            IsValidated = true
        });

        var result = controller.CreateOrder(new CreateOrderRequest { OrderNumber = "1004" });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var order = Assert.IsType<SharedOrder>(created.Value);
        Assert.Equal("operator3", store.LastActor);
        Assert.Equal("operator3", order.CreatedByUser);
        Assert.Equal("operator3", order.CreatedById);
    }

    private static OrdersController CreateController(StubLanOrderStore store)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        return new OrdersController(store, NullLogger<OrdersController>.Instance, configuration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private sealed class StubLanOrderStore : ILanOrderStore
    {
        public string LastActor { get; private set; } = string.Empty;

        public IReadOnlyList<SharedUser> GetUsers(bool includeInactive = false) => [];

        public UserOperationResult UpsertUser(UpsertUserRequest request, string actor)
        {
            throw new System.NotImplementedException();
        }

        public IReadOnlyList<SharedOrder> GetOrders(string createdBy)
        {
            return [];
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
            throw new System.NotImplementedException();
        }

        public StoreOperationResult TryDeleteOrder(string orderId, DeleteOrderRequest request, string actor)
        {
            throw new System.NotImplementedException();
        }

        public StoreOperationResult TryAddItem(string orderId, AddOrderItemRequest request, string actor)
        {
            throw new System.NotImplementedException();
        }

        public StoreOperationResult TryUpdateItem(string orderId, string itemId, UpdateOrderItemRequest request, string actor)
        {
            throw new System.NotImplementedException();
        }

        public StoreOperationResult TryDeleteItem(string orderId, string itemId, DeleteOrderItemRequest request, string actor)
        {
            throw new System.NotImplementedException();
        }

        public StoreOperationResult TryReorderItems(string orderId, ReorderOrderItemsRequest request, string actor)
        {
            throw new System.NotImplementedException();
        }

        public StoreOperationResult TryStartRun(string orderId, RunOrderRequest request, string actor)
        {
            throw new System.NotImplementedException();
        }

        public StoreOperationResult TryStopRun(string orderId, StopOrderRequest request, string actor)
        {
            throw new System.NotImplementedException();
        }
    }
}
