using MediatR;
using Microsoft.Extensions.Options;
using Replica.Api.Application.Abstractions;

namespace Replica.Api.Application.Behaviors;

// Transaction boundary for write commands at mediator level.
// We intentionally avoid ambient TransactionScope because stores already
// manage DB transactions explicitly (EfCore/Npgsql).
public sealed class ReplicaApiCommandTransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly SemaphoreSlim WriteGate = new(1, 1);
    private readonly ReplicaApiCommandPipelineOptions _options;

    public ReplicaApiCommandTransactionBehavior(IOptions<ReplicaApiCommandPipelineOptions> options)
    {
        _options = options.Value;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IReplicaApiWriteCommand)
            return await next();
        if (!_options.EnableSerializedWriteGate)
            return await next();

        await WriteGate.WaitAsync(cancellationToken);
        try
        {
            return await next();
        }
        finally
        {
            WriteGate.Release();
        }
    }
}
