using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Replica.Api.Services;

namespace Replica.Api.Infrastructure;

public sealed class ReplicaApiCurrentUserContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ReplicaApiCurrentUserContextMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public ReplicaApiCurrentUserContextMiddleware(
        RequestDelegate next,
        ILogger<ReplicaApiCurrentUserContextMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context, ILanOrderStore store, IReplicaApiTokenService tokenService)
    {
        var authMode = ReplicaApiAuthConfiguration.ResolveMode(_configuration);
        var currentUser = ReplicaApiCurrentUserContext.Resolve(
            context.Request,
            store.GetUsers(),
            strictActorValidation: ReplicaApiAuthConfiguration.IsStrict(authMode),
            tokenService,
            _logger);

        ReplicaApiCurrentUserContext.Set(context, currentUser);
        await _next(context);
    }
}

public static class ReplicaApiCurrentUserContextMiddlewareExtensions
{
    public static IApplicationBuilder UseReplicaApiCurrentUserContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ReplicaApiCurrentUserContextMiddleware>();
    }
}
