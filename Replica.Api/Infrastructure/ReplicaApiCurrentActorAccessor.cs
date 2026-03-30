using Microsoft.AspNetCore.Http;
using Replica.Api.Application.Abstractions;

namespace Replica.Api.Infrastructure;

public sealed class ReplicaApiCurrentActorAccessor : IReplicaApiCurrentActorAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReplicaApiCurrentActorAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetCurrentActorName()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
            return string.Empty;

        return ReplicaApiCurrentUserContext.Get(context).Name;
    }
}
