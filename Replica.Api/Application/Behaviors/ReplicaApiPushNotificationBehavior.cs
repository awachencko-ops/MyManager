using MediatR;
using Replica.Api.Application.Orders.Commands;
using Replica.Api.Application.Users.Commands;
using Replica.Api.Infrastructure;
using Replica.Api.Services;

namespace Replica.Api.Application.Behaviors;

public sealed class ReplicaApiPushNotificationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IReplicaOrderPushPublisher _publisher;

    public ReplicaApiPushNotificationBehavior(IReplicaOrderPushPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();
        await TryPublishAsync(request, response, cancellationToken);
        return response;
    }

    private async Task TryPublishAsync(TRequest request, TResponse response, CancellationToken cancellationToken)
    {
        switch (response)
        {
            case StoreOperationResult storeResult when storeResult.IsSuccess && storeResult.Order != null:
            {
                if (request is DeleteOrderCommand)
                {
                    await _publisher.PublishOrderDeletedAsync(storeResult.Order.InternalId, cancellationToken);
                    return;
                }

                await _publisher.PublishOrderUpdatedAsync(storeResult.Order.InternalId, cancellationToken);
                return;
            }
            case UserOperationResult userResult when userResult.IsSuccess && request is UpsertUserCommand:
                await _publisher.PublishForceRefreshAsync("users-changed", cancellationToken);
                return;
        }
    }
}
