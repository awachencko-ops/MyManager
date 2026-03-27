using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Application.Abstractions;
using Replica.Api.Application.Behaviors;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Replica.Shared.Models;
using Xunit;

namespace Replica.VerifyTests;

public sealed class ReplicaApiDualWriteShadowBehaviorTests
{
    [Fact]
    public async Task DualWrite_WhenEnabledAndWriteSucceeds_InvokesShadowWriter()
    {
        var probeWriter = new ProbeShadowWriter();
        var mediator = BuildMediator(
            probeWriter,
            configure: options =>
            {
                options.DualWriteEnabled = true;
                options.ShadowWriteFailurePolicy = ReplicaApiMigrationShadowWriteFailurePolicies.WarnOnly;
            });

        var result = await mediator.Send(new DualWriteProbeCommand("qa-actor"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, probeWriter.CallCount);
        Assert.Equal("dualwrite-probe", probeWriter.LastContext.CommandName);
        Assert.Equal("qa-actor", probeWriter.LastContext.Actor);
    }

    [Fact]
    public async Task DualWrite_WhenDisabled_DoesNotInvokeShadowWriter()
    {
        var probeWriter = new ProbeShadowWriter();
        var mediator = BuildMediator(
            probeWriter,
            configure: options =>
            {
                options.DualWriteEnabled = false;
            });

        var result = await mediator.Send(new DualWriteProbeCommand("qa-actor"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, probeWriter.CallCount);
    }

    [Fact]
    public async Task DualWrite_WhenWriterFailsAndWarnOnly_DoesNotFailCommand()
    {
        var probeWriter = new ProbeShadowWriter
        {
            NextResult = new ReplicaApiHistoryShadowWriteResult(
                IsSuccess: false,
                Error: "simulated-shadow-failure",
                FilePath: string.Empty,
                OrdersCount: 0)
        };
        var mediator = BuildMediator(
            probeWriter,
            configure: options =>
            {
                options.DualWriteEnabled = true;
                options.ShadowWriteFailurePolicy = ReplicaApiMigrationShadowWriteFailurePolicies.WarnOnly;
            });

        var result = await mediator.Send(new DualWriteProbeCommand("qa-actor"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, probeWriter.CallCount);
    }

    [Fact]
    public async Task DualWrite_WhenWriterFailsAndFailCommand_Throws()
    {
        var probeWriter = new ProbeShadowWriter
        {
            NextResult = new ReplicaApiHistoryShadowWriteResult(
                IsSuccess: false,
                Error: "simulated-shadow-failure",
                FilePath: string.Empty,
                OrdersCount: 0)
        };
        var mediator = BuildMediator(
            probeWriter,
            configure: options =>
            {
                options.DualWriteEnabled = true;
                options.ShadowWriteFailurePolicy = ReplicaApiMigrationShadowWriteFailurePolicies.FailCommand;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.Send(new DualWriteProbeCommand("qa-actor")));
        Assert.Equal(1, probeWriter.CallCount);
    }

    private static IMediator BuildMediator(
        ProbeShadowWriter probeWriter,
        Action<ReplicaApiMigrationOptions> configure)
    {
        var services = new ServiceCollection();
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { TraceIdentifier = "test-correlation-id" }
        };

        services.AddSingleton<ILanOrderStore, InMemoryLanOrderStore>();
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<IReplicaApiHistoryShadowWriter>(probeWriter);
        services.AddLogging();
        services.AddMediatR(typeof(ReplicaApiDualWriteShadowBehaviorTests).Assembly);
        services.AddReplicaApiCommandPipeline();
        services.Configure(configure);

        return services
            .BuildServiceProvider()
            .GetRequiredService<IMediator>();
    }

    private sealed record DualWriteProbeCommand(string Actor) : IRequest<StoreOperationResult>, IReplicaApiWriteCommand
    {
        public string CommandName => "dualwrite-probe";
    }

    private sealed class DualWriteProbeHandler : IRequestHandler<DualWriteProbeCommand, StoreOperationResult>
    {
        public Task<StoreOperationResult> Handle(DualWriteProbeCommand request, CancellationToken cancellationToken)
        {
            return Task.FromResult(StoreOperationResult.Success(new SharedOrder
            {
                InternalId = Guid.NewGuid().ToString("N"),
                OrderNumber = "probe-order",
                Version = 1
            }));
        }
    }

    private sealed class ProbeShadowWriter : IReplicaApiHistoryShadowWriter
    {
        public ReplicaApiHistoryShadowWriteResult NextResult { get; set; } = new(
            IsSuccess: true,
            Error: string.Empty,
            FilePath: "probe",
            OrdersCount: 1);

        public int CallCount { get; private set; }
        public ReplicaApiHistoryShadowWriteContext LastContext { get; private set; }

        public Task<ReplicaApiHistoryShadowWriteResult> TryWriteAsync(
            ReplicaApiHistoryShadowWriteContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastContext = context;
            return Task.FromResult(NextResult);
        }
    }
}
