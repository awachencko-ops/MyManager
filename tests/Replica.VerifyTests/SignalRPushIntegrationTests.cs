using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Replica.Api.Application.Behaviors;
using Replica.Api.Application.Orders.Commands;
using Replica.Api.Application.Users.Commands;
using Replica.Api.Contracts;
using Replica.Api.Hubs;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Xunit;

namespace Replica.VerifyTests;

public sealed class SignalRPushIntegrationTests
{
    [Fact]
    public async Task CreateOrder_WhenCommandSucceeds_BroadcastsOrderUpdatedToOtherClient()
    {
        await using var harness = await SignalRPushHarness.StartAsync();

        var eventReceivedByClientB = new TaskCompletionSource<LanOrderPushEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.ClientB.On<object>(ReplicaOrderHubEvents.OrderUpdated, payload =>
        {
            var parsed = LanOrderPushEventParser.Parse(ReplicaOrderHubEvents.OrderUpdated, payload, DateTime.UtcNow);
            eventReceivedByClientB.TrySetResult(parsed);
        });

        var createResult = await harness.Mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest { OrderNumber = "SIG-INTEG-1001" },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));

        Assert.True(createResult.IsSuccess);
        Assert.NotNull(createResult.Order);

        var pushedEvent = await eventReceivedByClientB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(createResult.Order!.InternalId, pushedEvent.OrderId);
        Assert.Equal(ReplicaOrderHubEvents.OrderUpdated, pushedEvent.EventType);
    }

    [Fact]
    public async Task DeleteOrder_WhenCommandSucceeds_BroadcastsOrderDeletedToOtherClient()
    {
        await using var harness = await SignalRPushHarness.StartAsync();

        var created = await harness.Mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest { OrderNumber = "SIG-INTEG-1002" },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(created.IsSuccess);
        Assert.NotNull(created.Order);

        var eventReceivedByClientB = new TaskCompletionSource<LanOrderPushEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.ClientB.On<object>(ReplicaOrderHubEvents.OrderDeleted, payload =>
        {
            var parsed = LanOrderPushEventParser.Parse(ReplicaOrderHubEvents.OrderDeleted, payload, DateTime.UtcNow);
            eventReceivedByClientB.TrySetResult(parsed);
        });

        var deleted = await harness.Mediator.Send(new DeleteOrderCommand(
            created.Order!.InternalId,
            new DeleteOrderRequest { ExpectedVersion = created.Order.Version },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));

        Assert.True(deleted.IsSuccess);
        var pushedEvent = await eventReceivedByClientB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(created.Order.InternalId, pushedEvent.OrderId);
        Assert.Equal(ReplicaOrderHubEvents.OrderDeleted, pushedEvent.EventType);
    }

    [Fact]
    public async Task UpsertUser_WhenCommandSucceeds_BroadcastsForceRefreshUsersChangedToOtherClient()
    {
        await using var harness = await SignalRPushHarness.StartAsync();

        var eventReceivedByClientB = new TaskCompletionSource<LanOrderPushEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.ClientB.On<object>(ReplicaOrderHubEvents.ForceRefresh, payload =>
        {
            var parsed = LanOrderPushEventParser.Parse(ReplicaOrderHubEvents.ForceRefresh, payload, DateTime.UtcNow);
            eventReceivedByClientB.TrySetResult(parsed);
        });

        var commandResult = await harness.Mediator.Send(new UpsertUserCommand(
            new UpsertUserRequest
            {
                Name = $"push-user-{Guid.NewGuid():N}",
                Role = ReplicaApiRoles.Operator,
                IsActive = true
            },
            Actor: "Administrator"));

        Assert.True(commandResult.IsSuccess);
        var pushedEvent = await eventReceivedByClientB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ReplicaOrderHubEvents.ForceRefresh, pushedEvent.EventType);
        Assert.Equal("users-changed", pushedEvent.Reason);
        Assert.Equal(string.Empty, pushedEvent.OrderId);
    }

    private sealed class SignalRPushHarness : IAsyncDisposable
    {
        private SignalRPushHarness(
            WebApplication app,
            HubConnection clientA,
            HubConnection clientB)
        {
            App = app;
            ClientA = clientA;
            ClientB = clientB;
            Mediator = app.Services.GetRequiredService<IMediator>();
        }

        public WebApplication App { get; }
        public HubConnection ClientA { get; }
        public HubConnection ClientB { get; }
        public IMediator Mediator { get; }

        public static async Task<SignalRPushHarness> StartAsync()
        {
            var app = BuildTestApplication();
            await app.StartAsync();

            var server = app.GetTestServer();
            var clientA = BuildHubConnection(server);
            var clientB = BuildHubConnection(server);
            await clientA.StartAsync();
            await clientB.StartAsync();

            return new SignalRPushHarness(app, clientA, clientB);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await ClientA.DisposeAsync();
            }
            catch
            {
                // no-op
            }

            try
            {
                await ClientB.DisposeAsync();
            }
            catch
            {
                // no-op
            }

            await App.StopAsync();
            await App.DisposeAsync();
        }

        private static WebApplication BuildTestApplication()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            builder.Services.AddSignalR();
            builder.Services.AddSingleton<ILanOrderStore, InMemoryLanOrderStore>();
            builder.Services.AddSingleton<IReplicaOrderPushPublisher, SignalRReplicaOrderPushPublisher>();
            builder.Services.AddMediatR(typeof(CreateOrderCommand).Assembly);
            builder.Services.AddReplicaApiCommandPipeline();

            var app = builder.Build();
            app.MapHub<ReplicaOrderHub>("/hubs/orders");
            return app;
        }

        private static HubConnection BuildHubConnection(TestServer server)
        {
            return new HubConnectionBuilder()
                .WithUrl(
                    "http://localhost/hubs/orders",
                    options =>
                    {
                        options.Transports = HttpTransportType.LongPolling;
                        options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    })
                .Build();
        }
    }
}
