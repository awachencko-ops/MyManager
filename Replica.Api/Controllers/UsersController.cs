using Microsoft.AspNetCore.Mvc;
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
}
