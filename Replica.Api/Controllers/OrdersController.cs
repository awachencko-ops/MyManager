using Microsoft.AspNetCore.Mvc;
using Replica.Api.Contracts;
using Replica.Api.Services;
using Replica.Shared.Models;
using System;
using System.Linq;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private readonly ILanOrderStore _store;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(ILanOrderStore store, ILogger<OrdersController> logger)
    {
        _store = store;
        _logger = logger;
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<SharedOrder> CreateOrder([FromBody] CreateOrderRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.OrderNumber))
            return BadRequest(new { error = "order number is required" });

        if (!TryResolveValidatedActor(out var actor, out var validationError))
            return validationError!;

        if (string.IsNullOrWhiteSpace(request.CreatedByUser))
            request.CreatedByUser = actor;
        if (string.IsNullOrWhiteSpace(request.CreatedById))
            request.CreatedById = actor;

        var idempotencyKey = ResolveIdempotencyKey();
        if (_store is EfCoreLanOrderStore efCoreStore)
        {
            var createResult = efCoreStore.TryCreateOrder(request, actor, idempotencyKey);
            if (createResult.IsSuccess && createResult.Order != null)
                return CreatedAtAction(nameof(GetOrderById), new { id = createResult.Order.InternalId }, createResult.Order);

            if (createResult.IsConflict)
                return Conflict(new { error = createResult.Error, currentVersion = createResult.CurrentVersion });
            if (createResult.IsNotFound)
                return NotFound(new { error = createResult.Error });
            return BadRequest(new { error = createResult.Error });
        }

        var created = _store.CreateOrder(request, actor);
        return CreatedAtAction(nameof(GetOrderById), new { id = created.InternalId }, created);
    }

    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> UpdateOrder(string id, [FromBody] UpdateOrderRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "request body is required" });

        if (!TryResolveValidatedActor(out var actor, out var validationError))
            return validationError!;

        var idempotencyKey = ResolveIdempotencyKey();
        var result = _store is EfCoreLanOrderStore efCoreStore
            ? efCoreStore.TryUpdateOrder(id, request, actor, idempotencyKey)
            : _store.TryUpdateOrder(id, request, actor);
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> AddOrderItem(string id, [FromBody] AddOrderItemRequest request)
    {
        if (request == null || request.Item == null)
            return BadRequest(new { error = "item payload is required" });

        if (!TryResolveValidatedActor(out var actor, out var validationError))
            return validationError!;

        var idempotencyKey = ResolveIdempotencyKey();
        var result = _store is EfCoreLanOrderStore efCoreStore
            ? efCoreStore.TryAddItem(id, request, actor, idempotencyKey)
            : _store.TryAddItem(id, request, actor);
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> UpdateOrderItem(string id, string itemId, [FromBody] UpdateOrderItemRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "request body is required" });

        if (!TryResolveValidatedActor(out var actor, out var validationError))
            return validationError!;

        var idempotencyKey = ResolveIdempotencyKey();
        var result = _store is EfCoreLanOrderStore efCoreStore
            ? efCoreStore.TryUpdateItem(id, itemId, request, actor, idempotencyKey)
            : _store.TryUpdateItem(id, itemId, request, actor);
        if (result.IsSuccess)
            return Ok(result.Order);
        if (result.IsNotFound)
            return NotFound(new { error = result.Error });
        if (result.IsConflict)
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
        return BadRequest(new { error = result.Error });
    }

    [HttpDelete("{id}/items/{itemId}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> DeleteOrderItem(string id, string itemId, [FromBody] DeleteOrderItemRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "request body is required" });

        if (!TryResolveValidatedActor(out var actor, out var validationError))
            return validationError!;

        var idempotencyKey = ResolveIdempotencyKey();
        var result = _store is EfCoreLanOrderStore efCoreStore
            ? efCoreStore.TryDeleteItem(id, itemId, request, actor, idempotencyKey)
            : _store.TryDeleteItem(id, itemId, request, actor);
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> ReorderOrderItems(string id, [FromBody] ReorderOrderItemsRequest request)
    {
        if (request == null || request.OrderedItemIds == null)
            return BadRequest(new { error = "ordered item ids are required" });

        if (!TryResolveValidatedActor(out var actor, out var validationError))
            return validationError!;

        var idempotencyKey = ResolveIdempotencyKey();
        var result = _store is EfCoreLanOrderStore efCoreStore
            ? efCoreStore.TryReorderItems(id, request, actor, idempotencyKey)
            : _store.TryReorderItems(id, request, actor);
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> StartOrderRun(string id, [FromBody] RunOrderRequest? request)
    {
        if (!TryResolveValidatedActor(out var actor, out var validationError))
            return validationError!;

        var runRequest = request ?? new RunOrderRequest();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = _store is EfCoreLanOrderStore efCoreStore
            ? efCoreStore.TryStartRun(id, runRequest, actor, idempotencyKey)
            : _store.TryStartRun(id, runRequest, actor);
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> StopOrderRun(string id, [FromBody] StopOrderRequest? request)
    {
        if (!TryResolveValidatedActor(out var actor, out var validationError))
            return validationError!;

        var stopRequest = request ?? new StopOrderRequest();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = _store is EfCoreLanOrderStore efCoreStore
            ? efCoreStore.TryStopRun(id, stopRequest, actor, idempotencyKey)
            : _store.TryStopRun(id, stopRequest, actor);
        if (result.IsSuccess)
            return Ok(result.Order);
        if (result.IsNotFound)
            return NotFound(new { error = result.Error });
        if (result.IsConflict)
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
        return BadRequest(new { error = result.Error });
    }

    private bool TryResolveValidatedActor(out string actor, out ActionResult? validationError)
    {
        actor = string.Empty;
        validationError = null;

        if (Request.Headers.TryGetValue("X-Current-User", out var actorHeader))
            actor = actorHeader.ToString().Trim();

        if (string.IsNullOrWhiteSpace(actor))
        {
            validationError = Unauthorized(new { error = "X-Current-User header is required" });
            return false;
        }

        var knownUsers = _store
            .GetUsers()
            .Where(user => user != null && !string.IsNullOrWhiteSpace(user.Name))
            .ToList();

        if (knownUsers.Count == 0)
            return true;

        var actorCandidate = actor;
        var matchedUser = knownUsers.FirstOrDefault(user =>
            string.Equals(user.Name.Trim(), actorCandidate, StringComparison.OrdinalIgnoreCase));
        if (matchedUser == null || !matchedUser.IsActive)
        {
            _logger.LogWarning("Write request rejected for actor {Actor}: unknown or inactive user.", actorCandidate);
            validationError = StatusCode(StatusCodes.Status403Forbidden, new { error = "actor is not allowed" });
            return false;
        }

        actor = matchedUser.Name.Trim();
        return true;
    }

    private string ResolveIdempotencyKey()
    {
        if (!Request.Headers.TryGetValue(IdempotencyHeaderName, out var rawValue))
            return string.Empty;

        var value = rawValue.ToString().Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= 128 ? value : value[..128];
    }
}

