using Microsoft.AspNetCore.Mvc;
using Replica.Api.Contracts;
using Replica.Api.Services;
using Replica.Shared.Models;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly ILanOrderStore _store;

    public OrdersController(ILanOrderStore store)
    {
        _store = store;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SharedOrder>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SharedOrder>> GetOrders([FromQuery] string createdBy = "")
    {
        return Ok(_store.GetOrders(createdBy));
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SharedOrder> GetOrderById(string id)
    {
        return _store.TryGetOrder(id, out var order)
            ? Ok(order)
            : NotFound(new { error = "order not found" });
    }

    [HttpPost]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<SharedOrder> CreateOrder([FromBody] CreateOrderRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.OrderNumber))
            return BadRequest(new { error = "order number is required" });

        var actor = ResolveActor();
        if (string.IsNullOrWhiteSpace(request.CreatedByUser))
            request.CreatedByUser = actor;
        if (string.IsNullOrWhiteSpace(request.CreatedById))
            request.CreatedById = actor;

        var created = _store.CreateOrder(request, actor);
        return CreatedAtAction(nameof(GetOrderById), new { id = created.InternalId }, created);
    }

    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> UpdateOrder(string id, [FromBody] UpdateOrderRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "request body is required" });

        var result = _store.TryUpdateOrder(id, request, ResolveActor());
        if (result.IsSuccess)
            return Ok(result.Order);
        if (result.IsNotFound)
            return NotFound(new { error = result.Error });
        if (result.IsConflict)
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
        return BadRequest(new { error = result.Error });
    }

    [HttpPost("{id}/items")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> AddOrderItem(string id, [FromBody] AddOrderItemRequest request)
    {
        if (request == null || request.Item == null)
            return BadRequest(new { error = "item payload is required" });

        var result = _store.TryAddItem(id, request, ResolveActor());
        if (result.IsSuccess)
            return Ok(result.Order);
        if (result.IsNotFound)
            return NotFound(new { error = result.Error });
        if (result.IsConflict)
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
        return BadRequest(new { error = result.Error });
    }

    [HttpPatch("{id}/items/{itemId}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> UpdateOrderItem(string id, string itemId, [FromBody] UpdateOrderItemRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "request body is required" });

        var result = _store.TryUpdateItem(id, itemId, request, ResolveActor());
        if (result.IsSuccess)
            return Ok(result.Order);
        if (result.IsNotFound)
            return NotFound(new { error = result.Error });
        if (result.IsConflict)
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
        return BadRequest(new { error = result.Error });
    }

    [HttpPost("{id}/items/reorder")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> ReorderOrderItems(string id, [FromBody] ReorderOrderItemsRequest request)
    {
        if (request == null || request.OrderedItemIds == null)
            return BadRequest(new { error = "ordered item ids are required" });

        var result = _store.TryReorderItems(id, request, ResolveActor());
        if (result.IsSuccess)
            return Ok(result.Order);
        if (result.IsNotFound)
            return NotFound(new { error = result.Error });
        if (result.IsConflict)
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
        return BadRequest(new { error = result.Error });
    }

    [HttpPost("{id}/run")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> StartOrderRun(string id, [FromBody] RunOrderRequest? request)
    {
        var runRequest = request ?? new RunOrderRequest();
        var result = _store.TryStartRun(id, runRequest, ResolveActor());
        if (result.IsSuccess)
            return Ok(result.Order);
        if (result.IsNotFound)
            return NotFound(new { error = result.Error });
        if (result.IsConflict)
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
        return BadRequest(new { error = result.Error });
    }

    [HttpPost("{id}/stop")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> StopOrderRun(string id, [FromBody] StopOrderRequest? request)
    {
        var stopRequest = request ?? new StopOrderRequest();
        var result = _store.TryStopRun(id, stopRequest, ResolveActor());
        if (result.IsSuccess)
            return Ok(result.Order);
        if (result.IsNotFound)
            return NotFound(new { error = result.Error });
        if (result.IsConflict)
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
        return BadRequest(new { error = result.Error });
    }

    private string ResolveActor()
    {
        if (Request.Headers.TryGetValue("X-Current-User", out var actorHeader))
        {
            var actor = actorHeader.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(actor))
                return actor;
        }

        return "api-anonymous";
    }
}

