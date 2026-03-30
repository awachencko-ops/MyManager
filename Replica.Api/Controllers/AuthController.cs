using Microsoft.AspNetCore.Mvc;
using Replica.Api.Application.Abstractions;
using Replica.Api.Contracts;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IReplicaApiAuthService _authService;
    private readonly IReplicaApiCurrentUserAccessor _currentUserAccessor;

    public AuthController(IReplicaApiAuthService authService, IReplicaApiCurrentUserAccessor currentUserAccessor)
    {
        _authService = authService;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet("me")]
    [Replica.Api.Infrastructure.ReplicaAuthorize(Replica.Api.Infrastructure.ReplicaApiRoles.Operator)]
    [ProducesResponseType(typeof(AuthMeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<AuthMeResponse> GetCurrentUser()
    {
        var currentUser = _currentUserAccessor.GetCurrentUser();
        return Ok(new AuthMeResponse
        {
            Name = currentUser.Name,
            Role = currentUser.Role,
            IsAuthenticated = currentUser.IsAuthenticated,
            IsValidated = currentUser.IsValidated,
            CanManageUsers = currentUser.CanManageUsers,
            AuthScheme = currentUser.AuthScheme,
            SessionId = currentUser.SessionId
        });
    }

    [HttpPost("login")]
    [Replica.Api.Infrastructure.ReplicaAuthorize(Replica.Api.Infrastructure.ReplicaApiRoles.Operator)]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<AuthTokenResponse> Login([FromBody] AuthLoginRequest? request)
    {
        var currentUser = _currentUserAccessor.GetCurrentUser();
        var requestedUserName = request?.UserName?.Trim() ?? string.Empty;
        var targetUserName = string.IsNullOrWhiteSpace(requestedUserName)
            ? currentUser.Name
            : requestedUserName;
        if (string.IsNullOrWhiteSpace(targetUserName))
            return BadRequest(new { error = "user name is required" });

        if (!string.Equals(targetUserName, currentUser.Name, StringComparison.OrdinalIgnoreCase) && !currentUser.CanManageUsers)
            return Forbid();

        var knownUsers = _authService.GetUsers(includeInactive: true);
        var targetUser = knownUsers.FirstOrDefault(user =>
            !string.IsNullOrWhiteSpace(user.Name)
            && string.Equals(user.Name.Trim(), targetUserName, StringComparison.OrdinalIgnoreCase));
        if (targetUser == null || !targetUser.IsActive)
            return Forbid();

        var issueResult = _authService.IssueToken(
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
    [Replica.Api.Infrastructure.ReplicaAuthorize(Replica.Api.Infrastructure.ReplicaApiRoles.Operator)]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AuthTokenResponse> Refresh([FromBody] AuthRefreshRequest? request)
    {
        var currentUser = _currentUserAccessor.GetCurrentUser();
        var requestedSessionId = request?.SessionId?.Trim() ?? string.Empty;
        var targetSessionId = string.IsNullOrWhiteSpace(requestedSessionId)
            ? currentUser.SessionId
            : requestedSessionId;
        if (string.IsNullOrWhiteSpace(targetSessionId))
            return BadRequest(new { error = "refresh requires bearer/api-key session id" });

        if (!string.Equals(targetSessionId, currentUser.SessionId, StringComparison.Ordinal) && !currentUser.CanManageUsers)
            return Forbid();

        var refreshResult = _authService.RefreshToken(
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
    [Replica.Api.Infrastructure.ReplicaAuthorize(Replica.Api.Infrastructure.ReplicaApiRoles.Operator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Revoke([FromBody] AuthRevokeRequest? request)
    {
        var currentUser = _currentUserAccessor.GetCurrentUser();
        var requestedSessionId = request?.SessionId?.Trim() ?? string.Empty;
        var targetSessionId = string.IsNullOrWhiteSpace(requestedSessionId)
            ? currentUser.SessionId
            : requestedSessionId;
        if (string.IsNullOrWhiteSpace(targetSessionId))
            return BadRequest(new { error = "session id is required" });

        if (!string.Equals(targetSessionId, currentUser.SessionId, StringComparison.Ordinal) && !currentUser.CanManageUsers)
            return Forbid();

        var revokeResult = _authService.RevokeToken(
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

    private static AuthTokenResponse ToTokenResponse(ReplicaApiAuthTokenIssueResult source)
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
