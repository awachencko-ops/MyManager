using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Application.Abstractions;
using Replica.Api.Application.Orders.Commands;
using Replica.Api.Application.Orders.Queries;
using Replica.Api.Contracts;
using Replica.Shared.Models;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/orders")]
[ReplicaAuthorize(ReplicaApiRoleNames.Operator)]
public sealed class OrdersController : ControllerBase
{
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private readonly IMediator _mediator;
    private readonly IReplicaApiCurrentActorAccessor _currentActorAccessor;

    [ActivatorUtilitiesConstructor]
    public OrdersController(IMediator mediator, IReplicaApiCurrentActorAccessor currentActorAccessor)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SharedOrder>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SharedOrder>>> GetOrders([FromQuery] string createdBy = "")
    {
        var orders = await ExecuteQueryAsync(new GetOrdersQuery(createdBy));
        return Ok(orders);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SharedOrder>> GetOrderById(string id)
    {
        var order = await ExecuteQueryAsync(new GetOrderByIdQuery(id));
        return order != null
            ? Ok(order)
            : NotFound(new { error = "order not found" });
    }

    [HttpPost]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SharedOrder>> CreateOrder([FromBody] CreateOrderRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.OrderNumber))
            return BadRequest(new { error = "order number is required" });

        var actor = GetCurrentActor();
        if (string.IsNullOrWhiteSpace(request.CreatedByUser))
            request.CreatedByUser = actor;
        if (string.IsNullOrWhiteSpace(request.CreatedById))
            request.CreatedById = actor;

        var idempotencyKey = ResolveIdempotencyKey();
        var result = await ExecuteWriteCommandAsync(new CreateOrderCommand(request, actor, idempotencyKey));
        return BuildCreateResponse(result);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SharedOrder>> DeleteOrder(string id, [FromBody] DeleteOrderRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "request body is required" });

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = await ExecuteWriteCommandAsync(new DeleteOrderCommand(id, request, actor, idempotencyKey));
        return BuildWriteResponse(result);
    }

    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SharedOrder>> UpdateOrder(string id, [FromBody] UpdateOrderRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "request body is required" });

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = await ExecuteWriteCommandAsync(new UpdateOrderCommand(id, request, actor, idempotencyKey));
        return BuildWriteResponse(result);
    }

    [HttpPost("{id}/items")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SharedOrder>> AddOrderItem(string id, [FromBody] AddOrderItemRequest request)
    {
        if (request == null || request.Item == null)
            return BadRequest(new { error = "item payload is required" });

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = await ExecuteWriteCommandAsync(new AddOrderItemCommand(id, request, actor, idempotencyKey));
        return BuildWriteResponse(result);
    }

    [HttpPatch("{id}/items/{itemId}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SharedOrder>> UpdateOrderItem(string id, string itemId, [FromBody] UpdateOrderItemRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "request body is required" });

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = await ExecuteWriteCommandAsync(new UpdateOrderItemCommand(id, itemId, request, actor, idempotencyKey));
        return BuildWriteResponse(result);
    }

    [HttpDelete("{id}/items/{itemId}")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SharedOrder>> DeleteOrderItem(string id, string itemId, [FromBody] DeleteOrderItemRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "request body is required" });

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = await ExecuteWriteCommandAsync(new DeleteOrderItemCommand(id, itemId, request, actor, idempotencyKey));
        return BuildWriteResponse(result);
    }

    [HttpPost("{id}/items/reorder")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SharedOrder>> ReorderOrderItems(string id, [FromBody] ReorderOrderItemsRequest request)
    {
        if (request == null || request.OrderedItemIds == null)
            return BadRequest(new { error = "ordered item ids are required" });

        var actor = GetCurrentActor();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = await ExecuteWriteCommandAsync(new ReorderOrderItemsCommand(id, request, actor, idempotencyKey));
        return BuildWriteResponse(result);
    }

    [HttpPost("{id}/run")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SharedOrder>> StartOrderRun(string id, [FromBody] RunOrderRequest? request)
    {
        var actor = GetCurrentActor();
        var runRequest = request ?? new RunOrderRequest();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = await ExecuteWriteCommandAsync(new StartOrderRunCommand(id, runRequest, actor, idempotencyKey));
        return BuildWriteResponse(result);
    }

    [HttpPost("{id}/stop")]
    [ProducesResponseType(typeof(SharedOrder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SharedOrder>> StopOrderRun(string id, [FromBody] StopOrderRequest? request)
    {
        var actor = GetCurrentActor();
        var stopRequest = request ?? new StopOrderRequest();
        var idempotencyKey = ResolveIdempotencyKey();
        var result = await ExecuteWriteCommandAsync(new StopOrderRunCommand(id, stopRequest, actor, idempotencyKey));
        return BuildWriteResponse(result);
    }

    private Task<TResult> ExecuteWriteCommandAsync<TResult>(IRequest<TResult> command)
        where TResult : IReplicaApiOrderOperationResult
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        return _mediator.Send(command, cancellationToken);
    }

    private Task<TResponse> ExecuteQueryAsync<TResponse>(IRequest<TResponse> query)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        return _mediator.Send(query, cancellationToken);
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

    private ActionResult<SharedOrder> BuildCreateResponse(IReplicaApiOrderOperationResult result)
    {
        if (result.IsSuccess && result.Order != null)
            return CreatedAtAction(nameof(GetOrderById), new { id = result.Order.InternalId }, result.Order);

        if (result.IsConflict)
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });

        if (result.IsNotFound)
            return NotFound(new { error = result.Error });

        return BadRequest(new { error = result.Error });
    }

    private ActionResult<SharedOrder> BuildWriteResponse(IReplicaApiOrderOperationResult result)
    {
        if (result.IsSuccess)
            return Ok(result.Order);

        if (result.IsNotFound)
            return NotFound(new { error = result.Error });

        if (result.IsConflict)
            return Conflict(new { error = result.Error, currentVersion = result.CurrentVersion });

        return BadRequest(new { error = result.Error });
    }

    private string GetCurrentActor()
    {
        return _currentActorAccessor.GetCurrentActorName();
    }
}

