using Microsoft.AspNetCore.Http;
using Replica.Api.Application.Abstractions;

namespace Replica.Api.Infrastructure;

public sealed class ReplicaApiCurrentActorAccessor : IReplicaApiCurrentActorAccessor, IReplicaApiCurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReplicaApiCurrentActorAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetCurrentActorName()
    {
        return GetCurrentUser().Name;
    }

    public ReplicaApiCurrentUserSnapshot GetCurrentUser()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
            return new ReplicaApiCurrentUserSnapshot();

        var currentUser = ReplicaApiCurrentUserContext.Get(context);
        return new ReplicaApiCurrentUserSnapshot
        {
            Name = currentUser.Name,
            Role = currentUser.Role,
            IsAuthenticated = currentUser.IsAuthenticated,
            IsValidated = currentUser.IsValidated,
            CanManageUsers = ReplicaApiRoles.IsInRole(currentUser.Role, ReplicaApiRoles.Admin),
            AuthScheme = currentUser.AuthScheme,
            SessionId = currentUser.SessionId
        };
    }
}
