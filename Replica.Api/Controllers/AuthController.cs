using Microsoft.AspNetCore.Mvc;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ILanOrderStore _store;
    private readonly IReplicaApiTokenService _tokenService;

    public AuthController(ILanOrderStore store, IReplicaApiTokenService tokenService)
    {
        _store = store;
        _tokenService = tokenService;
    }

    [HttpGet("me")]
    [ReplicaAuthorize(ReplicaApiRoles.Operator)]
    [ProducesResponseType(typeof(AuthMeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<AuthMeResponse> GetCurrentUser()
    {
        var currentUser = ReplicaApiCurrentUserContext.Get(HttpContext);
        return Ok(new AuthMeResponse
        {
            Name = currentUser.Name,
            Role = currentUser.Role,
            IsAuthenticated = currentUser.IsAuthenticated,
            IsValidated = currentUser.IsValidated,
            CanManageUsers = ReplicaApiRoles.IsInRole(currentUser.Role, ReplicaApiRoles.Admin),
            AuthScheme = currentUser.AuthScheme,
            SessionId = currentUser.SessionId
        });
    }

    [HttpPost("login")]
    [ReplicaAuthorize(ReplicaApiRoles.Operator)]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<AuthTokenResponse> Login([FromBody] AuthLoginRequest? request)
    {
        var currentUser = ReplicaApiCurrentUserContext.Get(HttpContext);
        var requestedUserName = request?.UserName?.Trim() ?? string.Empty;
        var targetUserName = string.IsNullOrWhiteSpace(requestedUserName)
            ? currentUser.Name
            : requestedUserName;
        if (string.IsNullOrWhiteSpace(targetUserName))
            return BadRequest(new { error = "user name is required" });

        var currentUserIsAdmin = ReplicaApiRoles.IsInRole(currentUser.Role, ReplicaApiRoles.Admin);
        if (!string.Equals(targetUserName, currentUser.Name, StringComparison.OrdinalIgnoreCase) && !currentUserIsAdmin)
            return Forbid();

        var knownUsers = _store.GetUsers(includeInactive: true);
        var targetUser = knownUsers.FirstOrDefault(user =>
            !string.IsNullOrWhiteSpace(user.Name)
            && string.Equals(user.Name.Trim(), targetUserName, StringComparison.OrdinalIgnoreCase));
        if (targetUser == null || !targetUser.IsActive)
            return Forbid();

        var issueResult = _tokenService.IssueToken(
            targetUser.Name.Trim(),
            targetUser.Role,
            currentUser.Name,
            ResolveClientIpAddress(),
            ResolveUserAgent());
        if (!issueResult.IsSuccess)
            return BadRequest(new { error = issueResult.Error });

        return Ok(ToTokenResponse(issueResult));
    }

    [HttpPost("refresh")]
    [ReplicaAuthorize(ReplicaApiRoles.Operator)]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AuthTokenResponse> Refresh([FromBody] AuthRefreshRequest? request)
    {
        var currentUser = ReplicaApiCurrentUserContext.Get(HttpContext);
        var requestedSessionId = request?.SessionId?.Trim() ?? string.Empty;
        var targetSessionId = string.IsNullOrWhiteSpace(requestedSessionId)
            ? currentUser.SessionId
            : requestedSessionId;
        if (string.IsNullOrWhiteSpace(targetSessionId))
            return BadRequest(new { error = "refresh requires bearer/api-key session id" });

        var currentUserIsAdmin = ReplicaApiRoles.IsInRole(currentUser.Role, ReplicaApiRoles.Admin);
        if (!string.Equals(targetSessionId, currentUser.SessionId, StringComparison.Ordinal) && !currentUserIsAdmin)
            return Forbid();

        var refreshResult = _tokenService.RefreshToken(
            targetSessionId,
            currentUser.Name,
            ResolveClientIpAddress(),
            ResolveUserAgent());
        if (!refreshResult.IsSuccess)
        {
            if (refreshResult.IsNotFound)
                return NotFound(new { error = refreshResult.Error });

            return BadRequest(new { error = refreshResult.Error });
        }

        return Ok(ToTokenResponse(refreshResult));
    }

    [HttpPost("revoke")]
    [ReplicaAuthorize(ReplicaApiRoles.Operator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Revoke([FromBody] AuthRevokeRequest? request)
    {
        var currentUser = ReplicaApiCurrentUserContext.Get(HttpContext);
        var requestedSessionId = request?.SessionId?.Trim() ?? string.Empty;
        var targetSessionId = string.IsNullOrWhiteSpace(requestedSessionId)
            ? currentUser.SessionId
            : requestedSessionId;
        if (string.IsNullOrWhiteSpace(targetSessionId))
            return BadRequest(new { error = "session id is required" });

        var currentUserIsAdmin = ReplicaApiRoles.IsInRole(currentUser.Role, ReplicaApiRoles.Admin);
        if (!string.Equals(targetSessionId, currentUser.SessionId, StringComparison.Ordinal) && !currentUserIsAdmin)
            return Forbid();

        var revokeResult = _tokenService.RevokeToken(
            targetSessionId,
            currentUser.Name,
            ResolveClientIpAddress(),
            ResolveUserAgent());
        if (!revokeResult.IsSuccess)
        {
            if (revokeResult.IsNotFound)
                return NotFound(new { error = revokeResult.Error });

            return BadRequest(new { error = revokeResult.Error });
        }

        return NoContent();
    }

    private static AuthTokenResponse ToTokenResponse(ReplicaApiTokenIssueResult source)
    {
        return new AuthTokenResponse
        {
            AccessToken = source.AccessToken,
            TokenType = "Bearer",
            ExpiresAtUtc = source.ExpiresAtUtc,
            Name = source.UserName,
            Role = source.Role,
            SessionId = source.SessionId
        };
    }

    private string ResolveClientIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }

    private string ResolveUserAgent()
    {
        return Request.Headers.TryGetValue("User-Agent", out var values)
            ? values.ToString()
            : string.Empty;
    }
}
