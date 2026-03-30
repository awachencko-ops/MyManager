using Replica.Api.Application.Abstractions;
using Replica.Shared.Models;

namespace Replica.Api.Services;

public sealed class UserOperationResult : IReplicaApiUserOperationResult
{
    public bool IsSuccess { get; init; }
    public bool IsBadRequest { get; init; }
    public string Error { get; init; } = string.Empty;
    public SharedUser? User { get; init; }

    public static UserOperationResult Success(SharedUser user) => new()
    {
        IsSuccess = true,
        User = user
    };

    public static UserOperationResult BadRequest(string error) => new()
    {
        IsBadRequest = true,
        Error = error ?? string.Empty
    };
}
