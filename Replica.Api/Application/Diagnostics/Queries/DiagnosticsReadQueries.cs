using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Data;
using Replica.Api.Infrastructure;
using System.Text.Json;

namespace Replica.Api.Application.Diagnostics.Queries;

public sealed record GetRecentOperationsQuery(int Limit) : IRequest<IReadOnlyList<ServerOperationReadModel>>;

public sealed class GetRecentOperationsQueryHandler : IRequestHandler<GetRecentOperationsQuery, IReadOnlyList<ServerOperationReadModel>>
{
    private readonly IServiceProvider _services;

    public GetRecentOperationsQueryHandler(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<IReadOnlyList<ServerOperationReadModel>> Handle(
        GetRecentOperationsQuery request,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(request.Limit, 1, 200);
        var dbContextFactory = _services.GetService<IDbContextFactory<ReplicaDbContext>>();
        if (dbContextFactory == null)
            return Array.Empty<ServerOperationReadModel>();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var operations = await db.OrderEvents
            .AsNoTracking()
            .OrderByDescending(x => x.EventId)
            .Take(normalizedLimit)
            .Select(x => new ServerOperationReadModel
            {
                EventId = x.EventId,
                OrderId = x.OrderInternalId,
                ItemId = x.ItemId,
                EventType = x.EventType,
                EventSource = x.EventSource,
                CreatedAtUtc = x.CreatedAt,
                PayloadJson = x.PayloadJson
            })
            .ToListAsync(cancellationToken);

        return operations;
    }
}

public sealed record GetOperationsByCorrelationQuery(string CorrelationId, int Limit) : IRequest<IReadOnlyList<ServerOperationReadModel>>;

public sealed class GetOperationsByCorrelationQueryHandler : IRequestHandler<GetOperationsByCorrelationQuery, IReadOnlyList<ServerOperationReadModel>>
{
    private readonly IServiceProvider _services;

    public GetOperationsByCorrelationQueryHandler(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<IReadOnlyList<ServerOperationReadModel>> Handle(
        GetOperationsByCorrelationQuery request,
        CancellationToken cancellationToken)
    {
        var normalizedCorrelationId = request.CorrelationId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCorrelationId))
            return Array.Empty<ServerOperationReadModel>();

        var normalizedLimit = Math.Clamp(request.Limit, 1, 200);
        var dbContextFactory = _services.GetService<IDbContextFactory<ReplicaDbContext>>();
        if (dbContextFactory == null)
            return Array.Empty<ServerOperationReadModel>();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.OrderEvents
            .AsNoTracking()
            .OrderByDescending(x => x.EventId)
            .Take(2000)
            .Select(x => new ServerOperationReadModel
            {
                EventId = x.EventId,
                OrderId = x.OrderInternalId,
                ItemId = x.ItemId,
                EventType = x.EventType,
                EventSource = x.EventSource,
                CreatedAtUtc = x.CreatedAt,
                PayloadJson = x.PayloadJson
            })
            .ToListAsync(cancellationToken);

        return candidates
            .Where(model => string.Equals(
                ExtractCorrelationId(model.PayloadJson),
                normalizedCorrelationId,
                StringComparison.OrdinalIgnoreCase))
            .Take(normalizedLimit)
            .ToList();
    }

    private static string ExtractCorrelationId(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return string.Empty;

            if (TryGetStringPropertyIgnoreCase(root, "correlation_id", out var correlationId))
                return correlationId;

            if (TryGetObjectPropertyIgnoreCase(root, "payload", out var payload)
                && TryGetStringPropertyIgnoreCase(payload, "correlation_id", out correlationId))
            {
                return correlationId;
            }
        }
        catch
        {
            // ignore malformed payload
        }

        return string.Empty;
    }

    private static bool TryGetObjectPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                value = property.Value;
                return true;
            }

            break;
        }

        value = default;
        return false;
    }

    private static bool TryGetStringPropertyIgnoreCase(JsonElement element, string propertyName, out string value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString()?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            break;
        }

        value = string.Empty;
        return false;
    }
}

public sealed record GetPushDiagnosticsQuery() : IRequest<PushDiagnosticsReadModel>;

public sealed class GetPushDiagnosticsQueryHandler : IRequestHandler<GetPushDiagnosticsQuery, PushDiagnosticsReadModel>
{
    public Task<PushDiagnosticsReadModel> Handle(GetPushDiagnosticsQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(PushDiagnosticsReadModel.FromCurrentSnapshot());
    }
}

public sealed class ServerOperationReadModel
{
    public long EventId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string EventSource { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string PayloadJson { get; set; } = "{}";
}

public sealed class PushDiagnosticsReadModel
{
    public long PublishedTotal { get; set; }
    public long PublishFailuresTotal { get; set; }
    public double PublishSuccessRatio { get; set; }
    public long OrderUpdatedPublished { get; set; }
    public long OrderDeletedPublished { get; set; }
    public long ForceRefreshPublished { get; set; }
    public long OrderUpdatedFailures { get; set; }
    public long OrderDeletedFailures { get; set; }
    public long ForceRefreshFailures { get; set; }

    public static PushDiagnosticsReadModel FromCurrentSnapshot()
    {
        var snapshot = ReplicaApiObservability.GetSnapshot();
        return new PushDiagnosticsReadModel
        {
            PublishedTotal = snapshot.PushPublishedTotal,
            PublishFailuresTotal = snapshot.PushPublishFailuresTotal,
            PublishSuccessRatio = snapshot.PushPublishSuccessRatio,
            OrderUpdatedPublished = snapshot.PushOrderUpdatedPublished,
            OrderDeletedPublished = snapshot.PushOrderDeletedPublished,
            ForceRefreshPublished = snapshot.PushForceRefreshPublished,
            OrderUpdatedFailures = snapshot.PushOrderUpdatedFailures,
            OrderDeletedFailures = snapshot.PushOrderDeletedFailures,
            ForceRefreshFailures = snapshot.PushForceRefreshFailures
        };
    }
}
