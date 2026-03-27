using MediatR;
using Replica.Api.Application.Abstractions;
using Replica.Api.Contracts;
using Replica.Api.Services;

namespace Replica.Api.Application.Users.Commands;

public sealed record UpsertUserCommand(
    UpsertUserRequest Request,
    string Actor) : IRequest<UserOperationResult>, IReplicaApiWriteCommand
{
    public string CommandName => "upsert-user";
}

public sealed class UpsertUserCommandHandler : IRequestHandler<UpsertUserCommand, UserOperationResult>
{
    private readonly ILanOrderStore _store;

    public UpsertUserCommandHandler(ILanOrderStore store)
    {
        _store = store;
    }

    public Task<UserOperationResult> Handle(UpsertUserCommand command, CancellationToken cancellationToken)
    {
        return Task.FromResult(_store.UpsertUser(command.Request, command.Actor));
    }
}
