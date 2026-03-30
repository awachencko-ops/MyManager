using Replica.Api.Application.Abstractions;
using Replica.Api.Services;
using Replica.Shared.Models;

namespace Replica.Api.Infrastructure;

public sealed class ReplicaApiAuthServiceAdapter : IReplicaApiAuthService
{
    private readonly ILanOrderStore _store;
    private readonly IReplicaApiTokenService _tokenService;

    public ReplicaApiAuthServiceAdapter(ILanOrderStore store, IReplicaApiTokenService tokenService)
    {
        _store = store;
        _tokenService = tokenService;
    }

    public IReadOnlyList<SharedUser> GetUsers(bool includeInactive)
    {
        return _store.GetUsers(includeInactive);
    }

    public ReplicaApiAuthTokenIssueResult IssueToken(
        string userName,
        string role,
        string issuedBy,
        string ipAddress,
        string userAgent)
    {
        var result = _tokenService.IssueToken(userName, role, issuedBy, ipAddress, userAgent);
        return MapIssueResult(result);
    }

    public ReplicaApiAuthTokenIssueResult RefreshToken(
        string sessionId,
        string requestedBy,
        string ipAddress,
        string userAgent)
    {
        var result = _tokenService.RefreshToken(sessionId, requestedBy, ipAddress, userAgent);
        return MapIssueResult(result);
    }

    public ReplicaApiAuthTokenRevokeResult RevokeToken(
        string sessionId,
        string requestedBy,
        string ipAddress,
        string userAgent)
    {
        var result = _tokenService.RevokeToken(sessionId, requestedBy, ipAddress, userAgent);
        if (result.IsSuccess)
            return ReplicaApiAuthTokenRevokeResult.Success();
        if (result.IsNotFound)
            return ReplicaApiAuthTokenRevokeResult.NotFound(result.Error);

        return ReplicaApiAuthTokenRevokeResult.Failed(result.Error);
    }

    private static ReplicaApiAuthTokenIssueResult MapIssueResult(ReplicaApiTokenIssueResult source)
    {
        if (source.IsSuccess)
        {
            return ReplicaApiAuthTokenIssueResult.Success(
                source.AccessToken,
                source.SessionId,
                source.UserName,
                source.Role,
                source.ExpiresAtUtc);
        }

        if (source.IsNotFound)
            return ReplicaApiAuthTokenIssueResult.NotFound(source.Error);

        return ReplicaApiAuthTokenIssueResult.Failed(source.Error);
    }
}
