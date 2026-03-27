using MediatR;
using Replica.Api.Application.Abstractions;
using Replica.Api.Services;

namespace Replica.Api.Application.Behaviors;

public sealed class ReplicaApiCommandIdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private const int MaxIdempotencyKeyLength = 128;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IReplicaApiIdempotentWriteCommand idempotentCommand)
        {
            var key = idempotentCommand.IdempotencyKey?.Trim() ?? string.Empty;
            if (key.Length > MaxIdempotencyKeyLength)
                return BuildBadRequest("idempotency key length must be <= 128");
        }

        return await next();
    }

    private static TResponse BuildBadRequest(string error)
    {
        if (typeof(TResponse) == typeof(StoreOperationResult))
            return (TResponse)(object)StoreOperationResult.BadRequest(error);

        if (typeof(TResponse) == typeof(UserOperationResult))
            return (TResponse)(object)UserOperationResult.BadRequest(error);

        throw new InvalidOperationException(
            $"Unsupported idempotency response type '{typeof(TResponse).Name}' for command pipeline.");
    }
}

