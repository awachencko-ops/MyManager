using Microsoft.AspNetCore.Mvc;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ReplicaAuthorize(ReplicaApiRoles.Operator)]
public sealed class AuthController : ControllerBase
{
    [HttpGet("me")]
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
            CanManageUsers = ReplicaApiRoles.IsInRole(currentUser.Role, ReplicaApiRoles.Admin)
        });
    }
}
