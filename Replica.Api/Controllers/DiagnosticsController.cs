using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Application.Diagnostics.Queries;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/diagnostics")]
[Replica.Api.Infrastructure.ReplicaAuthorize(Replica.Api.Infrastructure.ReplicaApiRoles.Admin)]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IMediator? _mediator;
    private readonly bool _allowFallback;

    [ActivatorUtilitiesConstructor]
    public DiagnosticsController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _allowFallback = false;
    }

    // Fallback constructor for isolated controller tests.
    public DiagnosticsController()
    {
        _mediator = null;
        _allowFallback = true;
    }

    [HttpGet("operations/recent")]
    [ProducesResponseType(typeof(IReadOnlyList<ServerOperationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ServerOperationDto>>> GetRecentOperations([FromQuery] int limit = 30)
    {
        var operations = await ExecuteQueryAsync(
            new GetRecentOperationsQuery(limit),
            () => Task.FromResult<IReadOnlyList<ServerOperationReadModel>>(Array.Empty<ServerOperationReadModel>()));

        return Ok(operations.Select(ToDto).ToList());
    }

    [HttpGet("push")]
    [ProducesResponseType(typeof(PushDiagnosticsDto), StatusCodes.Status200OK)]
    public ActionResult<PushDiagnosticsDto> GetPushDiagnostics()
    {
        var diagnostics = ExecuteQuery(
            new GetPushDiagnosticsQuery(),
            PushDiagnosticsReadModel.FromCurrentSnapshot);

        return Ok(ToDto(diagnostics));
    }

    private async Task<TResponse> ExecuteQueryAsync<TResponse>(
        IRequest<TResponse> query,
        Func<Task<TResponse>> fallback)
    {
        if (_mediator != null)
        {
            var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
            return await _mediator.Send(query, cancellationToken);
        }

        if (_allowFallback)
            return await fallback();

        throw new InvalidOperationException("DiagnosticsController requires IMediator in runtime composition.");
    }

    private TResponse ExecuteQuery<TResponse>(
        IRequest<TResponse> query,
        Func<TResponse> fallback)
    {
        if (_mediator != null)
        {
            var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
            return _mediator.Send(query, cancellationToken).GetAwaiter().GetResult();
        }

        if (_allowFallback)
            return fallback();

        throw new InvalidOperationException("DiagnosticsController requires IMediator in runtime composition.");
    }

    private static ServerOperationDto ToDto(ServerOperationReadModel source)
    {
        return new ServerOperationDto
        {
            EventId = source.EventId,
            OrderId = source.OrderId,
            ItemId = source.ItemId,
            EventType = source.EventType,
            EventSource = source.EventSource,
            CreatedAtUtc = source.CreatedAtUtc,
            PayloadJson = source.PayloadJson
        };
    }

    private static PushDiagnosticsDto ToDto(PushDiagnosticsReadModel source)
    {
        return new PushDiagnosticsDto
        {
            PublishedTotal = source.PublishedTotal,
            PublishFailuresTotal = source.PublishFailuresTotal,
            PublishSuccessRatio = source.PublishSuccessRatio,
            OrderUpdatedPublished = source.OrderUpdatedPublished,
            OrderDeletedPublished = source.OrderDeletedPublished,
            ForceRefreshPublished = source.ForceRefreshPublished,
            OrderUpdatedFailures = source.OrderUpdatedFailures,
            OrderDeletedFailures = source.OrderDeletedFailures,
            ForceRefreshFailures = source.ForceRefreshFailures
        };
    }
}

public sealed class ServerOperationDto
{
    public long EventId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string EventSource { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string PayloadJson { get; set; } = "{}";
}

public sealed class PushDiagnosticsDto
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
}
