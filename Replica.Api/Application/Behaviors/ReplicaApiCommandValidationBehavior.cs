using MediatR;
using Replica.Api.Application.Abstractions;
using Replica.Api.Services;

namespace Replica.Api.Application.Behaviors;

public sealed class ReplicaApiCommandValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IReplicaApiCommandValidator<TRequest>> _validators;

    public ReplicaApiCommandValidationBehavior(IEnumerable<IReplicaApiCommandValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        foreach (var validator in _validators)
        {
            if (validator.TryValidate(request, out var error))
                continue;

            return BuildValidationFailure(error);
        }

        return await next();
    }

    private static TResponse BuildValidationFailure(string error)
    {
        var normalizedError = string.IsNullOrWhiteSpace(error) ? "validation failed" : error.Trim();
        if (typeof(TResponse) == typeof(StoreOperationResult))
            return (TResponse)(object)StoreOperationResult.BadRequest(normalizedError);

        if (typeof(TResponse) == typeof(UserOperationResult))
            return (TResponse)(object)UserOperationResult.BadRequest(normalizedError);

        throw new InvalidOperationException(
            $"Unsupported validation response type '{typeof(TResponse).Name}' for command pipeline.");
    }
}
