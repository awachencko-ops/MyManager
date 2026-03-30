using Replica.Shared.Models;

namespace Replica.Api.Application.Abstractions;

public interface IReplicaApiOrderOperationResult
{
    bool IsSuccess { get; }
    bool IsNotFound { get; }
    bool IsConflict { get; }
    bool IsBadRequest { get; }
    string Error { get; }
    long CurrentVersion { get; }
    SharedOrder? Order { get; }
}

public interface IReplicaApiUserOperationResult
{
    bool IsSuccess { get; }
    bool IsBadRequest { get; }
    string Error { get; }
    SharedUser? User { get; }
}
