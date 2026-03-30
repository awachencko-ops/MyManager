using Replica.Shared.Models;

namespace Replica.Api.Application.Abstractions;

public interface IReplicaApiAuthService
{
    IReadOnlyList<SharedUser> GetUsers(bool includeInactive);

    ReplicaApiAuthTokenIssueResult IssueToken(
        string userName,
        string role,
        string issuedBy,
        string ipAddress,
        string userAgent);

    ReplicaApiAuthTokenIssueResult RefreshToken(
        string sessionId,
        string requestedBy,
        string ipAddress,
        string userAgent);

    ReplicaApiAuthTokenRevokeResult RevokeToken(
        string sessionId,
        string requestedBy,
        string ipAddress,
        string userAgent);
}

public sealed class ReplicaApiAuthTokenIssueResult
{
    private ReplicaApiAuthTokenIssueResult(
        bool isSuccess,
        bool isNotFound,
        string error,
        string accessToken,
        string sessionId,
        string userName,
        string role,
        DateTime expiresAtUtc)
    {
        IsSuccess = isSuccess;
        IsNotFound = isNotFound;
        Error = error;
        AccessToken = accessToken;
        SessionId = sessionId;
        UserName = userName;
        Role = role;
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsSuccess { get; }
    public bool IsNotFound { get; }
    public string Error { get; }
    public string AccessToken { get; }
    public string SessionId { get; }
    public string UserName { get; }
    public string Role { get; }
    public DateTime ExpiresAtUtc { get; }

    public static ReplicaApiAuthTokenIssueResult Success(
        string accessToken,
        string sessionId,
        string userName,
        string role,
        DateTime expiresAtUtc)
        => new(
            isSuccess: true,
            isNotFound: false,
            error: string.Empty,
            accessToken: accessToken,
            sessionId: sessionId,
            userName: userName,
            role: role,
            expiresAtUtc: expiresAtUtc);

    public static ReplicaApiAuthTokenIssueResult Failed(string error)
        => new(
            isSuccess: false,
            isNotFound: false,
            error: error ?? string.Empty,
            accessToken: string.Empty,
            sessionId: string.Empty,
            userName: string.Empty,
            role: string.Empty,
            expiresAtUtc: DateTime.MinValue);

    public static ReplicaApiAuthTokenIssueResult NotFound(string error)
        => new(
            isSuccess: false,
            isNotFound: true,
            error: error ?? string.Empty,
            accessToken: string.Empty,
            sessionId: string.Empty,
            userName: string.Empty,
            role: string.Empty,
            expiresAtUtc: DateTime.MinValue);
}

public sealed class ReplicaApiAuthTokenRevokeResult
{
    private ReplicaApiAuthTokenRevokeResult(bool isSuccess, bool isNotFound, string error)
    {
        IsSuccess = isSuccess;
        IsNotFound = isNotFound;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsNotFound { get; }
    public string Error { get; }

    public static ReplicaApiAuthTokenRevokeResult Success() => new(
        isSuccess: true,
        isNotFound: false,
        error: string.Empty);

    public static ReplicaApiAuthTokenRevokeResult NotFound(string error) => new(
        isSuccess: false,
        isNotFound: true,
        error: error ?? string.Empty);

    public static ReplicaApiAuthTokenRevokeResult Failed(string error) => new(
        isSuccess: false,
        isNotFound: false,
        error: error ?? string.Empty);
}
