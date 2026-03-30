using System.Collections.Generic;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Application.Abstractions;
using Replica.Api.Application.Orders.Commands;
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
            strictActorValidation: true,
            tokenService: null);

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
            strictActorValidation: true,
            tokenService: null);

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
            strictActorValidation: true,
            tokenService: null);

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
            strictActorValidation: false,
            tokenService: null);

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
            strictActorValidation: true,
            tokenService: null);

        Assert.False(currentUser.HasFailure);
        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal("Andrew", currentUser.Name);
    }

    [Fact]
    public void Resolve_WithBearerToken_UsesTokenIdentity()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = "Bearer rpl1.session1.secret1";

        var tokenService = new StubTokenService(token =>
        {
            return string.Equals(token, "rpl1.session1.secret1", StringComparison.Ordinal)
                ? ReplicaApiTokenValidationResult.Success("Administrator", ReplicaApiRoles.Admin, "session1")
                : ReplicaApiTokenValidationResult.Invalid("invalid token");
        });

        var currentUser = ReplicaApiCurrentUserContext.Resolve(
            httpContext.Request,
            knownUsers:
            [
                new SharedUser { Name = "Administrator", Role = ReplicaApiRoles.Admin, IsActive = true }
            ],
            strictActorValidation: true,
            tokenService: tokenService);

        Assert.False(currentUser.HasFailure);
        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal("Administrator", currentUser.Name);
        Assert.Equal(ReplicaApiRoles.Admin, currentUser.Role);
        Assert.Equal("Bearer", currentUser.AuthScheme);
        Assert.Equal("session1", currentUser.SessionId);
    }

    [Fact]
    public void Resolve_WithInvalidBearerToken_ReturnsUnauthorized()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = "Bearer rpl1.invalid.secret";

        var tokenService = new StubTokenService(_ => ReplicaApiTokenValidationResult.Invalid("invalid token"));

        var currentUser = ReplicaApiCurrentUserContext.Resolve(
            httpContext.Request,
            knownUsers:
            [
                new SharedUser { Name = "operator1", Role = ReplicaApiRoles.Operator, IsActive = true }
            ],
            strictActorValidation: true,
            tokenService: tokenService);

        Assert.True(currentUser.HasFailure);
        Assert.Equal(StatusCodes.Status401Unauthorized, currentUser.FailureStatusCode);
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

        var filter = new Replica.Api.Infrastructure.ReplicaAuthorizeAttribute(ReplicaApiRoles.Admin);
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

        var filter = new Replica.Api.Infrastructure.ReplicaAuthorizeAttribute(ReplicaApiRoles.Operator);
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
        var controller = CreateController(store, actorName: "operator3");

        var result = controller.CreateOrder(new CreateOrderRequest { OrderNumber = "1004" });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var order = Assert.IsType<SharedOrder>(created.Value);
        Assert.Equal("operator3", store.LastActor);
        Assert.Equal("operator3", order.CreatedByUser);
        Assert.Equal("operator3", order.CreatedById);
    }

    private static OrdersController CreateController(StubLanOrderStore store, string actorName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILanOrderStore>(store);
        services.AddMediatR(typeof(CreateOrderCommand).Assembly);
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        return new OrdersController(mediator, new StubCurrentActorAccessor(actorName))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = serviceProvider
                }
            }
        };
    }

    private sealed class StubCurrentActorAccessor : IReplicaApiCurrentActorAccessor
    {
        private readonly string _actorName;

        public StubCurrentActorAccessor(string actorName)
        {
            _actorName = actorName;
        }

        public string GetCurrentActorName()
        {
            return _actorName;
        }
    }

    private sealed class StubLanOrderStore : ILanOrderStore
    {
        public string LastActor { get; private set; } = string.Empty;

        public IReadOnlyList<SharedUser> GetUsers(bool includeInactive = false) => [];

        public UserOperationResult UpsertUser(UpsertUserRequest request, string actor)
        {
            throw new NotImplementedException();
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

    private sealed class StubTokenService : IReplicaApiTokenService
    {
        private readonly Func<string, ReplicaApiTokenValidationResult> _validate;

        public StubTokenService(Func<string, ReplicaApiTokenValidationResult> validate)
        {
            _validate = validate;
        }

        public ReplicaApiTokenIssueResult IssueToken(string userName, string role, string issuedBy, string ipAddress, string userAgent)
        {
            return ReplicaApiTokenIssueResult.Failed("not implemented");
        }

        public ReplicaApiTokenValidationResult ValidateToken(string rawToken)
        {
            return _validate(rawToken);
        }

        public ReplicaApiTokenIssueResult RefreshToken(string sessionId, string requestedBy, string ipAddress, string userAgent)
        {
            return ReplicaApiTokenIssueResult.Failed("not implemented");
        }

        public ReplicaApiTokenRevokeResult RevokeToken(string sessionId, string requestedBy, string ipAddress, string userAgent)
        {
            return ReplicaApiTokenRevokeResult.Failed("not implemented");
        }
    }
}
