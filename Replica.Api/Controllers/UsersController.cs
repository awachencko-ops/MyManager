using Microsoft.AspNetCore.Mvc;
using Replica.Api.Services;
using Replica.Shared.Models;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly InMemoryLanOrderStore _store;

    public UsersController(InMemoryLanOrderStore store)
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
