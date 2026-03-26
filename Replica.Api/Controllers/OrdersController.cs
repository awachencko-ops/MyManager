using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Replica.Shared;
using Replica.Shared.Models;
using System;
using System.Linq;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const string CreateOrderCommandName = "create-order";
    private const string DeleteOrderCommandName = "delete-order";
    private const string UpdateOrderCommandName = "update-order";
    private const string AddItemCommandName = "add-item";
    private const string UpdateItemCommandName = "update-item";
    private const string DeleteItemCommandName = "delete-item";
    private const string ReorderItemsCommandName = "reorder-items";
    private const string RunCommandName = "run";
    private const string StopCommandName = "stop";
    private readonly ILanOrderStore _store;
    private readonly ILogger<OrdersController> _logger;
    private readonly bool _strictActorValidation;

    public OrdersController(ILanOrderStore store, ILogger<OrdersController> logger)
        : this(store, logger, configuration: null)
    {
    }

    public OrdersController(ILanOrderStore store, ILogger<OrdersController> logger, IConfiguration? configuration)
    {
        _store = store;
        _logger = logger;
        _strictActorValidation = configuration?.GetValue<bool?>("ReplicaApi:StrictActorValidation") ?? false;
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
            return BuildCreateResponse(createResult);
        }

        var created = _store.CreateOrder(request, actor);
        ReplicaApiObservability.RecordWriteCommand(CreateOrderCommandName, "success");
        return CreatedAtAction(nameof(GetOrderById), new { id = created.InternalId }, created);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SharedOrder> DeleteOrder(string id, [FromBody] DeleteOrderRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "request body is required" });

        if (!TryResolveValidatedActor(out var actor, out var validationError))
            return validationError!;

        var idempotencyKey = ResolveIdempotencyKey();
        var result = _store is EfCoreLanOrderStore efCoreStore
            ? efCoreStore.TryDeleteOrder(id, request, actor, idempotencyKey)
            : _store.TryDeleteOrder(id, request, actor);
        return BuildWriteResponse(DeleteOrderCommandName, result);
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
        return BuildWriteResponse(UpdateOrderCommandName, result);
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
        return BuildWriteResponse(AddItemCommandName, result);
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
        return BuildWriteResponse(UpdateItemCommandName, result);
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
        return BuildWriteResponse(DeleteItemCommandName, result);
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
        return BuildWriteResponse(ReorderItemsCommandName, result);
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
        return BuildWriteResponse(RunCommandName, result);
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
        return BuildWriteResponse(StopCommandName, result);
    }

    private bool TryResolveValidatedActor(out string actor, out ActionResult? validationError)
    {
        actor = string.Empty;
        validationError = null;

        if (Request.Headers.TryGetValue(CurrentUserHeaderCodec.EncodedHeaderName, out var encodedActorHeader)
            && CurrentUserHeaderCodec.TryDecode(encodedActorHeader.ToString(), out var decodedActor))
            actor = decodedActor;
        else if (Request.Headers.TryGetValue(CurrentUserHeaderCodec.HeaderName, out var actorHeader))
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

        // Allow bootstrap environments to migrate away from the legacy placeholder user.
        if (knownUsers.All(user => string.Equals(user.Name.Trim(), "Сервер \"Таудеми\"", StringComparison.OrdinalIgnoreCase)))
            return true;

        var actorCandidate = actor;
        var matchedUser = knownUsers.FirstOrDefault(user =>
            string.Equals(user.Name.Trim(), actorCandidate, StringComparison.OrdinalIgnoreCase));
        if (matchedUser == null || !matchedUser.IsActive)
        {
            if (!_strictActorValidation)
            {
                _logger.LogWarning(
                    "Write request actor {Actor} is not present in active users list. Allowing because strict actor validation is disabled.",
                    actorCandidate);
                actor = actorCandidate.Trim();
                return true;
            }

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

    private ActionResult<SharedOrder> BuildCreateResponse(StoreOperationResult result)
    {
        if (result.IsSuccess && result.Order != null)
        {
            ReplicaApiObservability.RecordWriteCommand(CreateOrderCommandName, "success");
            return CreatedAtAction(nameof(GetOrderById), new { id = result.Order.InternalId }, result.Order);
        }

        if (result.IsConflict)
        {
            ReplicaApiObservability.RecordWriteCommand(CreateOrderCommandName, "conflict");
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
        }

        if (result.IsNotFound)
        {
            ReplicaApiObservability.RecordWriteCommand(CreateOrderCommandName, "not_found");
            return NotFound(new { error = result.Error });
        }

        ReplicaApiObservability.RecordWriteCommand(CreateOrderCommandName, "bad_request");
        return BadRequest(new { error = result.Error });
    }

    private ActionResult<SharedOrder> BuildWriteResponse(string commandName, StoreOperationResult result)
    {
        if (result.IsSuccess)
        {
            ReplicaApiObservability.RecordWriteCommand(commandName, "success");
            return Ok(result.Order);
        }

        if (result.IsNotFound)
        {
            ReplicaApiObservability.RecordWriteCommand(commandName, "not_found");
            return NotFound(new { error = result.Error });
        }

        if (result.IsConflict)
        {
            ReplicaApiObservability.RecordWriteCommand(commandName, "conflict");
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });
        }

        ReplicaApiObservability.RecordWriteCommand(commandName, "bad_request");
        return BadRequest(new { error = result.Error });
    }
}

