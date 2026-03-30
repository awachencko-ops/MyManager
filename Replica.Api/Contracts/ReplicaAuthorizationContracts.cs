using Microsoft.AspNetCore.Mvc.Filters;

namespace Replica.Api.Contracts;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ReplicaAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    private readonly Replica.Api.Infrastructure.ReplicaAuthorizeAttribute _inner;

    public ReplicaAuthorizeAttribute(params string[] roles)
    {
        _inner = new Replica.Api.Infrastructure.ReplicaAuthorizeAttribute(roles);
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        _inner.OnAuthorization(context);
    }
}

public static class ReplicaApiRoleNames
{
    public const string Admin = Replica.Api.Infrastructure.ReplicaApiRoles.Admin;
    public const string Operator = Replica.Api.Infrastructure.ReplicaApiRoles.Operator;

    public static string Normalize(string? role)
    {
        return Replica.Api.Infrastructure.ReplicaApiRoles.Normalize(role);
    }

    public static bool IsInRole(string actualRole, string requiredRole)
    {
        return Replica.Api.Infrastructure.ReplicaApiRoles.IsInRole(actualRole, requiredRole);
    }
}
