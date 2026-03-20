using System;

namespace Replica;

internal sealed class OrdersWorkspaceRuntimeServices
{
    public OrdersWorkspaceRuntimeServices(
        IOrderApplicationService orderApplicationService)
    {
        OrderApplicationService = orderApplicationService ?? throw new ArgumentNullException(nameof(orderApplicationService));
    }

    public IOrderApplicationService OrderApplicationService { get; }
}

internal static class OrdersWorkspaceCompositionRoot
{
    public static OrdersWorkspaceRuntimeServices CreateRuntimeServices()
    {
        var lanRunCommandCoordinator = new LanRunCommandCoordinator(new LanOrderRunApiGateway());
        var orderRunStateService = new OrderRunStateService();
        var orderRunWorkflowOrchestrationService = new OrderRunWorkflowOrchestrationService(
            orderRunStateService,
            lanRunCommandCoordinator);
        var orderRunCommandService = new OrderRunCommandService(
            orderRunWorkflowOrchestrationService,
            orderRunStateService,
            new OrderRunExecutionService());
        var orderItemMutationService = new OrderItemMutationService();
        var orderFilePathMutationService = new OrderFilePathMutationService();
        var orderFileRenameRemoveCommandService = new OrderFileRenameRemoveCommandService(
            orderFilePathMutationService,
            orderItemMutationService);
        var ordersHistoryRepositoryCoordinator = new OrdersHistoryRepositoryCoordinator();
        var ordersHistoryMaintenanceService = new OrdersHistoryMaintenanceService();
        var orderFolderPathResolutionService = new OrderFolderPathResolutionService();
        var orderApplicationService = new OrderApplicationService(
            orderRunCommandService: orderRunCommandService,
            orderRunFeedbackService: new OrderRunFeedbackService(),
            ordersHistoryRepositoryCoordinator: ordersHistoryRepositoryCoordinator,
            ordersHistoryMaintenanceService: ordersHistoryMaintenanceService,
            orderFolderPathResolutionService: orderFolderPathResolutionService,
            orderEditorMutationService: new OrderEditorMutationService(),
            orderItemMutationService: orderItemMutationService,
            orderFileStageCommandService: new OrderFileStageCommandService(),
            orderFilePathMutationService: orderFilePathMutationService,
            orderFileRenameRemoveCommandService: orderFileRenameRemoveCommandService,
            orderDeleteCommandService: new OrderDeleteCommandService(),
            orderItemDeleteCommandService: new OrderItemDeleteCommandService(itemMutationService: orderItemMutationService),
            orderStatusTransitionService: new OrderStatusTransitionService(),
            orderStorageVersionSyncService: new OrderStorageVersionSyncService());

        return new OrdersWorkspaceRuntimeServices(
            orderApplicationService: orderApplicationService);
    }
}
