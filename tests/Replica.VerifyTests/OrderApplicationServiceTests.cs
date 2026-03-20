using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class OrderApplicationServiceTests
{
    [Fact]
    public void AddCreatedOrder_AssignsInternalIdAndAddsToHistory()
    {
        var service = CreateService();
        var history = new List<OrderData>();
        var order = new OrderData
        {
            Id = "1001",
            UserName = " test-user "
        };

        var internalId = service.AddCreatedOrder(history, order, user => user.Trim());

        Assert.Single(history);
        Assert.False(string.IsNullOrWhiteSpace(internalId));
        Assert.Equal(order.InternalId, internalId);
        Assert.Equal("test-user", history[0].UserName);
    }

    [Fact]
    public void BuildRunSkippedDetails_ComposesLocalAndServerReasons()
    {
        var service = CreateService();
        var runPlan = new OrderRunStateService.RunPlan(
            RunnableOrders: [],
            OrdersWithoutNumber: [new OrderData()],
            AlreadyRunningOrders: [new OrderData()]);

        var details = service.BuildRunSkippedDetails(runPlan, ["server rejected"]);

        Assert.Equal("без номера: 1, уже запущены: 1, сервер отклонил: 1", details);
    }

    [Fact]
    public void SyncStorageVersions_UpdatesOnlyMatchingLocalOrders()
    {
        var service = CreateService();
        var localOrders = new List<OrderData>
        {
            new() { InternalId = "a", StorageVersion = 1 },
            new() { InternalId = "b", StorageVersion = 2 }
        };
        var storageOrders = new List<OrderData>
        {
            new() { InternalId = "a", StorageVersion = 10 },
            new() { InternalId = "x", StorageVersion = 99 }
        };

        var updated = service.SyncStorageVersions(localOrders, storageOrders);

        Assert.Equal(1, updated);
        Assert.Equal(10, localOrders[0].StorageVersion);
        Assert.Equal(2, localOrders[1].StorageVersion);
    }

    [Fact]
    public void TryBuildRenamedPath_ReturnsSuccessForAvailableTarget()
    {
        var service = CreateService();
        var tempDir = Path.Combine(Path.GetTempPath(), "replica_order_app_service_tests");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, $"src_{Guid.NewGuid():N}.pdf");
        File.WriteAllText(sourcePath, "pdf");
        try
        {
            var result = service.TryBuildRenamedPath(sourcePath, "renamed_file");

            Assert.True(result.IsSuccess);
            Assert.Equal(RenamePathBuildStatus.Success, result.Status);
            Assert.EndsWith("renamed_file.pdf", result.RenamedPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(sourcePath))
                File.Delete(sourcePath);
        }
    }

    [Fact]
    public void ResolvePreferredOrderFolder_UsesOrderFolderName()
    {
        var service = CreateService();
        var order = new OrderData { FolderName = "A-100" };

        var resolved = service.ResolvePreferredOrderFolder(order, @"C:\orders", @"C:\temp");

        Assert.Equal(Path.Combine(@"C:\orders", "A-100"), resolved);
    }

    private static OrderApplicationService CreateService()
    {
        var now = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Local);
        var orderRunStateService = new OrderRunStateService();
        var lanRunCommandCoordinator = new LanRunCommandCoordinator(new LanOrderRunApiGateway());
        var orderRunWorkflowOrchestrationService = new OrderRunWorkflowOrchestrationService(
            orderRunStateService,
            lanRunCommandCoordinator);
        var orderRunCommandService = new OrderRunCommandService(
            orderRunWorkflowOrchestrationService,
            orderRunStateService,
            new OrderRunExecutionService());
        var orderItemMutationService = new OrderItemMutationService(() => now);
        var orderFilePathMutationService = new OrderFilePathMutationService(() => now);
        var orderFileRenameRemoveCommandService = new OrderFileRenameRemoveCommandService(
            orderFilePathMutationService,
            orderItemMutationService);

        return new OrderApplicationService(
            orderRunCommandService: orderRunCommandService,
            orderRunFeedbackService: new OrderRunFeedbackService(),
            ordersHistoryRepositoryCoordinator: new OrdersHistoryRepositoryCoordinator(),
            ordersHistoryMaintenanceService: new OrdersHistoryMaintenanceService(() => now),
            orderFolderPathResolutionService: new OrderFolderPathResolutionService(),
            orderEditorMutationService: new OrderEditorMutationService(() => now),
            orderItemMutationService: orderItemMutationService,
            orderFileStageCommandService: new OrderFileStageCommandService(),
            orderFilePathMutationService: orderFilePathMutationService,
            orderFileRenameRemoveCommandService: orderFileRenameRemoveCommandService,
            orderDeleteCommandService: new OrderDeleteCommandService(),
            orderItemDeleteCommandService: new OrderItemDeleteCommandService(itemMutationService: orderItemMutationService),
            orderStatusTransitionService: new OrderStatusTransitionService(),
            orderStorageVersionSyncService: new OrderStorageVersionSyncService());
    }
}
