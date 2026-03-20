using System;

namespace Replica;

internal sealed class OrdersWorkspaceRuntimeServices
{
    public OrdersWorkspaceRuntimeServices(
        LanRunCommandCoordinator lanRunCommandCoordinator,
        OrdersHistoryRepositoryCoordinator ordersHistoryCoordinator,
        OrdersHistoryMaintenanceService ordersHistoryMaintenanceService,
        OrderFolderPathResolutionService orderFolderPathResolutionService,
        OrderStorageVersionSyncService orderStorageVersionSyncService,
        OrderRunStateService orderRunStateService,
        OrderRunFeedbackService orderRunFeedbackService,
        OrderRunCommandService orderRunCommandService,
        OrderEditorMutationService orderEditorMutationService,
        OrderItemMutationService orderItemMutationService,
        OrderFileStageCommandService orderFileStageCommandService,
        OrderFilePathMutationService orderFilePathMutationService,
        OrderFileRenameRemoveCommandService orderFileRenameRemoveCommandService,
        OrderDeleteCommandService orderDeleteCommandService,
        OrderItemDeleteCommandService orderItemDeleteCommandService,
        OrderStatusTransitionService orderStatusTransitionService)
    {
        LanRunCommandCoordinator = lanRunCommandCoordinator ?? throw new ArgumentNullException(nameof(lanRunCommandCoordinator));
        OrdersHistoryCoordinator = ordersHistoryCoordinator ?? throw new ArgumentNullException(nameof(ordersHistoryCoordinator));
        OrdersHistoryMaintenanceService = ordersHistoryMaintenanceService ?? throw new ArgumentNullException(nameof(ordersHistoryMaintenanceService));
        OrderFolderPathResolutionService = orderFolderPathResolutionService ?? throw new ArgumentNullException(nameof(orderFolderPathResolutionService));
        OrderStorageVersionSyncService = orderStorageVersionSyncService ?? throw new ArgumentNullException(nameof(orderStorageVersionSyncService));
        OrderRunStateService = orderRunStateService ?? throw new ArgumentNullException(nameof(orderRunStateService));
        OrderRunFeedbackService = orderRunFeedbackService ?? throw new ArgumentNullException(nameof(orderRunFeedbackService));
        OrderRunCommandService = orderRunCommandService ?? throw new ArgumentNullException(nameof(orderRunCommandService));
        OrderEditorMutationService = orderEditorMutationService ?? throw new ArgumentNullException(nameof(orderEditorMutationService));
        OrderItemMutationService = orderItemMutationService ?? throw new ArgumentNullException(nameof(orderItemMutationService));
        OrderFileStageCommandService = orderFileStageCommandService ?? throw new ArgumentNullException(nameof(orderFileStageCommandService));
        OrderFilePathMutationService = orderFilePathMutationService ?? throw new ArgumentNullException(nameof(orderFilePathMutationService));
        OrderFileRenameRemoveCommandService = orderFileRenameRemoveCommandService ?? throw new ArgumentNullException(nameof(orderFileRenameRemoveCommandService));
        OrderDeleteCommandService = orderDeleteCommandService ?? throw new ArgumentNullException(nameof(orderDeleteCommandService));
        OrderItemDeleteCommandService = orderItemDeleteCommandService ?? throw new ArgumentNullException(nameof(orderItemDeleteCommandService));
        OrderStatusTransitionService = orderStatusTransitionService ?? throw new ArgumentNullException(nameof(orderStatusTransitionService));
    }

    public LanRunCommandCoordinator LanRunCommandCoordinator { get; }
    public OrdersHistoryRepositoryCoordinator OrdersHistoryCoordinator { get; }
    public OrdersHistoryMaintenanceService OrdersHistoryMaintenanceService { get; }
    public OrderFolderPathResolutionService OrderFolderPathResolutionService { get; }
    public OrderStorageVersionSyncService OrderStorageVersionSyncService { get; }
    public OrderRunStateService OrderRunStateService { get; }
    public OrderRunFeedbackService OrderRunFeedbackService { get; }
    public OrderRunCommandService OrderRunCommandService { get; }
    public OrderEditorMutationService OrderEditorMutationService { get; }
    public OrderItemMutationService OrderItemMutationService { get; }
    public OrderFileStageCommandService OrderFileStageCommandService { get; }
    public OrderFilePathMutationService OrderFilePathMutationService { get; }
    public OrderFileRenameRemoveCommandService OrderFileRenameRemoveCommandService { get; }
    public OrderDeleteCommandService OrderDeleteCommandService { get; }
    public OrderItemDeleteCommandService OrderItemDeleteCommandService { get; }
    public OrderStatusTransitionService OrderStatusTransitionService { get; }
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

        return new OrdersWorkspaceRuntimeServices(
            lanRunCommandCoordinator: lanRunCommandCoordinator,
            ordersHistoryCoordinator: new OrdersHistoryRepositoryCoordinator(),
            ordersHistoryMaintenanceService: new OrdersHistoryMaintenanceService(),
            orderFolderPathResolutionService: new OrderFolderPathResolutionService(),
            orderStorageVersionSyncService: new OrderStorageVersionSyncService(),
            orderRunStateService: orderRunStateService,
            orderRunFeedbackService: new OrderRunFeedbackService(),
            orderRunCommandService: orderRunCommandService,
            orderEditorMutationService: new OrderEditorMutationService(),
            orderItemMutationService: new OrderItemMutationService(),
            orderFileStageCommandService: new OrderFileStageCommandService(),
            orderFilePathMutationService: new OrderFilePathMutationService(),
            orderFileRenameRemoveCommandService: new OrderFileRenameRemoveCommandService(),
            orderDeleteCommandService: new OrderDeleteCommandService(),
            orderItemDeleteCommandService: new OrderItemDeleteCommandService(),
            orderStatusTransitionService: new OrderStatusTransitionService());
    }
}
