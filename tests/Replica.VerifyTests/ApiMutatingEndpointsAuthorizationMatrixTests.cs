using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Replica.Api.Controllers;
using Replica.Api.Infrastructure;
using Xunit;

namespace Replica.VerifyTests;

public sealed class ApiMutatingEndpointsAuthorizationMatrixTests
{
    public static IEnumerable<object[]> MutatingEndpoints()
    {
        foreach (var endpoint in EndpointCases)
            yield return [endpoint];
    }

    [Theory]
    [MemberData(nameof(MutatingEndpoints))]
    public void MutatingEndpoint_WhenCurrentUserHasUnauthorizedFailure_Returns401(MutatingEndpointAuthCase endpoint)
    {
        var result = EvaluateAuthorization(endpoint, new ReplicaApiCurrentUser
        {
            FailureStatusCode = StatusCodes.Status401Unauthorized,
            FailureMessage = "unauthorized"
        });

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, ResolveStatusCode(result!));
    }

    [Theory]
    [MemberData(nameof(MutatingEndpoints))]
    public void MutatingEndpoint_WhenOperatorCallsAdminEndpoint_Returns403(MutatingEndpointAuthCase endpoint)
    {
        var result = EvaluateAuthorization(endpoint, new ReplicaApiCurrentUser
        {
            Name = "operator-1",
            Role = ReplicaApiRoles.Operator,
            IsAuthenticated = true,
            IsValidated = true
        });

        if (string.Equals(endpoint.RequiredRole, ReplicaApiRoles.Admin, StringComparison.Ordinal))
        {
            Assert.NotNull(result);
            Assert.Equal(StatusCodes.Status403Forbidden, ResolveStatusCode(result!));
            return;
        }

        Assert.Null(result);
    }

    [Theory]
    [MemberData(nameof(MutatingEndpoints))]
    public void MutatingEndpoint_WhenRoleIsAllowed_PassesAuthorization(MutatingEndpointAuthCase endpoint)
    {
        var requiredRole = string.Equals(endpoint.RequiredRole, ReplicaApiRoles.Admin, StringComparison.Ordinal)
            ? ReplicaApiRoles.Admin
            : ReplicaApiRoles.Operator;
        var actorName = string.Equals(requiredRole, ReplicaApiRoles.Admin, StringComparison.Ordinal)
            ? "admin-1"
            : "operator-1";

        var result = EvaluateAuthorization(endpoint, new ReplicaApiCurrentUser
        {
            Name = actorName,
            Role = requiredRole,
            IsAuthenticated = true,
            IsValidated = true
        });

        Assert.Null(result);
    }

    private static IActionResult? EvaluateAuthorization(MutatingEndpointAuthCase endpoint, ReplicaApiCurrentUser currentUser)
    {
        var method = endpoint.ResolveMethod();
        var filters = ResolveAuthorizeFilters(method);

        var httpContext = new DefaultHttpContext();
        ReplicaApiCurrentUserContext.Set(httpContext, currentUser);
        var filterContext = new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());

        foreach (var filter in filters)
        {
            filter.OnAuthorization(filterContext);
            if (filterContext.Result != null)
                break;
        }

        return filterContext.Result;
    }

    private static IReadOnlyList<ReplicaAuthorizeAttribute> ResolveAuthorizeFilters(MethodInfo method)
    {
        var filters = new List<ReplicaAuthorizeAttribute>();
        if (method.DeclaringType != null)
        {
            filters.AddRange(method.DeclaringType
                .GetCustomAttributes<ReplicaAuthorizeAttribute>(inherit: true));
        }

        filters.AddRange(method.GetCustomAttributes<ReplicaAuthorizeAttribute>(inherit: true));
        return filters;
    }

    private static int ResolveStatusCode(IActionResult actionResult)
    {
        return actionResult switch
        {
            UnauthorizedObjectResult unauthorized => unauthorized.StatusCode ?? StatusCodes.Status401Unauthorized,
            ObjectResult objectResult => objectResult.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => StatusCodes.Status200OK
        };
    }

    private static readonly MutatingEndpointAuthCase[] EndpointCases =
    [
        new(typeof(OrdersController), nameof(OrdersController.CreateOrder), ReplicaApiRoles.Operator),
        new(typeof(OrdersController), nameof(OrdersController.DeleteOrder), ReplicaApiRoles.Operator),
        new(typeof(OrdersController), nameof(OrdersController.UpdateOrder), ReplicaApiRoles.Operator),
        new(typeof(OrdersController), nameof(OrdersController.AddOrderItem), ReplicaApiRoles.Operator),
        new(typeof(OrdersController), nameof(OrdersController.UpdateOrderItem), ReplicaApiRoles.Operator),
        new(typeof(OrdersController), nameof(OrdersController.DeleteOrderItem), ReplicaApiRoles.Operator),
        new(typeof(OrdersController), nameof(OrdersController.ReorderOrderItems), ReplicaApiRoles.Operator),
        new(typeof(OrdersController), nameof(OrdersController.StartOrderRun), ReplicaApiRoles.Operator),
        new(typeof(OrdersController), nameof(OrdersController.StopOrderRun), ReplicaApiRoles.Operator),
        new(typeof(AuthController), nameof(AuthController.Login), ReplicaApiRoles.Operator),
        new(typeof(AuthController), nameof(AuthController.Refresh), ReplicaApiRoles.Operator),
        new(typeof(AuthController), nameof(AuthController.Revoke), ReplicaApiRoles.Operator),
        new(typeof(UsersController), nameof(UsersController.UpsertUser), ReplicaApiRoles.Admin)
    ];

    public sealed record MutatingEndpointAuthCase(Type ControllerType, string ActionName, string RequiredRole)
    {
        public MethodInfo ResolveMethod()
        {
            var method = ControllerType.GetMethod(ActionName, BindingFlags.Instance | BindingFlags.Public);
            Assert.True(method != null, $"{ControllerType.Name}.{ActionName} should exist");
            return method!;
        }

        public override string ToString()
        {
            return $"{ControllerType.Name}.{ActionName} ({RequiredRole})";
        }
    }
}
