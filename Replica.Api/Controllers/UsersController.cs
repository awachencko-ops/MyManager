using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Application.Abstractions;
using Replica.Api.Application.Users.Commands;
using Replica.Api.Application.Users.Queries;
using Replica.Api.Contracts;
using Replica.Shared.Models;

namespace Replica.Api.Controllers;

[ApiController]
[Route("api/users")]
[ReplicaAuthorize(ReplicaApiRoleNames.Operator)]
public sealed class UsersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IReplicaApiCurrentActorAccessor _currentActorAccessor;

    [ActivatorUtilitiesConstructor]
    public UsersController(IMediator mediator, IReplicaApiCurrentActorAccessor currentActorAccessor)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SharedUser>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SharedUser>> GetUsers()
    {
        var users = ExecuteQuery(new GetUsersQuery(IncludeInactive: false));
        return Ok(users);
    }

    [HttpGet("admin/all")]
    [ReplicaAuthorize(ReplicaApiRoleNames.Admin)]
    [ProducesResponseType(typeof(IReadOnlyList<SharedUser>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<IReadOnlyList<SharedUser>> GetAllUsers()
    {
        var users = ExecuteQuery(new GetUsersQuery(IncludeInactive: true));
        return Ok(users);
    }

    [HttpPost("admin/upsert")]
    [ReplicaAuthorize(ReplicaApiRoleNames.Admin)]
    [ProducesResponseType(typeof(SharedUser), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<SharedUser> UpsertUser([FromBody] UpsertUserRequest request)
    {
        var actor = GetCurrentActor();
        var result = ExecuteWriteCommand(new UpsertUserCommand(request, actor));
        if (result.IsSuccess && result.User != null)
            return Ok(result.User);

        return BadRequest(new { error = result.Error });
    }

    private TResult ExecuteWriteCommand<TResult>(IRequest<TResult> command)
        where TResult : IReplicaApiUserOperationResult
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        return _mediator.Send(command, cancellationToken).GetAwaiter().GetResult();
    }

    private TResponse ExecuteQuery<TResponse>(IRequest<TResponse> query)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        return _mediator.Send(query, cancellationToken).GetAwaiter().GetResult();
    }

    private string GetCurrentActor()
    {
        return _currentActorAccessor.GetCurrentActorName();
    }
}

