using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Replica;

public interface IOrderApplicationService
{
    string OrdersRepositoryBackendName { get; }

    void ConfigureHistoryRepository(OrdersStorageMode storageBackend, string lanPostgreSqlConnectionString, string historyFilePath);
    bool TryLoadHistory(out List<OrderData> orders);
    bool TrySaveHistory(IReadOnlyCollection<OrderData> orders, out string error);
    bool TryAppendHistoryEvent(
        string orderInternalId,
        string itemId,
        string eventType,
        string eventSource,
        string payloadJson,
        out string error);
    OrdersHistoryPostLoadResult ApplyHistoryPostLoad(
        IList<OrderData> orders,
        Func<string?, string> normalizeUserName,
        int hashBackfillBudget = 32,
        Action<OrderData, string>? onTopologyIssue = null);
    OrdersHistoryPreSaveResult ApplyHistoryPreSave(IList<OrderData> orders);
    bool NormalizeHistoryTopology(IList<OrderData> orders, Action<OrderData, string>? onTopologyIssue);
    bool BackfillMissingHistoryFileHashes(IList<OrderData> orders, int maxFilesToHash);

    OrderBrowseFolderResolution ResolveBrowseFolderPath(OrderData order, string ordersRootPath, string tempRootPath);
    string ResolvePreferredOrderFolder(OrderData order, string ordersRootPath, string tempRootPath);

    string AddCreatedOrder(ICollection<OrderData> orderHistory, OrderData order, Func<string, string> normalizeUserName);
    void ApplySimpleEdit(OrderData order, string orderNumber, DateTime orderDate);
    void ApplyExtendedEdit(OrderData targetOrder, OrderData updatedOrder);
    Task<LanOrderWriteCommandResult> TryCreateOrderViaLanApiAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
        Func<string, string> normalizeUserName,
        CancellationToken cancellationToken = default);
    Task<LanOrderWriteCommandResult> TryUpdateOrderViaLanApiAsync(
        OrderData currentOrder,
        OrderData updatedOrder,
        string lanApiBaseUrl,
        string actor,
        Func<string, string> normalizeUserName,
        CancellationToken cancellationToken = default);
    Task<LanOrderWriteCommandResult> TryReorderOrderItemsViaLanApiAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default);
    Task<LanOrderWriteCommandResult> TryUpsertOrderItemViaLanApiAsync(
        OrderData order,
        OrderFileItem item,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default);

    Task<OrderRunStartPhaseResult> PrepareAndBeginRunAsync(
        IReadOnlyCollection<OrderData> selectedOrders,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        Func<OrderData, string> orderDisplayIdResolver,
        Func<IReadOnlyCollection<OrderData>, string, bool>? tryRefreshSnapshotFromStorage,
        CancellationToken cancellationToken = default);

    Task<OrderRunExecutionResult> ExecuteRunAsync(
        IReadOnlyCollection<OrderRunStateService.RunSession> runSessions,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        Func<OrderData, CancellationToken, Task> runOrderAsync,
        Action<OrderData> onCancelled,
        Action<OrderData, Exception> onFailed,
        Action<OrderData> onCompleted);

    Task<OrderRunStopPhaseResult> ExecuteStopAsync(
        OrderData order,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        Func<IReadOnlyCollection<OrderData>, string, bool>? tryRefreshSnapshotFromStorage,
        Action<OrderData>? applyLocalStopStatus = null,
        CancellationToken cancellationToken = default);

    string BuildRunServerSkippedPreview(IReadOnlyCollection<string>? serverSkipped, int previewLimit = 5);
    string BuildRunSkippedDetails(OrderRunStateService.RunPlan runPlan, IReadOnlyCollection<string>? serverSkipped);
    string BuildRunExecutionErrorsPreview(
        IReadOnlyCollection<OrderRunExecutionError>? errors,
        Func<OrderData, string> orderDisplayIdResolver,
        int previewLimit = 5);

    OrderDeleteCommandResult DeleteOrders(
        IList<OrderData> orderHistory,
        IReadOnlyCollection<OrderData> selectedOrders,
        bool removeFilesFromDisk,
        string ordersRootPath,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        ISet<string> expandedOrderIds,
        Action<OrderData, bool> onOrderRemoved);

    OrderItemDeleteCommandResult DeleteOrderItems(
        IReadOnlyCollection<OrderItemSelection> selectedOrderItems,
        bool removeFilesFromDisk,
        Action<OrderData, OrderFileItem, string> onItemRemoved);

    StatusTransitionResult ApplyStatusTransition(OrderData order, string status, string source, string reason);

    OrderItemAddPreparationResult PrepareAddItem(
        OrderData order,
        string sourcePath,
        string pitStopAction,
        string imposingAction);

    bool RollbackPreparedItem(OrderData order, OrderFileItem item);
    OrderItemTopologyMutationResult ApplyTopologyAfterItemMutation(OrderData order, bool wasMultiOrderBeforeMutation);
    bool ContainsOrderItem(OrderData order, string? itemId);

    bool TryPrepareOrderFileAdd(
        OrderData order,
        string sourceFile,
        int stage,
        Func<int, string, string> ensureUniqueStageFileName,
        out OrderFileStageAddPlan plan);

    bool TryPrepareItemFileAdd(
        OrderData order,
        OrderFileItem item,
        string sourceFile,
        int stage,
        Func<int, string, string> ensureUniqueStageFileName,
        Func<string, string> buildItemPrintFileName,
        out OrderFileStageAddPlan plan);

    FileSyncStatusUpdate ApplyOrderFileRemoved(OrderData order, int stage);
    OrderItemFileRemoveOutcome ApplyItemFileRemoved(OrderData order, OrderFileItem item, int stage, bool wasMultiOrderBeforeMutation);
    FileSyncStatusUpdate ApplyOrderFileRenamed(OrderData order, int stage, string renamedPath);
    FileSyncStatusUpdate ApplyItemFileRenamed(OrderData order, OrderFileItem item, int stage, string renamedPath);
    FileSyncStatusUpdate ApplyPrintTileFileRenamed(OrderData order, string oldPath, string renamedPath);
    RenamePathBuildResult TryBuildRenamedPath(string currentPath, string? requestedName);

    FileSyncStatusUpdate ApplyOrderFilePath(OrderData order, int stage, string path);
    FileSyncStatusUpdate ApplyItemFilePath(OrderData order, OrderFileItem item, int stage, string path);
    FileSyncStatusUpdate CalculateOrderStatusFromItems(OrderData order);

    int SyncStorageVersions(IReadOnlyCollection<OrderData> localOrders, IReadOnlyCollection<OrderData> storageOrders);
}

public sealed class OrderApplicationService : IOrderApplicationService
{
    private readonly OrderRunCommandService _orderRunCommandService;
    private readonly OrderRunFeedbackService _orderRunFeedbackService;
    private readonly OrdersHistoryRepositoryCoordinator _ordersHistoryRepositoryCoordinator;
    private readonly OrdersHistoryMaintenanceService _ordersHistoryMaintenanceService;
    private readonly OrderFolderPathResolutionService _orderFolderPathResolutionService;
    private readonly OrderEditorMutationService _orderEditorMutationService;
    private readonly OrderItemMutationService _orderItemMutationService;
    private readonly OrderFileStageCommandService _orderFileStageCommandService;
    private readonly OrderFilePathMutationService _orderFilePathMutationService;
    private readonly OrderFileRenameRemoveCommandService _orderFileRenameRemoveCommandService;
    private readonly OrderDeleteCommandService _orderDeleteCommandService;
    private readonly OrderItemDeleteCommandService _orderItemDeleteCommandService;
    private readonly OrderStatusTransitionService _orderStatusTransitionService;
    private readonly OrderStorageVersionSyncService _orderStorageVersionSyncService;
    private readonly LanOrderWriteCommandService _lanOrderWriteCommandService;

    public OrderApplicationService(
        OrderRunCommandService orderRunCommandService,
        OrderRunFeedbackService orderRunFeedbackService,
        OrdersHistoryRepositoryCoordinator ordersHistoryRepositoryCoordinator,
        OrdersHistoryMaintenanceService ordersHistoryMaintenanceService,
        OrderFolderPathResolutionService orderFolderPathResolutionService,
        OrderEditorMutationService orderEditorMutationService,
        OrderItemMutationService orderItemMutationService,
        OrderFileStageCommandService orderFileStageCommandService,
        OrderFilePathMutationService orderFilePathMutationService,
        OrderFileRenameRemoveCommandService orderFileRenameRemoveCommandService,
        OrderDeleteCommandService orderDeleteCommandService,
        OrderItemDeleteCommandService orderItemDeleteCommandService,
        OrderStatusTransitionService orderStatusTransitionService,
        OrderStorageVersionSyncService orderStorageVersionSyncService,
        LanOrderWriteCommandService lanOrderWriteCommandService)
    {
        _orderRunCommandService = orderRunCommandService ?? throw new ArgumentNullException(nameof(orderRunCommandService));
        _orderRunFeedbackService = orderRunFeedbackService ?? throw new ArgumentNullException(nameof(orderRunFeedbackService));
        _ordersHistoryRepositoryCoordinator = ordersHistoryRepositoryCoordinator ?? throw new ArgumentNullException(nameof(ordersHistoryRepositoryCoordinator));
        _ordersHistoryMaintenanceService = ordersHistoryMaintenanceService ?? throw new ArgumentNullException(nameof(ordersHistoryMaintenanceService));
        _orderFolderPathResolutionService = orderFolderPathResolutionService ?? throw new ArgumentNullException(nameof(orderFolderPathResolutionService));
        _orderEditorMutationService = orderEditorMutationService ?? throw new ArgumentNullException(nameof(orderEditorMutationService));
        _orderItemMutationService = orderItemMutationService ?? throw new ArgumentNullException(nameof(orderItemMutationService));
        _orderFileStageCommandService = orderFileStageCommandService ?? throw new ArgumentNullException(nameof(orderFileStageCommandService));
        _orderFilePathMutationService = orderFilePathMutationService ?? throw new ArgumentNullException(nameof(orderFilePathMutationService));
        _orderFileRenameRemoveCommandService = orderFileRenameRemoveCommandService ?? throw new ArgumentNullException(nameof(orderFileRenameRemoveCommandService));
        _orderDeleteCommandService = orderDeleteCommandService ?? throw new ArgumentNullException(nameof(orderDeleteCommandService));
        _orderItemDeleteCommandService = orderItemDeleteCommandService ?? throw new ArgumentNullException(nameof(orderItemDeleteCommandService));
        _orderStatusTransitionService = orderStatusTransitionService ?? throw new ArgumentNullException(nameof(orderStatusTransitionService));
        _orderStorageVersionSyncService = orderStorageVersionSyncService ?? throw new ArgumentNullException(nameof(orderStorageVersionSyncService));
        _lanOrderWriteCommandService = lanOrderWriteCommandService ?? throw new ArgumentNullException(nameof(lanOrderWriteCommandService));
    }

    public string OrdersRepositoryBackendName => _ordersHistoryRepositoryCoordinator.BackendName;

    public void ConfigureHistoryRepository(OrdersStorageMode storageBackend, string lanPostgreSqlConnectionString, string historyFilePath)
        => _ordersHistoryRepositoryCoordinator.Configure(storageBackend, lanPostgreSqlConnectionString, historyFilePath);

    public bool TryLoadHistory(out List<OrderData> orders)
        => _ordersHistoryRepositoryCoordinator.TryLoadAll(out orders);

    public bool TrySaveHistory(IReadOnlyCollection<OrderData> orders, out string error)
        => _ordersHistoryRepositoryCoordinator.TrySaveAll(orders, out error);

    public bool TryAppendHistoryEvent(
        string orderInternalId,
        string itemId,
        string eventType,
        string eventSource,
        string payloadJson,
        out string error)
        => _ordersHistoryRepositoryCoordinator.TryAppendEvent(
            orderInternalId,
            itemId,
            eventType,
            eventSource,
            payloadJson,
            out error);

    public OrdersHistoryPostLoadResult ApplyHistoryPostLoad(
        IList<OrderData> orders,
        Func<string?, string> normalizeUserName,
        int hashBackfillBudget = 32,
        Action<OrderData, string>? onTopologyIssue = null)
        => _ordersHistoryMaintenanceService.ApplyPostLoad(
            orders,
            normalizeUserName,
            hashBackfillBudget,
            onTopologyIssue);

    public OrdersHistoryPreSaveResult ApplyHistoryPreSave(IList<OrderData> orders)
        => _ordersHistoryMaintenanceService.ApplyPreSave(orders);

    public bool NormalizeHistoryTopology(IList<OrderData> orders, Action<OrderData, string>? onTopologyIssue)
        => _ordersHistoryMaintenanceService.NormalizeOrderTopologyInHistory(orders, onTopologyIssue);

    public bool BackfillMissingHistoryFileHashes(IList<OrderData> orders, int maxFilesToHash)
        => _ordersHistoryMaintenanceService.BackfillMissingFileHashesIncrementally(orders, maxFilesToHash);

    public OrderBrowseFolderResolution ResolveBrowseFolderPath(OrderData order, string ordersRootPath, string tempRootPath)
        => _orderFolderPathResolutionService.ResolveBrowseFolderPath(order, ordersRootPath, tempRootPath);

    public string ResolvePreferredOrderFolder(OrderData order, string ordersRootPath, string tempRootPath)
        => _orderFolderPathResolutionService.ResolvePreferredOrderFolder(order, ordersRootPath, tempRootPath);

    public string AddCreatedOrder(ICollection<OrderData> orderHistory, OrderData order, Func<string, string> normalizeUserName)
        => _orderEditorMutationService.AddCreatedOrder(orderHistory, order, normalizeUserName);

    public void ApplySimpleEdit(OrderData order, string orderNumber, DateTime orderDate)
        => _orderEditorMutationService.ApplySimpleEdit(order, orderNumber, orderDate);

    public void ApplyExtendedEdit(OrderData targetOrder, OrderData updatedOrder)
        => _orderEditorMutationService.ApplyExtendedEdit(targetOrder, updatedOrder);

    public Task<LanOrderWriteCommandResult> TryCreateOrderViaLanApiAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
        Func<string, string> normalizeUserName,
        CancellationToken cancellationToken = default)
        => _lanOrderWriteCommandService.TryCreateOrderAsync(
            order,
            lanApiBaseUrl,
            actor,
            normalizeUserName,
            cancellationToken);

    public Task<LanOrderWriteCommandResult> TryUpdateOrderViaLanApiAsync(
        OrderData currentOrder,
        OrderData updatedOrder,
        string lanApiBaseUrl,
        string actor,
        Func<string, string> normalizeUserName,
        CancellationToken cancellationToken = default)
        => _lanOrderWriteCommandService.TryUpdateOrderAsync(
            currentOrder,
            updatedOrder,
            lanApiBaseUrl,
            actor,
            normalizeUserName,
            cancellationToken);

    public Task<LanOrderWriteCommandResult> TryReorderOrderItemsViaLanApiAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
        => _lanOrderWriteCommandService.TryReorderItemsAsync(
            order,
            lanApiBaseUrl,
            actor,
            cancellationToken);

    public Task<LanOrderWriteCommandResult> TryUpsertOrderItemViaLanApiAsync(
        OrderData order,
        OrderFileItem item,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
        => _lanOrderWriteCommandService.TryUpsertItemAsync(
            order,
            item,
            lanApiBaseUrl,
            actor,
            cancellationToken);

    public Task<OrderRunStartPhaseResult> PrepareAndBeginRunAsync(
        IReadOnlyCollection<OrderData> selectedOrders,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        Func<OrderData, string> orderDisplayIdResolver,
        Func<IReadOnlyCollection<OrderData>, string, bool>? tryRefreshSnapshotFromStorage,
        CancellationToken cancellationToken = default)
        => _orderRunCommandService.PrepareAndBeginAsync(
            selectedOrders,
            runTokensByOrder,
            runProgressByOrderInternalId,
            useLanApi,
            lanApiBaseUrl,
            actor,
            orderDisplayIdResolver,
            tryRefreshSnapshotFromStorage,
            cancellationToken);

    public Task<OrderRunExecutionResult> ExecuteRunAsync(
        IReadOnlyCollection<OrderRunStateService.RunSession> runSessions,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        Func<OrderData, CancellationToken, Task> runOrderAsync,
        Action<OrderData> onCancelled,
        Action<OrderData, Exception> onFailed,
        Action<OrderData> onCompleted)
        => _orderRunCommandService.ExecuteAsync(
            runSessions,
            runTokensByOrder,
            runProgressByOrderInternalId,
            runOrderAsync,
            onCancelled,
            onFailed,
            onCompleted);

    public Task<OrderRunStopPhaseResult> ExecuteStopAsync(
        OrderData order,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        Func<IReadOnlyCollection<OrderData>, string, bool>? tryRefreshSnapshotFromStorage,
        Action<OrderData>? applyLocalStopStatus = null,
        CancellationToken cancellationToken = default)
        => _orderRunCommandService.ExecuteStopAsync(
            order,
            useLanApi,
            lanApiBaseUrl,
            actor,
            runTokensByOrder,
            runProgressByOrderInternalId,
            tryRefreshSnapshotFromStorage,
            applyLocalStopStatus,
            cancellationToken);

    public string BuildRunServerSkippedPreview(IReadOnlyCollection<string>? serverSkipped, int previewLimit = 5)
        => _orderRunFeedbackService.BuildServerSkippedPreview(serverSkipped, previewLimit);

    public string BuildRunSkippedDetails(OrderRunStateService.RunPlan runPlan, IReadOnlyCollection<string>? serverSkipped)
        => _orderRunFeedbackService.BuildSkippedDetails(runPlan, serverSkipped);

    public string BuildRunExecutionErrorsPreview(
        IReadOnlyCollection<OrderRunExecutionError>? errors,
        Func<OrderData, string> orderDisplayIdResolver,
        int previewLimit = 5)
        => _orderRunFeedbackService.BuildExecutionErrorsPreview(errors, orderDisplayIdResolver, previewLimit);

    public OrderDeleteCommandResult DeleteOrders(
        IList<OrderData> orderHistory,
        IReadOnlyCollection<OrderData> selectedOrders,
        bool removeFilesFromDisk,
        string ordersRootPath,
        IDictionary<string, CancellationTokenSource> runTokensByOrder,
        IDictionary<string, int> runProgressByOrderInternalId,
        ISet<string> expandedOrderIds,
        Action<OrderData, bool> onOrderRemoved)
        => _orderDeleteCommandService.Execute(
            orderHistory,
            selectedOrders,
            removeFilesFromDisk,
            ordersRootPath,
            runTokensByOrder,
            runProgressByOrderInternalId,
            expandedOrderIds,
            onOrderRemoved);

    public OrderItemDeleteCommandResult DeleteOrderItems(
        IReadOnlyCollection<OrderItemSelection> selectedOrderItems,
        bool removeFilesFromDisk,
        Action<OrderData, OrderFileItem, string> onItemRemoved)
        => _orderItemDeleteCommandService.Execute(selectedOrderItems, removeFilesFromDisk, onItemRemoved);

    public StatusTransitionResult ApplyStatusTransition(OrderData order, string status, string source, string reason)
        => _orderStatusTransitionService.Apply(order, status, source, reason);

    public OrderItemAddPreparationResult PrepareAddItem(
        OrderData order,
        string sourcePath,
        string pitStopAction,
        string imposingAction)
        => _orderItemMutationService.PrepareAddItem(order, sourcePath, pitStopAction, imposingAction);

    public bool RollbackPreparedItem(OrderData order, OrderFileItem item)
        => _orderItemMutationService.RollbackPreparedItem(order, item);

    public OrderItemTopologyMutationResult ApplyTopologyAfterItemMutation(OrderData order, bool wasMultiOrderBeforeMutation)
        => _orderItemMutationService.ApplyTopologyAfterItemMutation(order, wasMultiOrderBeforeMutation);

    public bool ContainsOrderItem(OrderData order, string? itemId)
        => _orderItemMutationService.ContainsOrderItem(order, itemId);

    public bool TryPrepareOrderFileAdd(
        OrderData order,
        string sourceFile,
        int stage,
        Func<int, string, string> ensureUniqueStageFileName,
        out OrderFileStageAddPlan plan)
        => _orderFileStageCommandService.TryPrepareOrderAdd(order, sourceFile, stage, ensureUniqueStageFileName, out plan);

    public bool TryPrepareItemFileAdd(
        OrderData order,
        OrderFileItem item,
        string sourceFile,
        int stage,
        Func<int, string, string> ensureUniqueStageFileName,
        Func<string, string> buildItemPrintFileName,
        out OrderFileStageAddPlan plan)
        => _orderFileStageCommandService.TryPrepareItemAdd(
            order,
            item,
            sourceFile,
            stage,
            ensureUniqueStageFileName,
            buildItemPrintFileName,
            out plan);

    public FileSyncStatusUpdate ApplyOrderFileRemoved(OrderData order, int stage)
        => _orderFileRenameRemoveCommandService.ApplyOrderFileRemoved(order, stage);

    public OrderItemFileRemoveOutcome ApplyItemFileRemoved(OrderData order, OrderFileItem item, int stage, bool wasMultiOrderBeforeMutation)
        => _orderFileRenameRemoveCommandService.ApplyItemFileRemoved(order, item, stage, wasMultiOrderBeforeMutation);

    public FileSyncStatusUpdate ApplyOrderFileRenamed(OrderData order, int stage, string renamedPath)
        => _orderFileRenameRemoveCommandService.ApplyOrderFileRenamed(order, stage, renamedPath);

    public FileSyncStatusUpdate ApplyItemFileRenamed(OrderData order, OrderFileItem item, int stage, string renamedPath)
        => _orderFileRenameRemoveCommandService.ApplyItemFileRenamed(order, item, stage, renamedPath);

    public FileSyncStatusUpdate ApplyPrintTileFileRenamed(OrderData order, string oldPath, string renamedPath)
        => _orderFileRenameRemoveCommandService.ApplyPrintTileFileRenamed(order, oldPath, renamedPath);

    public RenamePathBuildResult TryBuildRenamedPath(string currentPath, string? requestedName)
        => _orderFileRenameRemoveCommandService.TryBuildRenamedPath(currentPath, requestedName);

    public FileSyncStatusUpdate ApplyOrderFilePath(OrderData order, int stage, string path)
        => _orderFilePathMutationService.ApplyOrderFilePath(order, stage, path);

    public FileSyncStatusUpdate ApplyItemFilePath(OrderData order, OrderFileItem item, int stage, string path)
        => _orderFilePathMutationService.ApplyItemFilePath(order, item, stage, path);

    public FileSyncStatusUpdate CalculateOrderStatusFromItems(OrderData order)
        => _orderFilePathMutationService.CalculateOrderStatusFromItems(order);

    public int SyncStorageVersions(IReadOnlyCollection<OrderData> localOrders, IReadOnlyCollection<OrderData> storageOrders)
        => _orderStorageVersionSyncService.SyncLocalVersions(localOrders, storageOrders);
}
