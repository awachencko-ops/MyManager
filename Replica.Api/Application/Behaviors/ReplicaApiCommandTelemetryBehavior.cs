using MediatR;
using Replica.Api.Application.Abstractions;
using Replica.Api.Infrastructure;
using Replica.Api.Services;

namespace Replica.Api.Application.Behaviors;

public sealed class ReplicaApiCommandTelemetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IReplicaApiWriteCommand writeCommand)
            return await next();

        try
        {
            var response = await next();
            ReplicaApiObservability.RecordWriteCommand(writeCommand.CommandName, ResolveResultKind(response));
            return response;
        }
        catch
        {
            ReplicaApiObservability.RecordWriteCommand(writeCommand.CommandName, "bad_request");
            throw;
        }
    }

    private static string ResolveResultKind(object? response)
    {
        return response switch
        {
            StoreOperationResult storeResult when storeResult.IsSuccess => "success",
            StoreOperationResult storeResult when storeResult.IsConflict => "conflict",
            StoreOperationResult storeResult when storeResult.IsNotFound => "not_found",
            StoreOperationResult => "bad_request",
            UserOperationResult userResult when userResult.IsSuccess => "success",
            UserOperationResult => "bad_request",
            _ => "success"
        };
    }
}
