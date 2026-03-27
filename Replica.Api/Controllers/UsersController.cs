using MediatR;
using Microsoft.AspNetCore.Mvc;
using Replica.Api.Application.Users.Commands;
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
    private readonly IMediator? _mediator;

    public UsersController(ILanOrderStore store)
        : this(store, mediator: null)
    {
    }

    public UsersController(ILanOrderStore store, IMediator? mediator)
    {
        _store = store;
        _mediator = mediator;
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
        var actor = GetCurrentActor();
        var result = ExecuteWriteCommand(
            new UpsertUserCommand(request, actor),
            () => _store.UpsertUser(request, actor));
        if (result.IsSuccess && result.User != null)
            return Ok(result.User);

        return BadRequest(new { error = result.Error });
    }

    private UserOperationResult ExecuteWriteCommand(
        IRequest<UserOperationResult> command,
        Func<UserOperationResult> fallback)
    {
        if (_mediator == null)
            return fallback();

        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        return _mediator.Send(command, cancellationToken).GetAwaiter().GetResult();
    }

    private string GetCurrentActor()
    {
        return ReplicaApiCurrentUserContext.Get(HttpContext).Name;
    }
}
