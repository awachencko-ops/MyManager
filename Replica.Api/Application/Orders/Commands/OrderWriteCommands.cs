using MediatR;
using Replica.Api.Contracts;
using Replica.Api.Services;

namespace Replica.Api.Application.Orders.Commands;

public sealed record CreateOrderCommand(
    CreateOrderRequest Request,
    string Actor,
    string IdempotencyKey) : IRequest<StoreOperationResult>;

public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, StoreOperationResult>
{
    private readonly ILanOrderStore _store;

    public CreateOrderCommandHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<StoreOperationResult> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        if (_store is EfCoreLanOrderStore efCoreStore)
            return Task.FromResult(efCoreStore.TryCreateOrder(command.Request, command.Actor, command.IdempotencyKey));

        var created = _store.CreateOrder(command.Request, command.Actor);
        return Task.FromResult(StoreOperationResult.Success(created));
    }
}

public sealed record DeleteOrderCommand(
    string OrderId,
    DeleteOrderRequest Request,
    string Actor,
    string IdempotencyKey) : IRequest<StoreOperationResult>;

public sealed class DeleteOrderCommandHandler : IRequestHandler<DeleteOrderCommand, StoreOperationResult>
{
    private readonly ILanOrderStore _store;

    public DeleteOrderCommandHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<StoreOperationResult> Handle(DeleteOrderCommand command, CancellationToken cancellationToken)
    {
        if (_store is EfCoreLanOrderStore efCoreStore)
            return Task.FromResult(efCoreStore.TryDeleteOrder(command.OrderId, command.Request, command.Actor, command.IdempotencyKey));

        return Task.FromResult(_store.TryDeleteOrder(command.OrderId, command.Request, command.Actor));
    }
}

public sealed record UpdateOrderCommand(
    string OrderId,
    UpdateOrderRequest Request,
    string Actor,
    string IdempotencyKey) : IRequest<StoreOperationResult>;

public sealed class UpdateOrderCommandHandler : IRequestHandler<UpdateOrderCommand, StoreOperationResult>
{
    private readonly ILanOrderStore _store;

    public UpdateOrderCommandHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<StoreOperationResult> Handle(UpdateOrderCommand command, CancellationToken cancellationToken)
    {
        if (_store is EfCoreLanOrderStore efCoreStore)
            return Task.FromResult(efCoreStore.TryUpdateOrder(command.OrderId, command.Request, command.Actor, command.IdempotencyKey));

        return Task.FromResult(_store.TryUpdateOrder(command.OrderId, command.Request, command.Actor));
    }
}

public sealed record AddOrderItemCommand(
    string OrderId,
    AddOrderItemRequest Request,
    string Actor,
    string IdempotencyKey) : IRequest<StoreOperationResult>;

public sealed class AddOrderItemCommandHandler : IRequestHandler<AddOrderItemCommand, StoreOperationResult>
{
    private readonly ILanOrderStore _store;

    public AddOrderItemCommandHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<StoreOperationResult> Handle(AddOrderItemCommand command, CancellationToken cancellationToken)
    {
        if (_store is EfCoreLanOrderStore efCoreStore)
            return Task.FromResult(efCoreStore.TryAddItem(command.OrderId, command.Request, command.Actor, command.IdempotencyKey));

        return Task.FromResult(_store.TryAddItem(command.OrderId, command.Request, command.Actor));
    }
}

public sealed record UpdateOrderItemCommand(
    string OrderId,
    string ItemId,
    UpdateOrderItemRequest Request,
    string Actor,
    string IdempotencyKey) : IRequest<StoreOperationResult>;

public sealed class UpdateOrderItemCommandHandler : IRequestHandler<UpdateOrderItemCommand, StoreOperationResult>
{
    private readonly ILanOrderStore _store;

    public UpdateOrderItemCommandHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<StoreOperationResult> Handle(UpdateOrderItemCommand command, CancellationToken cancellationToken)
    {
        if (_store is EfCoreLanOrderStore efCoreStore)
            return Task.FromResult(efCoreStore.TryUpdateItem(command.OrderId, command.ItemId, command.Request, command.Actor, command.IdempotencyKey));

        return Task.FromResult(_store.TryUpdateItem(command.OrderId, command.ItemId, command.Request, command.Actor));
    }
}

public sealed record DeleteOrderItemCommand(
    string OrderId,
    string ItemId,
    DeleteOrderItemRequest Request,
    string Actor,
    string IdempotencyKey) : IRequest<StoreOperationResult>;

public sealed class DeleteOrderItemCommandHandler : IRequestHandler<DeleteOrderItemCommand, StoreOperationResult>
{
    private readonly ILanOrderStore _store;

    public DeleteOrderItemCommandHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<StoreOperationResult> Handle(DeleteOrderItemCommand command, CancellationToken cancellationToken)
    {
        if (_store is EfCoreLanOrderStore efCoreStore)
            return Task.FromResult(efCoreStore.TryDeleteItem(command.OrderId, command.ItemId, command.Request, command.Actor, command.IdempotencyKey));

        return Task.FromResult(_store.TryDeleteItem(command.OrderId, command.ItemId, command.Request, command.Actor));
    }
}

public sealed record ReorderOrderItemsCommand(
    string OrderId,
    ReorderOrderItemsRequest Request,
    string Actor,
    string IdempotencyKey) : IRequest<StoreOperationResult>;

public sealed class ReorderOrderItemsCommandHandler : IRequestHandler<ReorderOrderItemsCommand, StoreOperationResult>
{
    private readonly ILanOrderStore _store;

    public ReorderOrderItemsCommandHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<StoreOperationResult> Handle(ReorderOrderItemsCommand command, CancellationToken cancellationToken)
    {
        if (_store is EfCoreLanOrderStore efCoreStore)
            return Task.FromResult(efCoreStore.TryReorderItems(command.OrderId, command.Request, command.Actor, command.IdempotencyKey));

        return Task.FromResult(_store.TryReorderItems(command.OrderId, command.Request, command.Actor));
    }
}

public sealed record StartOrderRunCommand(
    string OrderId,
    RunOrderRequest Request,
    string Actor,
    string IdempotencyKey) : IRequest<StoreOperationResult>;

public sealed class StartOrderRunCommandHandler : IRequestHandler<StartOrderRunCommand, StoreOperationResult>
{
    private readonly ILanOrderStore _store;

    public StartOrderRunCommandHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<StoreOperationResult> Handle(StartOrderRunCommand command, CancellationToken cancellationToken)
    {
        if (_store is EfCoreLanOrderStore efCoreStore)
            return Task.FromResult(efCoreStore.TryStartRun(command.OrderId, command.Request, command.Actor, command.IdempotencyKey));

        return Task.FromResult(_store.TryStartRun(command.OrderId, command.Request, command.Actor));
    }
}

public sealed record StopOrderRunCommand(
    string OrderId,
    StopOrderRequest Request,
    string Actor,
    string IdempotencyKey) : IRequest<StoreOperationResult>;

public sealed class StopOrderRunCommandHandler : IRequestHandler<StopOrderRunCommand, StoreOperationResult>
{
    private readonly ILanOrderStore _store;

    public StopOrderRunCommandHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<StoreOperationResult> Handle(StopOrderRunCommand command, CancellationToken cancellationToken)
    {
        if (_store is EfCoreLanOrderStore efCoreStore)
            return Task.FromResult(efCoreStore.TryStopRun(command.OrderId, command.Request, command.Actor, command.IdempotencyKey));

        return Task.FromResult(_store.TryStopRun(command.OrderId, command.Request, command.Actor));
    }
}
