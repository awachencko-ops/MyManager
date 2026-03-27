namespace Replica.Api.Application.Abstractions;

public interface IReplicaApiWriteCommand
{
    string CommandName { get; }
}

public interface IReplicaApiIdempotentWriteCommand : IReplicaApiWriteCommand
{
    string IdempotencyKey { get; }
}

public interface IReplicaApiCommandValidator<in TCommand>
{
    bool TryValidate(TCommand command, out string error);
}

