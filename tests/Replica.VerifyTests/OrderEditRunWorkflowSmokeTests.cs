using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Replica.Api.Application.Behaviors;
using Replica.Api.Application.Orders.Commands;
using Replica.Api.Application.Orders.Queries;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Replica.Shared.Models;
using Xunit;
using Xunit.Abstractions;

namespace Replica.VerifyTests;

public sealed class OrderEditRunWorkflowSmokeTests
{
    private readonly ITestOutputHelper _output;

    public OrderEditRunWorkflowSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Smoke_CreateEditPreparedPathsAndRunLifecycle_ServerStateIsConsistent()
    {
        await using var harness = await OrderWorkflowHarness.StartAsync();

        var createRequest = new CreateOrderRequest
        {
            OrderNumber = "SMOKE-EDIT-1001",
            UserName = "Smoke User",
            CreatedById = "smoke-user",
            CreatedByUser = "smoke-user",
            Status = WorkflowStatusNames.Waiting,
            ArrivalDate = new DateTime(2026, 3, 30, 9, 45, 0, DateTimeKind.Local),
            ManagerOrderDate = new DateTime(2026, 3, 30),
            PitStopAction = "-",
            ImposingAction = "-"
        };
        _output.WriteLine("[1] CreateOrder request sent.");

        var created = await harness.Mediator.Send(new CreateOrderCommand(
            createRequest,
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(created.IsSuccess, created.Error);
        Assert.NotNull(created.Order);
        var current = created.Order!;
        _output.WriteLine($"[1] Created order: internalId={current.InternalId}, number={current.OrderNumber}, version={current.Version}");

        var snapshotAfterCreate = await harness.Mediator.Send(new GetOrderByIdQuery(current.InternalId));
        Assert.NotNull(snapshotAfterCreate);
        Assert.Equal("SMOKE-EDIT-1001", snapshotAfterCreate!.OrderNumber);
        Assert.Equal(new DateTime(2026, 3, 30), snapshotAfterCreate.ManagerOrderDate.Date);
        _output.WriteLine("[1] Server snapshot after create is consistent.");

        var addItemRequest = new AddOrderItemRequest
        {
            ExpectedOrderVersion = current.Version,
            Item = new SharedOrderItem
            {
                ItemId = "smoke-item-1",
                SequenceNo = 0,
                ClientFileLabel = "smoke-prepress.pdf",
                FileStatus = WorkflowStatusNames.Waiting,
                PreparedPath = @"\\nas\orders\smoke\prep\smoke-prepress-v1.pdf"
            }
        };
        _output.WriteLine("[2] AddOrderItem request sent (PreparedPath method #1).");

        var addItemResult = await harness.Mediator.Send(new AddOrderItemCommand(
            current.InternalId,
            addItemRequest,
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(addItemResult.IsSuccess, addItemResult.Error);
        Assert.NotNull(addItemResult.Order);
        current = addItemResult.Order!;
        var item = current.Items.Single(x => x.ItemId == "smoke-item-1");
        Assert.Equal(@"\\nas\orders\smoke\prep\smoke-prepress-v1.pdf", item.PreparedPath);
        _output.WriteLine($"[2] Added item: itemId={item.ItemId}, orderVersion={current.Version}, itemVersion={item.Version}");

        var updateItemRequest = new UpdateOrderItemRequest
        {
            ExpectedOrderVersion = current.Version,
            ExpectedItemVersion = item.Version,
            PreparedPath = @"\\nas\orders\smoke\prep\smoke-prepress-v2.pdf"
        };
        _output.WriteLine("[3] UpdateOrderItem request sent (PreparedPath method #2).");

        var updateItemResult = await harness.Mediator.Send(new UpdateOrderItemCommand(
            current.InternalId,
            item.ItemId,
            updateItemRequest,
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(updateItemResult.IsSuccess, updateItemResult.Error);
        Assert.NotNull(updateItemResult.Order);
        current = updateItemResult.Order!;
        item = current.Items.Single(x => x.ItemId == "smoke-item-1");
        Assert.Equal(@"\\nas\orders\smoke\prep\smoke-prepress-v2.pdf", item.PreparedPath);
        _output.WriteLine($"[3] Updated item PreparedPath: itemId={item.ItemId}, orderVersion={current.Version}, itemVersion={item.Version}");

        var updateOrderRequest = new UpdateOrderRequest
        {
            ExpectedVersion = current.Version,
            OrderNumber = "SMOKE-EDIT-2002",
            ManagerOrderDate = new DateTime(2026, 4, 1)
        };
        _output.WriteLine("[4] UpdateOrder request sent (number/date edit).");

        var updateOrderResult = await harness.Mediator.Send(new UpdateOrderCommand(
            current.InternalId,
            updateOrderRequest,
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(updateOrderResult.IsSuccess, updateOrderResult.Error);
        Assert.NotNull(updateOrderResult.Order);
        current = updateOrderResult.Order!;
        Assert.Equal("SMOKE-EDIT-2002", current.OrderNumber);
        Assert.Equal(new DateTime(2026, 4, 1), current.ManagerOrderDate.Date);
        _output.WriteLine($"[4] Order edited: number={current.OrderNumber}, managerOrderDate={current.ManagerOrderDate:yyyy-MM-dd}, version={current.Version}");

        var snapshotAfterEdit = await harness.Mediator.Send(new GetOrderByIdQuery(current.InternalId));
        Assert.NotNull(snapshotAfterEdit);
        Assert.Equal("SMOKE-EDIT-2002", snapshotAfterEdit!.OrderNumber);
        Assert.Equal(new DateTime(2026, 4, 1), snapshotAfterEdit.ManagerOrderDate.Date);
        Assert.Equal(@"\\nas\orders\smoke\prep\smoke-prepress-v2.pdf", snapshotAfterEdit.Items.Single(x => x.ItemId == "smoke-item-1").PreparedPath);
        _output.WriteLine("[4] Server snapshot after edit is consistent.");

        var runStart = await harness.Mediator.Send(new StartOrderRunCommand(
            current.InternalId,
            new RunOrderRequest { ExpectedOrderVersion = current.Version },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(runStart.IsSuccess, runStart.Error);
        Assert.NotNull(runStart.Order);
        current = runStart.Order!;
        Assert.Equal("Processing", current.Status);
        _output.WriteLine($"[5] Run started: status={current.Status}, version={current.Version}");

        var runStop = await harness.Mediator.Send(new StopOrderRunCommand(
            current.InternalId,
            new StopOrderRequest { ExpectedOrderVersion = current.Version },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(runStop.IsSuccess, runStop.Error);
        Assert.NotNull(runStop.Order);
        current = runStop.Order!;
        Assert.Equal("Cancelled", current.Status);
        _output.WriteLine($"[6] Run stopped: status={current.Status}, version={current.Version}");

        var finalSnapshot = await harness.Mediator.Send(new GetOrderByIdQuery(current.InternalId));
        Assert.NotNull(finalSnapshot);
        Assert.Equal("SMOKE-EDIT-2002", finalSnapshot!.OrderNumber);
        Assert.Equal("Cancelled", finalSnapshot.Status);
        Assert.Equal(@"\\nas\orders\smoke\prep\smoke-prepress-v2.pdf", finalSnapshot.Items.Single(x => x.ItemId == "smoke-item-1").PreparedPath);
        _output.WriteLine("[7] Final server snapshot is consistent.");
    }

    [Fact]
    public async Task Smoke_ReplacePrintFileAndMoveToArchivePath_ServerStateIsConsistent()
    {
        await using var harness = await OrderWorkflowHarness.StartAsync();

        var createResult = await harness.Mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest
            {
                OrderNumber = "SMOKE-PRINT-1001",
                UserName = "Smoke User",
                CreatedById = "smoke-user",
                CreatedByUser = "smoke-user",
                Status = WorkflowStatusNames.Waiting,
                ArrivalDate = new DateTime(2026, 3, 30, 10, 0, 0, DateTimeKind.Local),
                ManagerOrderDate = new DateTime(2026, 3, 30)
            },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(createResult.IsSuccess, createResult.Error);
        Assert.NotNull(createResult.Order);
        var current = createResult.Order!;

        var addItemResult = await harness.Mediator.Send(new AddOrderItemCommand(
            current.InternalId,
            new AddOrderItemRequest
            {
                ExpectedOrderVersion = current.Version,
                Item = new SharedOrderItem
                {
                    ItemId = "smoke-print-item-1",
                    SequenceNo = 0,
                    ClientFileLabel = "print-a.pdf",
                    FileStatus = WorkflowStatusNames.Waiting,
                    PrintPath = @"\\nas\orders\smoke\print\SMOKE-PRINT-1001-v1.pdf"
                }
            },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(addItemResult.IsSuccess, addItemResult.Error);
        Assert.NotNull(addItemResult.Order);
        current = addItemResult.Order!;
        var item = current.Items.Single(x => x.ItemId == "smoke-print-item-1");
        Assert.Equal(@"\\nas\orders\smoke\print\SMOKE-PRINT-1001-v1.pdf", item.PrintPath);
        _output.WriteLine("[print] Initial print path created.");

        var replacePrintResult = await harness.Mediator.Send(new UpdateOrderItemCommand(
            current.InternalId,
            item.ItemId,
            new UpdateOrderItemRequest
            {
                ExpectedOrderVersion = current.Version,
                ExpectedItemVersion = item.Version,
                PrintPath = @"\\nas\orders\smoke\print\SMOKE-PRINT-1001-v2.pdf"
            },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(replacePrintResult.IsSuccess, replacePrintResult.Error);
        Assert.NotNull(replacePrintResult.Order);
        current = replacePrintResult.Order!;
        item = current.Items.Single(x => x.ItemId == "smoke-print-item-1");
        Assert.Equal(@"\\nas\orders\smoke\print\SMOKE-PRINT-1001-v2.pdf", item.PrintPath);
        _output.WriteLine("[print] Print path replaced to v2.");

        var moveToArchiveResult = await harness.Mediator.Send(new UpdateOrderItemCommand(
            current.InternalId,
            item.ItemId,
            new UpdateOrderItemRequest
            {
                ExpectedOrderVersion = current.Version,
                ExpectedItemVersion = item.Version,
                PrintPath = @"\\nas\archive\done\SMOKE-PRINT-1001-v2.pdf"
            },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(moveToArchiveResult.IsSuccess, moveToArchiveResult.Error);
        Assert.NotNull(moveToArchiveResult.Order);
        current = moveToArchiveResult.Order!;
        _output.WriteLine("[archive] Print path moved to archive path.");

        var finalSnapshot = await harness.Mediator.Send(new GetOrderByIdQuery(current.InternalId));
        Assert.NotNull(finalSnapshot);
        var finalItem = finalSnapshot!.Items.Single(x => x.ItemId == "smoke-print-item-1");
        Assert.Equal(@"\\nas\archive\done\SMOKE-PRINT-1001-v2.pdf", finalItem.PrintPath);
    }

    [Fact]
    public async Task Smoke_ClearPreparedAndPrintPaths_LogicalDeleteInProgram_ServerStateIsConsistent()
    {
        await using var harness = await OrderWorkflowHarness.StartAsync();

        var createResult = await harness.Mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest
            {
                OrderNumber = "SMOKE-CLEAR-1001",
                UserName = "Smoke User",
                CreatedById = "smoke-user",
                CreatedByUser = "smoke-user",
                Status = WorkflowStatusNames.Waiting,
                ArrivalDate = new DateTime(2026, 3, 30, 10, 10, 0, DateTimeKind.Local),
                ManagerOrderDate = new DateTime(2026, 3, 30)
            },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(createResult.IsSuccess, createResult.Error);
        Assert.NotNull(createResult.Order);
        var current = createResult.Order!;

        var addItemResult = await harness.Mediator.Send(new AddOrderItemCommand(
            current.InternalId,
            new AddOrderItemRequest
            {
                ExpectedOrderVersion = current.Version,
                Item = new SharedOrderItem
                {
                    ItemId = "smoke-clear-item-1",
                    SequenceNo = 0,
                    ClientFileLabel = "clear.pdf",
                    FileStatus = WorkflowStatusNames.Waiting,
                    PreparedPath = @"\\nas\orders\smoke\prep\SMOKE-CLEAR-1001-prepared.pdf",
                    PrintPath = @"\\nas\orders\smoke\print\SMOKE-CLEAR-1001-print.pdf"
                }
            },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(addItemResult.IsSuccess, addItemResult.Error);
        Assert.NotNull(addItemResult.Order);
        current = addItemResult.Order!;
        var item = current.Items.Single(x => x.ItemId == "smoke-clear-item-1");
        Assert.False(string.IsNullOrWhiteSpace(item.PreparedPath));
        Assert.False(string.IsNullOrWhiteSpace(item.PrintPath));
        _output.WriteLine("[clear] Initial prepared/print paths created.");

        var clearPathsResult = await harness.Mediator.Send(new UpdateOrderItemCommand(
            current.InternalId,
            item.ItemId,
            new UpdateOrderItemRequest
            {
                ExpectedOrderVersion = current.Version,
                ExpectedItemVersion = item.Version,
                PreparedPath = string.Empty,
                PrintPath = string.Empty
            },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(clearPathsResult.IsSuccess, clearPathsResult.Error);
        Assert.NotNull(clearPathsResult.Order);
        current = clearPathsResult.Order!;
        _output.WriteLine("[clear] Prepared/print paths cleared via API update.");

        var finalSnapshot = await harness.Mediator.Send(new GetOrderByIdQuery(current.InternalId));
        Assert.NotNull(finalSnapshot);
        var finalItem = finalSnapshot!.Items.Single(x => x.ItemId == "smoke-clear-item-1");
        Assert.True(string.IsNullOrEmpty(finalItem.PreparedPath));
        Assert.True(string.IsNullOrEmpty(finalItem.PrintPath));
        Assert.Single(finalSnapshot.Items);
    }

    [Fact]
    public async Task Smoke_EditOrderNumberAndDateTwice_ServerStateIsConsistent()
    {
        await using var harness = await OrderWorkflowHarness.StartAsync();

        var createResult = await harness.Mediator.Send(new CreateOrderCommand(
            new CreateOrderRequest
            {
                OrderNumber = "SMOKE-EDIT-TWICE-1001",
                UserName = "Smoke User",
                CreatedById = "smoke-user",
                CreatedByUser = "smoke-user",
                Status = WorkflowStatusNames.Waiting,
                ArrivalDate = new DateTime(2026, 3, 30, 10, 20, 0, DateTimeKind.Local),
                ManagerOrderDate = new DateTime(2026, 3, 30)
            },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(createResult.IsSuccess, createResult.Error);
        Assert.NotNull(createResult.Order);
        var current = createResult.Order!;

        var firstEditResult = await harness.Mediator.Send(new UpdateOrderCommand(
            current.InternalId,
            new UpdateOrderRequest
            {
                ExpectedVersion = current.Version,
                OrderNumber = "SMOKE-EDIT-TWICE-2002",
                ManagerOrderDate = new DateTime(2026, 4, 1)
            },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(firstEditResult.IsSuccess, firstEditResult.Error);
        Assert.NotNull(firstEditResult.Order);
        current = firstEditResult.Order!;
        Assert.Equal("SMOKE-EDIT-TWICE-2002", current.OrderNumber);
        Assert.Equal(new DateTime(2026, 4, 1), current.ManagerOrderDate.Date);
        _output.WriteLine("[edit] First number/date edit applied.");

        var secondEditResult = await harness.Mediator.Send(new UpdateOrderCommand(
            current.InternalId,
            new UpdateOrderRequest
            {
                ExpectedVersion = current.Version,
                OrderNumber = "SMOKE-EDIT-TWICE-3003",
                ManagerOrderDate = new DateTime(2026, 4, 2)
            },
            Actor: "Administrator",
            IdempotencyKey: string.Empty));
        Assert.True(secondEditResult.IsSuccess, secondEditResult.Error);
        Assert.NotNull(secondEditResult.Order);
        current = secondEditResult.Order!;

        var finalSnapshot = await harness.Mediator.Send(new GetOrderByIdQuery(current.InternalId));
        Assert.NotNull(finalSnapshot);
        Assert.Equal("SMOKE-EDIT-TWICE-3003", finalSnapshot!.OrderNumber);
        Assert.Equal(new DateTime(2026, 4, 2), finalSnapshot.ManagerOrderDate.Date);
        _output.WriteLine("[edit] Second number/date edit persisted on server.");
    }

    private sealed class OrderWorkflowHarness : IAsyncDisposable
    {
        private OrderWorkflowHarness(WebApplication app)
        {
            App = app;
            Mediator = app.Services.GetRequiredService<IMediator>();
        }

        public WebApplication App { get; }
        public IMediator Mediator { get; }

        public static async Task<OrderWorkflowHarness> StartAsync()
        {
            var app = BuildTestApplication();
            await app.StartAsync();
            return new OrderWorkflowHarness(app);
        }

        public async ValueTask DisposeAsync()
        {
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

            builder.Services.AddSingleton<ILanOrderStore, InMemoryLanOrderStore>();
            builder.Services.AddSingleton<IReplicaOrderPushPublisher, NoOpReplicaOrderPushPublisher>();
            builder.Services.AddMediatR(typeof(CreateOrderCommand).Assembly);
            builder.Services.AddReplicaApiCommandPipeline();

            var app = builder.Build();
            return app;
        }
    }
}
