using Microsoft.AspNetCore.SignalR;
using Replica.Api.Hubs;

namespace Replica.Api.Infrastructure;

public interface IReplicaOrderPushPublisher
{
    Task PublishOrderUpdatedAsync(string orderId, CancellationToken cancellationToken);
    Task PublishOrderDeletedAsync(string orderId, CancellationToken cancellationToken);
    Task PublishForceRefreshAsync(string reason, CancellationToken cancellationToken);
}

public sealed class NoOpReplicaOrderPushPublisher : IReplicaOrderPushPublisher
{
    public Task PublishOrderUpdatedAsync(string orderId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task PublishOrderDeletedAsync(string orderId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task PublishForceRefreshAsync(string reason, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class SignalRReplicaOrderPushPublisher : IReplicaOrderPushPublisher
{
    private readonly IHubContext<ReplicaOrderHub> _hubContext;
    private readonly ILogger<SignalRReplicaOrderPushPublisher> _logger;

    public SignalRReplicaOrderPushPublisher(
        IHubContext<ReplicaOrderHub> hubContext,
        ILogger<SignalRReplicaOrderPushPublisher> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishOrderUpdatedAsync(string orderId, CancellationToken cancellationToken)
    {
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedOrderId))
            return;

        try
        {
            await _hubContext.Clients.All.SendAsync(
                ReplicaOrderHubEvents.OrderUpdated,
                new
                {
                    orderId = normalizedOrderId,
                    occurredAtUtc = DateTime.UtcNow
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "failed to publish SignalR event {EventName} for order {OrderId}", ReplicaOrderHubEvents.OrderUpdated, normalizedOrderId);
        }
    }

    public async Task PublishOrderDeletedAsync(string orderId, CancellationToken cancellationToken)
    {
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedOrderId))
            return;

        try
        {
            await _hubContext.Clients.All.SendAsync(
                ReplicaOrderHubEvents.OrderDeleted,
                new
                {
                    orderId = normalizedOrderId,
                    occurredAtUtc = DateTime.UtcNow
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "failed to publish SignalR event {EventName} for order {OrderId}", ReplicaOrderHubEvents.OrderDeleted, normalizedOrderId);
        }
    }

    public async Task PublishForceRefreshAsync(string reason, CancellationToken cancellationToken)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "state-changed"
            : reason.Trim();

        try
        {
            await _hubContext.Clients.All.SendAsync(
                ReplicaOrderHubEvents.ForceRefresh,
                new
                {
                    reason = normalizedReason,
                    occurredAtUtc = DateTime.UtcNow
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "failed to publish SignalR event {EventName}", ReplicaOrderHubEvents.ForceRefresh);
        }
    }
}
