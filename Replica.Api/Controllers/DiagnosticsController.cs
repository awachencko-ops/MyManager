using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Data;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    [HttpGet("operations/recent")]
    [ProducesResponseType(typeof(IReadOnlyList<ServerOperationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ServerOperationDto>>> GetRecentOperations([FromQuery] int limit = 30)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 200);
        var dbContextFactory = HttpContext.RequestServices.GetService<IDbContextFactory<ReplicaDbContext>>();
        if (dbContextFactory == null)
            return Ok(Array.Empty<ServerOperationDto>());

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var operations = await db.OrderEvents
            .AsNoTracking()
            .OrderByDescending(x => x.EventId)
            .Take(normalizedLimit)
            .Select(x => new ServerOperationDto
            {
                EventId = x.EventId,
                OrderId = x.OrderInternalId,
                ItemId = x.ItemId,
                EventType = x.EventType,
                EventSource = x.EventSource,
                CreatedAtUtc = x.CreatedAt,
                PayloadJson = x.PayloadJson
            })
            .ToListAsync();

        return Ok(operations);
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
