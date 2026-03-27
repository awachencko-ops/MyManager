using MediatR;
using Replica.Api.Services;
using Replica.Shared.Models;

namespace Replica.Api.Application.Orders.Queries;

public sealed record GetOrdersQuery(string CreatedBy) : IRequest<IReadOnlyList<SharedOrder>>;

public sealed class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, IReadOnlyList<SharedOrder>>
{
    private readonly ILanOrderStore _store;

    public GetOrdersQueryHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<SharedOrder>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_store.GetOrders(request.CreatedBy ?? string.Empty));
    }
}

public sealed record GetOrderByIdQuery(string OrderId) : IRequest<SharedOrder?>;

public sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, SharedOrder?>
{
    private readonly ILanOrderStore _store;

    public GetOrderByIdQueryHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<SharedOrder?> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        if (_store.TryGetOrder(request.OrderId ?? string.Empty, out var order))
            return Task.FromResult<SharedOrder?>(order);

        return Task.FromResult<SharedOrder?>(null);
    }
}
