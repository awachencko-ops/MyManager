using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Replica.Api.Application.Abstractions;
using Replica.Api.Application.Behaviors;
using Replica.Api.Application.Orders.Commands;
using Replica.Api.Application.Users.Commands;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Replica.Shared.Models;
using Xunit;

namespace Replica.VerifyTests;

public sealed class MediatRCommandPipelineBehaviorsTests
{
    [Fact]
    public async Task PipelineValidation_WhenCreateOrderHasNoOrderNumber_ReturnsBadRequest()
    {
        var mediator = BuildMediator();

        var result = await mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest { OrderNumber = "" },
            Actor: "Administrator",
            IdempotencyKey: ""));

        Assert.True(result.IsBadRequest);
        Assert.Equal("order number is required", result.Error);
    }

    [Fact]
    public async Task PipelineValidation_WhenUpsertUserHasNoName_ReturnsBadRequest()
    {
        var mediator = BuildMediator();

        var result = await mediator.Send(new UpsertUserCommand(
            new UpsertUserRequest { Name = " ", Role = ReplicaApiRoles.Admin },
            Actor: "Administrator"));

        Assert.True(result.IsBadRequest);
        Assert.Equal("user name is required", result.Error);
    }

    [Fact]
    public async Task PipelineTelemetry_WhenCreateOrderSucceeds_RecordsWriteMetric()
    {
        var mediator = BuildMediator();
        var before = ReplicaApiObservability.GetSnapshot();
        var beforeCommandTotal = GetCommandWriteTotal(before, "create-order");

        var result = await mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest { OrderNumber = "MDR-PIPE-001" },
            Actor: "Administrator",
            IdempotencyKey: ""));

        var after = ReplicaApiObservability.GetSnapshot();
        var afterCommandTotal = GetCommandWriteTotal(after, "create-order");

        Assert.True(result.IsSuccess);
        Assert.True(after.WriteCommandsTotal >= before.WriteCommandsTotal + 1);
        Assert.True(afterCommandTotal >= beforeCommandTotal + 1);
    }

    [Fact]
    public async Task PipelineIdempotency_WhenKeyIsTooLong_ReturnsBadRequest()
    {
        var mediator = BuildMediator();
        var idempotencyKey = new string('k', 129);

        var result = await mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest { OrderNumber = "MDR-PIPE-002" },
            Actor: "Administrator",
            IdempotencyKey: idempotencyKey));

        Assert.True(result.IsBadRequest);
        Assert.Equal("idempotency key length must be <= 128", result.Error);
    }

    [Fact]
    public async Task PipelineTransaction_WhenConcurrentWriteCommands_ExecutesSerially()
    {
        TransactionProbeHandler.Reset();
        var mediator = BuildMediator(enableSerializedWriteGate: true);

        var first = mediator.Send(new TransactionProbeWriteCommand("probe-1"));
        var second = mediator.Send(new TransactionProbeWriteCommand("probe-2"));
        await Task.WhenAll(first, second);

        Assert.Equal(1, TransactionProbeHandler.MaxConcurrentExecutions);
        Assert.True(first.Result.IsSuccess);
        Assert.True(second.Result.IsSuccess);
    }

    [Fact]
    public async Task PipelineTransaction_WhenSerializedGateDisabled_AllowsOverlap()
    {
        TransactionProbeHandler.Reset();
        var mediator = BuildMediator(enableSerializedWriteGate: false);

        var jobs = Enumerable.Range(1, 6)
            .Select(i => mediator.Send(new TransactionProbeWriteCommand($"probe-open-{i}")))
            .ToArray();
        await Task.WhenAll(jobs);

        Assert.True(TransactionProbeHandler.MaxConcurrentExecutions >= 2);
    }

    private static IMediator BuildMediator(bool enableSerializedWriteGate = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILanOrderStore, InMemoryLanOrderStore>();
        services.AddMediatR(typeof(CreateOrderCommand).Assembly, typeof(MediatRCommandPipelineBehaviorsTests).Assembly);
        services.AddReplicaApiCommandPipeline();
        services.Configure<ReplicaApiCommandPipelineOptions>(options =>
        {
            options.EnableSerializedWriteGate = enableSerializedWriteGate;
        });

        return services
            .BuildServiceProvider()
            .GetRequiredService<IMediator>();
    }

    private static long GetCommandWriteTotal(ReplicaApiObservabilitySnapshot snapshot, string commandName)
    {
        if (!snapshot.Commands.TryGetValue(commandName, out var commandSnapshot))
            return 0;

        return commandSnapshot.WriteTotal;
    }

    private sealed record TransactionProbeWriteCommand(string Name) : IRequest<StoreOperationResult>, IReplicaApiWriteCommand
    {
        public string CommandName => "transaction-probe";
    }

    private sealed class TransactionProbeHandler : IRequestHandler<TransactionProbeWriteCommand, StoreOperationResult>
    {
        private static int _currentExecutions;
        private static int _maxConcurrentExecutions;

        public static int MaxConcurrentExecutions => _maxConcurrentExecutions;

        public static void Reset()
        {
            _currentExecutions = 0;
            _maxConcurrentExecutions = 0;
        }

        public async Task<StoreOperationResult> Handle(TransactionProbeWriteCommand request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _currentExecutions);
            InterlockedExtensions.UpdateMax(ref _maxConcurrentExecutions, current);

            try
            {
                await Task.Delay(80, cancellationToken);
                return StoreOperationResult.Success(new SharedOrder
                {
                    InternalId = request.Name,
                    OrderNumber = request.Name,
                    Version = 1
                });
            }
            finally
            {
                Interlocked.Decrement(ref _currentExecutions);
            }
        }
    }

    private static class InterlockedExtensions
    {
        public static void UpdateMax(ref int target, int value)
        {
            while (true)
            {
                var snapshot = target;
                if (snapshot >= value)
                    return;

                if (Interlocked.CompareExchange(ref target, value, snapshot) == snapshot)
                    return;
            }
        }
    }
}
