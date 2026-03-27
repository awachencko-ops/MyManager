using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Replica.Api.Application.Orders.Commands;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Replica.Shared.Models;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/orders")]
[ReplicaAuthorize(ReplicaApiRoles.Operator)]
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
    private readonly IMediator? _mediator;

    public OrdersController(ILanOrderStore store, ILogger<OrdersController> logger, IConfiguration? configuration)
        : this(store, logger, configuration, mediator: null)
    {
    }

    public OrdersController(ILanOrderStore store, ILogger<OrdersController> logger, IConfiguration? configuration, IMediator? mediator)
    {
        _store = store;
        _mediator = mediator;
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

        var actor = GetCurrentActor();
        if (string.IsNullOrWhiteSpace(request.CreatedByUser))
            request.CreatedByUser = actor;
        if (string.IsNullOrWhiteSpace(request.CreatedById))
            request.CreatedById = actor;

        var idempotencyKey = ResolveIdempotencyKey();
        var result = ExecuteWriteCommand(
            new CreateOrderCommand(request, actor, idempotencyKey),
            () =>
            {
                if (_store is EfCoreLanOrderStore efCoreStore)
                    return efCoreStore.TryCreateOrder(request, actor, idempotencyKey);

                var created = _store.CreateOrder(request, actor);
                return StoreOperationResult.Success(created);
            });
        return BuildCreateResponse(result);
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

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = ExecuteWriteCommand(
            new DeleteOrderCommand(id, request, actor, idempotencyKey),
            () => _store is EfCoreLanOrderStore efCoreStore
                ? efCoreStore.TryDeleteOrder(id, request, actor, idempotencyKey)
                : _store.TryDeleteOrder(id, request, actor));
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

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = ExecuteWriteCommand(
            new UpdateOrderCommand(id, request, actor, idempotencyKey),
            () => _store is EfCoreLanOrderStore efCoreStore
                ? efCoreStore.TryUpdateOrder(id, request, actor, idempotencyKey)
                : _store.TryUpdateOrder(id, request, actor));
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

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = ExecuteWriteCommand(
            new AddOrderItemCommand(id, request, actor, idempotencyKey),
            () => _store is EfCoreLanOrderStore efCoreStore
                ? efCoreStore.TryAddItem(id, request, actor, idempotencyKey)
                : _store.TryAddItem(id, request, actor));
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

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = ExecuteWriteCommand(
            new UpdateOrderItemCommand(id, itemId, request, actor, idempotencyKey),
            () => _store is EfCoreLanOrderStore efCoreStore
                ? efCoreStore.TryUpdateItem(id, itemId, request, actor, idempotencyKey)
                : _store.TryUpdateItem(id, itemId, request, actor));
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

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = ExecuteWriteCommand(
            new DeleteOrderItemCommand(id, itemId, request, actor, idempotencyKey),
            () => _store is EfCoreLanOrderStore efCoreStore
                ? efCoreStore.TryDeleteItem(id, itemId, request, actor, idempotencyKey)
                : _store.TryDeleteItem(id, itemId, request, actor));
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

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = ExecuteWriteCommand(
            new ReorderOrderItemsCommand(id, request, actor, idempotencyKey),
            () => _store is EfCoreLanOrderStore efCoreStore
                ? efCoreStore.TryReorderItems(id, request, actor, idempotencyKey)
                : _store.TryReorderItems(id, request, actor));
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
        var actor = GetCurrentActor();
        var runRequest = request ?? new RunOrderRequest();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = ExecuteWriteCommand(
            new StartOrderRunCommand(id, runRequest, actor, idempotencyKey),
            () => _store is EfCoreLanOrderStore efCoreStore
                ? efCoreStore.TryStartRun(id, runRequest, actor, idempotencyKey)
                : _store.TryStartRun(id, runRequest, actor));
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
        var actor = GetCurrentActor();
        var stopRequest = request ?? new StopOrderRequest();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = ExecuteWriteCommand(
            new StopOrderRunCommand(id, stopRequest, actor, idempotencyKey),
            () => _store is EfCoreLanOrderStore efCoreStore
                ? efCoreStore.TryStopRun(id, stopRequest, actor, idempotencyKey)
                : _store.TryStopRun(id, stopRequest, actor));
        return BuildWriteResponse(StopCommandName, result);
    }

    private StoreOperationResult ExecuteWriteCommand(
        IRequest<StoreOperationResult> command,
        Func<StoreOperationResult> fallback)
    {
        if (_mediator == null)
            return fallback();

        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        return _mediator.Send(command, cancellationToken).GetAwaiter().GetResult();
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

    private string GetCurrentActor()
    {
        return ReplicaApiCurrentUserContext.Get(HttpContext).Name;
    }
}
