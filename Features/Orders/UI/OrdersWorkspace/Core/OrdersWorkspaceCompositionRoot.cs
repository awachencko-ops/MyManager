using System;

namespace Replica;

internal sealed class OrdersWorkspaceRuntimeServices
{
    public OrdersWorkspaceRuntimeServices(
        OrdersHistoryRepositoryCoordinator ordersHistoryCoordinator,
        OrdersHistoryMaintenanceService ordersHistoryMaintenanceService,
        OrderFolderPathResolutionService orderFolderPathResolutionService,
        IOrderApplicationService orderApplicationService)
    {
        OrdersHistoryCoordinator = ordersHistoryCoordinator ?? throw new ArgumentNullException(nameof(ordersHistoryCoordinator));
        OrdersHistoryMaintenanceService = ordersHistoryMaintenanceService ?? throw new ArgumentNullException(nameof(ordersHistoryMaintenanceService));
        OrderFolderPathResolutionService = orderFolderPathResolutionService ?? throw new ArgumentNullException(nameof(orderFolderPathResolutionService));
        OrderApplicationService = orderApplicationService ?? throw new ArgumentNullException(nameof(orderApplicationService));
    }

    public OrdersHistoryRepositoryCoordinator OrdersHistoryCoordinator { get; }
    public OrdersHistoryMaintenanceService OrdersHistoryMaintenanceService { get; }
    public OrderFolderPathResolutionService OrderFolderPathResolutionService { get; }
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
        var orderApplicationService = new OrderApplicationService(
            orderRunCommandService: orderRunCommandService,
            orderRunFeedbackService: new OrderRunFeedbackService(),
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
            ordersHistoryCoordinator: new OrdersHistoryRepositoryCoordinator(),
            ordersHistoryMaintenanceService: new OrdersHistoryMaintenanceService(),
            orderFolderPathResolutionService: new OrderFolderPathResolutionService(),
            orderApplicationService: orderApplicationService);
    }
}
