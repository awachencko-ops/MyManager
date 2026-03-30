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
    public async Task<ActionResult<IReadOnlyList<SharedUser>>> GetUsers()
    {
        var users = await ExecuteQueryAsync(new GetUsersQuery(IncludeInactive: false));
        return Ok(users);
    }

    [HttpGet("admin/all")]
    [ReplicaAuthorize(ReplicaApiRoleNames.Admin)]
    [ProducesResponseType(typeof(IReadOnlyList<SharedUser>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<SharedUser>>> GetAllUsers()
    {
        var users = await ExecuteQueryAsync(new GetUsersQuery(IncludeInactive: true));
        return Ok(users);
    }

    [HttpPost("admin/upsert")]
    [ReplicaAuthorize(ReplicaApiRoleNames.Admin)]
    [ProducesResponseType(typeof(SharedUser), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SharedUser>> UpsertUser([FromBody] UpsertUserRequest request)
    {
        var actor = GetCurrentActor();
        var result = await ExecuteWriteCommandAsync(new UpsertUserCommand(request, actor));
        if (result.IsSuccess && result.User != null)
            return Ok(result.User);

        return BadRequest(new { error = result.Error });
    }

    private Task<TResult> ExecuteWriteCommandAsync<TResult>(IRequest<TResult> command)
        where TResult : IReplicaApiUserOperationResult
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        return _mediator.Send(command, cancellationToken);
    }

    private Task<TResponse> ExecuteQueryAsync<TResponse>(IRequest<TResponse> query)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        return _mediator.Send(query, cancellationToken);
    }

    private string GetCurrentActor()
    {
        return _currentActorAccessor.GetCurrentActorName();
    }
}

