using Microsoft.AspNetCore.Mvc;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Replica.Shared.Models;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/users")]
[ReplicaAuthorize(ReplicaApiRoles.Operator)]
public sealed class UsersController : ControllerBase
{
    private readonly ILanOrderStore _store;

    public UsersController(ILanOrderStore store)
    {
        _store = store;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SharedUser>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SharedUser>> GetUsers()
    {
        return Ok(_store.GetUsers());
    }

    [HttpGet("admin/all")]
    [ReplicaAuthorize(ReplicaApiRoles.Admin)]
    [ProducesResponseType(typeof(IReadOnlyList<SharedUser>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<IReadOnlyList<SharedUser>> GetAllUsers()
    {
        return Ok(_store.GetUsers(includeInactive: true));
    }

    [HttpPost("admin/upsert")]
    [ReplicaAuthorize(ReplicaApiRoles.Admin)]
    [ProducesResponseType(typeof(SharedUser), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<SharedUser> UpsertUser([FromBody] UpsertUserRequest request)
    {
        var result = _store.UpsertUser(request, GetCurrentActor());
        if (result.IsSuccess && result.User != null)
            return Ok(result.User);

        return BadRequest(new { error = result.Error });
    }

    private string GetCurrentActor()
    {
        return ReplicaApiCurrentUserContext.Get(HttpContext).Name;
    }
}
