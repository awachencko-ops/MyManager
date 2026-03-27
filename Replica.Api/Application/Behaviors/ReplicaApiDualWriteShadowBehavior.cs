using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Replica.Api.Application.Abstractions;
using Replica.Api.Infrastructure;
using Replica.Api.Services;

namespace Replica.Api.Application.Behaviors;

public sealed class ReplicaApiDualWriteShadowBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IOptions<ReplicaApiMigrationOptions> _options;
    private readonly IReplicaApiHistoryShadowWriter _shadowWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ReplicaApiDualWriteShadowBehavior<TRequest, TResponse>> _logger;

    public ReplicaApiDualWriteShadowBehavior(
        IOptions<ReplicaApiMigrationOptions> options,
        IReplicaApiHistoryShadowWriter shadowWriter,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ReplicaApiDualWriteShadowBehavior<TRequest, TResponse>> logger)
    {
        _options = options;
        _shadowWriter = shadowWriter;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IReplicaApiWriteCommand writeCommand)
            return await next();

        var response = await next();
        if (!ShouldMirrorToShadow(response))
            return response;

        var options = ReplicaApiMigrationConfiguration.Normalize(_options.Value);
        if (!options.DualWriteEnabled)
            return response;

        var context = new ReplicaApiHistoryShadowWriteContext(
            CommandName: writeCommand.CommandName,
            Actor: ResolveActor(request),
            CorrelationId: _httpContextAccessor.HttpContext?.TraceIdentifier ?? string.Empty);
        var writeResult = await _shadowWriter.TryWriteAsync(context, cancellationToken);
        if (writeResult.IsSuccess)
            return response;

        if (ReplicaApiMigrationShadowWriteFailurePolicies.IsFailCommand(options.ShadowWriteFailurePolicy))
        {
            throw new InvalidOperationException(
                $"shadow mirror write failed for command '{writeCommand.CommandName}': {writeResult.Error}");
        }

        _logger.LogWarning(
            "MIGRATION | shadow-write-warn-only | command={Command} | actor={Actor} | error={Error}",
            writeCommand.CommandName,
            context.Actor,
            writeResult.Error);
        return response;
    }

    private static bool ShouldMirrorToShadow(object? response)
    {
        return response is StoreOperationResult storeResult && storeResult.IsSuccess;
    }

    private static string ResolveActor(TRequest request)
    {
        var actorProperty = request?.GetType().GetProperty("Actor");
        if (actorProperty?.PropertyType != typeof(string))
            return string.Empty;

        var value = actorProperty.GetValue(request) as string;
        return value?.Trim() ?? string.Empty;
    }
}
