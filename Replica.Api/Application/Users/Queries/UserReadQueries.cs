using MediatR;
using Replica.Api.Services;
using Replica.Shared.Models;

namespace Replica.Api.Application.Users.Queries;

public sealed record GetUsersQuery(bool IncludeInactive) : IRequest<IReadOnlyList<SharedUser>>;

public sealed class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, IReadOnlyList<SharedUser>>
{
    private readonly ILanOrderStore _store;

    public GetUsersQueryHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<SharedUser>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_store.GetUsers(request.IncludeInactive));
    }
}
