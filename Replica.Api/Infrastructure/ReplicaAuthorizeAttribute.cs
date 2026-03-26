using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Replica.Api.Infrastructure;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ReplicaAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _roles;

    public ReplicaAuthorizeAttribute(params string[] roles)
    {
        _roles = roles ?? Array.Empty<string>();
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var currentUser = ReplicaApiCurrentUserContext.Get(context.HttpContext);
        if (currentUser.HasFailure)
        {
            context.Result = currentUser.FailureStatusCode == StatusCodes.Status401Unauthorized
                ? new UnauthorizedObjectResult(new { error = currentUser.FailureMessage })
                : new ObjectResult(new { error = currentUser.FailureMessage }) { StatusCode = currentUser.FailureStatusCode };
            return;
        }

        if (!currentUser.IsAuthenticated)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "X-Current-User header is required" });
            return;
        }

        if (_roles.Length == 0)
            return;

        if (_roles.Any(role => ReplicaApiRoles.IsInRole(currentUser.Role, role)))
            return;

        context.Result = new ObjectResult(new { error = "actor role is not allowed" })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
