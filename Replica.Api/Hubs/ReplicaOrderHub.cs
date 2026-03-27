using Microsoft.AspNetCore.SignalR;

namespace Replica.Api.Hubs;

public sealed class ReplicaOrderHub : Hub
{
}

public static class ReplicaOrderHubEvents
{
    public const string OrderUpdated = "OrderUpdated";
    public const string OrderDeleted = "OrderDeleted";
    public const string ForceRefresh = "ForceRefresh";
}
