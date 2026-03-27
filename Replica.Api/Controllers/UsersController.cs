using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Application.Users.Commands;
using Replica.Api.Application.Users.Queries;
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
    private readonly ILanOrderStore? _store;
    private readonly IMediator? _mediator;
    private readonly bool _allowStoreFallback;

    [ActivatorUtilitiesConstructor]
    public UsersController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _store = null;
        _allowStoreFallback = false;
    }

    // Fallback constructor for isolated controller tests.
    public UsersController(ILanOrderStore store)
    {
        _store = store;
        _mediator = null;
        _allowStoreFallback = true;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SharedUser>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SharedUser>> GetUsers()
    {
        var users = ExecuteQuery(
            new GetUsersQuery(IncludeInactive: false),
            () => _store!.GetUsers());
        return Ok(users);
    }

    [HttpGet("admin/all")]
    [ReplicaAuthorize(ReplicaApiRoles.Admin)]
    [ProducesResponseType(typeof(IReadOnlyList<SharedUser>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<IReadOnlyList<SharedUser>> GetAllUsers()
    {
        var users = ExecuteQuery(
            new GetUsersQuery(IncludeInactive: true),
            () => _store!.GetUsers(includeInactive: true));
        return Ok(users);
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
            () => _store!.UpsertUser(request, actor));
        if (result.IsSuccess && result.User != null)
            return Ok(result.User);

        return BadRequest(new { error = result.Error });
    }

    private UserOperationResult ExecuteWriteCommand(
        IRequest<UserOperationResult> command,
        Func<UserOperationResult> fallback)
    {
        if (_mediator != null)
        {
            var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
            return _mediator.Send(command, cancellationToken).GetAwaiter().GetResult();
        }

        if (_allowStoreFallback)
            return fallback();

        throw new InvalidOperationException("UsersController requires IMediator in runtime composition.");
    }

    private TResponse ExecuteQuery<TResponse>(
        IRequest<TResponse> query,
        Func<TResponse> fallback)
    {
        if (_mediator != null)
        {
            var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
            return _mediator.Send(query, cancellationToken).GetAwaiter().GetResult();
        }

        if (_allowStoreFallback)
            return fallback();

        throw new InvalidOperationException("UsersController requires IMediator in runtime composition.");
    }

    private string GetCurrentActor()
    {
        return ReplicaApiCurrentUserContext.Get(HttpContext).Name;
    }
}
