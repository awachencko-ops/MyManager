using System;
using System.Collections.Generic;
using System.Linq;
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
    Task<LanOrderWriteCommandResult> TryDeleteOrderViaLanApiAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
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
    Task<LanOrderWriteCommandResult> TryDeleteOrderItemViaLanApiAsync(
        OrderData order,
        OrderFileItem item,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default);
    Task<LanOrderStatusPersistOutcome> TryPersistOrderStatusViaLanApiAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
        Func<string, string> normalizeUserName,
        string source,
        string reason,
        Func<OrderData, string> orderDisplayIdResolver,
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
    OrderRunStartUiMutation BuildRunStartUiMutation(bool isBatchRun);
    OrderRunStatusUiMutation BuildRunCancelledUiMutation();
    OrderRunStatusUiMutation BuildRunFailedUiMutation(string? errorMessage);
    OrderRunStartUiFeedback BuildRunSelectionRequiredUiFeedback();
    OrderRunStartUiFeedback BuildRunStartUiFeedback(OrderRunStartPhaseResult startPhase);
    OrderRunStartProgressUiFeedback BuildRunStartProgressUiFeedback(
        int runnableOrdersCount,
        OrderRunStateService.RunPlan runPlan,
        IReadOnlyCollection<string>? serverSkipped);
    OrderRunUiEffectsPlan BuildRunPostStatusApplyUiEffectsPlan();
    OrderRunUiEffectsPlan BuildRunPerOrderCompletionUiEffectsPlan();
    OrderRunUiEffectsPlan BuildRunPostExecutionUiEffectsPlan();
    OrderRunCompletionUiFeedback BuildRunCompletionUiFeedback(
        IReadOnlyCollection<OrderRunExecutionError>? errors,
        int runnableOrdersCount,
        Func<OrderData, string> orderDisplayIdResolver);
    OrderRunLifecycleUiFeedback BuildRunCommandStartLifecycleUiFeedback();
    OrderRunLifecycleUiFeedback BuildRunStopCommandStartLifecycleUiFeedback();
    OrderRunLifecycleUiFeedback BuildRunSnapshotRefreshWarningUiFeedback(string phase, string? orderDisplayId = null);
    OrderRunLifecycleUiFeedback BuildRunCommandFinishLifecycleUiFeedback(int startedCount, int errorsCount);
    OrderRunUiEffectsPlan BuildStopPostPhaseUiEffectsPlan(OrderRunStopPhaseResult stopPhase, OrderRunStopUiFeedback stopUiFeedback);
    OrderRunStopLocalUiMutation BuildRunStopLocalUiMutation();
    OrderRunStopUiFeedback BuildRunStopSelectionRequiredUiFeedback();
    OrderRunStopUiFeedback BuildRunStopUiFeedback(OrderRunStopPhaseResult stopPhase, string orderDisplayId);
    OrderGridMutationUiPlan BuildPostGridMutationUiPlan();

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

    Task<OrderStatusApplyOutcome> ApplyStatusTransitionWithPersistenceAsync(
        OrderData order,
        string status,
        string source,
        string reason,
        bool persistHistory,
        bool rebuildGrid,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        Func<string, string> normalizeUserName,
        Func<OrderData, string> orderDisplayIdResolver,
        CancellationToken cancellationToken = default);

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
    void UpsertOrderInHistory(IList<OrderData> orderHistory, OrderData updatedOrder);
    void ApplyLanStatusSnapshot(OrderData localOrder, OrderData serverOrder);
    void ApplyLanOrderItemVersionsSnapshot(OrderData localOrder, OrderData serverOrder);
    void ApplyLanOrderItemDeleteSnapshot(OrderData localOrder, OrderData serverOrder);
    LanOrderWriteApplyOutcome ApplyLanOrderWriteResult(
        IList<OrderData> orderHistory,
        OrderData? targetOrder,
        LanOrderWriteCommandResult writeResult,
        string operationCaption,
        string successSnapshotReason);
    Task<LanOrderItemsReorderSyncOutcome> SyncLanItemReorderForOrdersAsync(
        IEnumerable<OrderData> orders,
        IList<OrderData> orderHistory,
        string lanApiBaseUrl,
        string actor,
        string reason,
        Func<OrderData, string> orderDisplayIdResolver,
        CancellationToken cancellationToken = default);
    Task<LanOrderItemSyncOutcome> SyncLanOrderItemUpsertAsync(
        OrderData order,
        OrderFileItem item,
        string lanApiBaseUrl,
        string actor,
        string reason,
        Func<OrderData, string> orderDisplayIdResolver,
        CancellationToken cancellationToken = default);
    Task<LanOrderItemSyncOutcome> SyncLanOrderItemDeleteAsync(
        OrderData order,
        OrderFileItem item,
        string lanApiBaseUrl,
        string actor,
        string reason,
        Func<OrderData, string> orderDisplayIdResolver,
        CancellationToken cancellationToken = default);
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

    public Task<LanOrderWriteCommandResult> TryDeleteOrderViaLanApiAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
        => _lanOrderWriteCommandService.TryDeleteOrderAsync(
            order,
            lanApiBaseUrl,
            actor,
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

    public Task<LanOrderWriteCommandResult> TryDeleteOrderItemViaLanApiAsync(
        OrderData order,
        OrderFileItem item,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
        => _lanOrderWriteCommandService.TryDeleteItemAsync(
            order,
            item,
            lanApiBaseUrl,
            actor,
            cancellationToken);

    public async Task<LanOrderStatusPersistOutcome> TryPersistOrderStatusViaLanApiAsync(
        OrderData order,
        string lanApiBaseUrl,
        string actor,
        Func<string, string> normalizeUserName,
        string source,
        string reason,
        Func<OrderData, string> orderDisplayIdResolver,
        CancellationToken cancellationToken = default)
    {
        if (order == null)
            return LanOrderStatusPersistOutcome.NotPersisted(logs: Array.Empty<OrderRunFeedbackLogEntry>());

        var statusUpdateModel = new OrderData
        {
            Id = order.Id,
            OrderDate = order.OrderDate,
            UserName = order.UserName,
            Status = order.Status,
            Keyword = order.Keyword,
            FolderName = order.FolderName,
            PitStopAction = order.PitStopAction,
            ImposingAction = order.ImposingAction
        };

        var writeResult = await TryUpdateOrderViaLanApiAsync(
            order,
            statusUpdateModel,
            lanApiBaseUrl,
            actor,
            normalizeUserName,
            cancellationToken);

        if (writeResult.IsSuccess && writeResult.Order != null)
        {
            ApplyLanStatusSnapshot(order, writeResult.Order);
            return LanOrderStatusPersistOutcome.Persisted("lan-api-status-update");
        }

        if (writeResult.CurrentVersion > 0)
            order.StorageVersion = writeResult.CurrentVersion;

        var errorText = string.IsNullOrWhiteSpace(writeResult.Error)
            ? "LAN status update failed"
            : writeResult.Error;
        return LanOrderStatusPersistOutcome.NotPersisted(
            logs:
            [
                new OrderRunFeedbackLogEntry(
                    $"LAN-API | status-update-failed | order={ResolveOrderDisplayId(order, orderDisplayIdResolver)} | source={source} | reason={reason} | conflict={(writeResult.IsConflict ? "1" : "0")} | unavailable={(writeResult.IsUnavailable ? "1" : "0")} | {errorText}",
                    isWarning: true)
            ]);
    }

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

    public OrderRunStartUiMutation BuildRunStartUiMutation(bool isBatchRun)
        => _orderRunFeedbackService.BuildRunStartUiMutation(isBatchRun);

    public OrderRunStatusUiMutation BuildRunCancelledUiMutation()
        => _orderRunFeedbackService.BuildRunCancelledUiMutation();

    public OrderRunStatusUiMutation BuildRunFailedUiMutation(string? errorMessage)
        => _orderRunFeedbackService.BuildRunFailedUiMutation(errorMessage);

    public OrderRunStartUiFeedback BuildRunSelectionRequiredUiFeedback()
        => _orderRunFeedbackService.BuildRunSelectionRequiredUiFeedback();

    public OrderRunStartUiFeedback BuildRunStartUiFeedback(OrderRunStartPhaseResult startPhase)
        => _orderRunFeedbackService.BuildStartUiFeedback(startPhase);

    public OrderRunStartProgressUiFeedback BuildRunStartProgressUiFeedback(
        int runnableOrdersCount,
        OrderRunStateService.RunPlan runPlan,
        IReadOnlyCollection<string>? serverSkipped)
        => _orderRunFeedbackService.BuildStartProgressUiFeedback(runnableOrdersCount, runPlan, serverSkipped);

    public OrderRunUiEffectsPlan BuildRunPostStatusApplyUiEffectsPlan()
        => _orderRunFeedbackService.BuildRunPostStatusApplyUiEffectsPlan();

    public OrderRunUiEffectsPlan BuildRunPerOrderCompletionUiEffectsPlan()
        => _orderRunFeedbackService.BuildRunPerOrderCompletionUiEffectsPlan();

    public OrderRunUiEffectsPlan BuildRunPostExecutionUiEffectsPlan()
        => _orderRunFeedbackService.BuildRunPostExecutionUiEffectsPlan();

    public OrderRunCompletionUiFeedback BuildRunCompletionUiFeedback(
        IReadOnlyCollection<OrderRunExecutionError>? errors,
        int runnableOrdersCount,
        Func<OrderData, string> orderDisplayIdResolver)
        => _orderRunFeedbackService.BuildCompletionUiFeedback(errors, runnableOrdersCount, orderDisplayIdResolver);

    public OrderRunLifecycleUiFeedback BuildRunCommandStartLifecycleUiFeedback()
        => _orderRunFeedbackService.BuildRunCommandStartLifecycleUiFeedback();

    public OrderRunLifecycleUiFeedback BuildRunStopCommandStartLifecycleUiFeedback()
        => _orderRunFeedbackService.BuildRunStopCommandStartLifecycleUiFeedback();

    public OrderRunLifecycleUiFeedback BuildRunSnapshotRefreshWarningUiFeedback(string phase, string? orderDisplayId = null)
        => _orderRunFeedbackService.BuildRunSnapshotRefreshWarningUiFeedback(phase, orderDisplayId);

    public OrderRunLifecycleUiFeedback BuildRunCommandFinishLifecycleUiFeedback(int startedCount, int errorsCount)
        => _orderRunFeedbackService.BuildRunCommandFinishLifecycleUiFeedback(startedCount, errorsCount);

    public OrderRunUiEffectsPlan BuildStopPostPhaseUiEffectsPlan(
        OrderRunStopPhaseResult stopPhase,
        OrderRunStopUiFeedback stopUiFeedback)
        => _orderRunFeedbackService.BuildStopPostPhaseUiEffectsPlan(stopPhase, stopUiFeedback);

    public OrderRunStopLocalUiMutation BuildRunStopLocalUiMutation()
        => _orderRunFeedbackService.BuildStopLocalUiMutation();

    public OrderRunStopUiFeedback BuildRunStopSelectionRequiredUiFeedback()
        => _orderRunFeedbackService.BuildStopSelectionRequiredUiFeedback();

    public OrderRunStopUiFeedback BuildRunStopUiFeedback(OrderRunStopPhaseResult stopPhase, string orderDisplayId)
        => _orderRunFeedbackService.BuildStopUiFeedback(stopPhase, orderDisplayId);

    public OrderGridMutationUiPlan BuildPostGridMutationUiPlan()
        => OrderGridMutationUiPlan.Changed();

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

    public async Task<OrderStatusApplyOutcome> ApplyStatusTransitionWithPersistenceAsync(
        OrderData order,
        string status,
        string source,
        string reason,
        bool persistHistory,
        bool rebuildGrid,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        Func<string, string> normalizeUserName,
        Func<OrderData, string> orderDisplayIdResolver,
        CancellationToken cancellationToken = default)
    {
        var transition = ApplyStatusTransition(order, status, source, reason);
        if (!transition.Changed)
            return OrderStatusApplyOutcome.NotChanged();

        var shouldSaveLocalHistory = persistHistory && !useLanApi;
        var shouldRefreshSnapshot = false;
        var snapshotRefreshReason = string.Empty;
        var logs = new List<OrderRunFeedbackLogEntry>();

        if (persistHistory && useLanApi)
        {
            var persistOutcome = await TryPersistOrderStatusViaLanApiAsync(
                order,
                lanApiBaseUrl,
                actor,
                normalizeUserName,
                source: transition.Source,
                reason: transition.Reason,
                orderDisplayIdResolver,
                cancellationToken);

            if (persistOutcome.Logs.Count > 0)
                logs.AddRange(persistOutcome.Logs);

            if (persistOutcome.IsPersisted)
            {
                shouldRefreshSnapshot = persistOutcome.ShouldRefreshSnapshot;
                snapshotRefreshReason = persistOutcome.SnapshotRefreshReason;
            }
            else
            {
                shouldSaveLocalHistory = true;
                logs.Add(new OrderRunFeedbackLogEntry(
                    $"LAN-API | status-update-fallback-local-save | order={ResolveOrderDisplayId(order, orderDisplayIdResolver)} | status={transition.NewStatus}",
                    isWarning: true));
            }
        }

        return OrderStatusApplyOutcome.FromChanged(
            transition,
            shouldSaveLocalHistory,
            shouldRefreshSnapshot,
            snapshotRefreshReason,
            uiRefreshMode: ResolveStatusUiRefreshMode(transition.Source, rebuildGrid),
            logs);
    }

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

    public void UpsertOrderInHistory(IList<OrderData> orderHistory, OrderData updatedOrder)
    {
        if (orderHistory == null || updatedOrder == null)
            return;

        var index = -1;
        for (var i = 0; i < orderHistory.Count; i++)
        {
            var order = orderHistory[i];
            if (order == null)
                continue;

            if (!string.Equals(order.InternalId, updatedOrder.InternalId, StringComparison.Ordinal))
                continue;

            index = i;
            break;
        }

        if (index >= 0)
        {
            orderHistory[index] = updatedOrder;
            return;
        }

        orderHistory.Add(updatedOrder);
    }

    public void ApplyLanStatusSnapshot(OrderData localOrder, OrderData serverOrder)
    {
        if (localOrder == null || serverOrder == null)
            return;

        if (serverOrder.StorageVersion > 0)
            localOrder.StorageVersion = serverOrder.StorageVersion;
        if (!string.IsNullOrWhiteSpace(serverOrder.Status))
            localOrder.Status = serverOrder.Status.Trim();
        if (!string.IsNullOrWhiteSpace(serverOrder.LastStatusSource))
            localOrder.LastStatusSource = serverOrder.LastStatusSource.Trim();
        if (!string.IsNullOrWhiteSpace(serverOrder.LastStatusReason))
            localOrder.LastStatusReason = serverOrder.LastStatusReason.Trim();
        if (serverOrder.LastStatusAt != default)
            localOrder.LastStatusAt = serverOrder.LastStatusAt;
    }

    public void ApplyLanOrderItemVersionsSnapshot(OrderData localOrder, OrderData serverOrder)
    {
        if (localOrder == null || serverOrder == null)
            return;

        if (serverOrder.StorageVersion > 0)
            localOrder.StorageVersion = serverOrder.StorageVersion;

        var serverItemsById = (serverOrder.Items ?? [])
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ItemId))
            .ToDictionary(item => item.ItemId, item => item, StringComparer.Ordinal);

        foreach (var localItem in (localOrder.Items ?? []).Where(item => item != null))
        {
            if (!serverItemsById.TryGetValue(localItem.ItemId, out var serverItem))
                continue;

            if (serverItem.StorageVersion > 0)
                localItem.StorageVersion = serverItem.StorageVersion;
        }
    }

    public void ApplyLanOrderItemDeleteSnapshot(OrderData localOrder, OrderData serverOrder)
    {
        if (localOrder == null || serverOrder == null)
            return;

        if (serverOrder.StorageVersion > 0)
            localOrder.StorageVersion = serverOrder.StorageVersion;

        var serverIds = (serverOrder.Items ?? [])
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ItemId))
            .Select(item => item.ItemId)
            .ToHashSet(StringComparer.Ordinal);

        if (localOrder.Items != null)
            localOrder.Items.RemoveAll(localItem => localItem == null || !serverIds.Contains(localItem.ItemId));

        ApplyLanOrderItemVersionsSnapshot(localOrder, serverOrder);
    }

    public LanOrderWriteApplyOutcome ApplyLanOrderWriteResult(
        IList<OrderData> orderHistory,
        OrderData? targetOrder,
        LanOrderWriteCommandResult writeResult,
        string operationCaption,
        string successSnapshotReason)
    {
        if (writeResult != null && writeResult.IsSuccess && writeResult.Order != null)
        {
            UpsertOrderInHistory(orderHistory, writeResult.Order);
            return LanOrderWriteApplyOutcome.Success(writeResult.Order, successSnapshotReason);
        }

        if (targetOrder != null && writeResult != null && writeResult.CurrentVersion > 0)
            targetOrder.StorageVersion = writeResult.CurrentVersion;

        var caption = string.IsNullOrWhiteSpace(operationCaption)
            ? "LAN API"
            : operationCaption.Trim();
        var defaultError = "LAN API недоступен";
        var errorText = writeResult == null || string.IsNullOrWhiteSpace(writeResult.Error)
            ? defaultError
            : writeResult.Error;

        if (writeResult != null && writeResult.IsConflict)
        {
            return LanOrderWriteApplyOutcome.Failed(
                bottomStatus: $"{caption}: конфликт версии",
                dialog: new OrderRunFeedbackDialog(
                    caption,
                    $"Сервер отклонил запись из-за конфликта версии.{Environment.NewLine}{errorText}",
                    OrderRunFeedbackSeverity.Information),
                shouldRefreshSnapshot: true,
                snapshotRefreshReason: "lan-api-write-conflict");
        }

        return LanOrderWriteApplyOutcome.Failed(
            bottomStatus: $"{caption} не выполнено",
            dialog: new OrderRunFeedbackDialog(
                caption,
                $"{caption} не выполнено.{Environment.NewLine}{errorText}",
                OrderRunFeedbackSeverity.Warning),
            shouldRefreshSnapshot: false,
            snapshotRefreshReason: string.Empty);
    }

    public async Task<LanOrderItemsReorderSyncOutcome> SyncLanItemReorderForOrdersAsync(
        IEnumerable<OrderData> orders,
        IList<OrderData> orderHistory,
        string lanApiBaseUrl,
        string actor,
        string reason,
        Func<OrderData, string> orderDisplayIdResolver,
        CancellationToken cancellationToken = default)
    {
        if (orders == null)
            return LanOrderItemsReorderSyncOutcome.Noop();

        var normalizedOrders = orders
            .Where(order => order != null && !string.IsNullOrWhiteSpace(order.InternalId))
            .GroupBy(order => order.InternalId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Where(order => (order.Items?.Count ?? 0) > 1)
            .ToList();
        if (normalizedOrders.Count == 0)
            return LanOrderItemsReorderSyncOutcome.Noop();

        var logs = new List<OrderRunFeedbackLogEntry>();
        foreach (var order in normalizedOrders)
        {
            var reorderResult = await TryReorderOrderItemsViaLanApiAsync(
                order,
                lanApiBaseUrl,
                actor,
                cancellationToken);

            if (reorderResult.IsSuccess && reorderResult.Order != null)
            {
                UpsertOrderInHistory(orderHistory, reorderResult.Order);
                continue;
            }

            if (reorderResult.CurrentVersion > 0)
                order.StorageVersion = reorderResult.CurrentVersion;

            var errorText = string.IsNullOrWhiteSpace(reorderResult.Error)
                ? "LAN reorder failed"
                : reorderResult.Error;
            logs.Add(new OrderRunFeedbackLogEntry(
                $"LAN-API | item-reorder-sync-failed | reason={reason} | order={ResolveOrderDisplayId(order, orderDisplayIdResolver)} | conflict={(reorderResult.IsConflict ? "1" : "0")} | unavailable={(reorderResult.IsUnavailable ? "1" : "0")} | {errorText}",
                isWarning: true));
        }

        return LanOrderItemsReorderSyncOutcome.From($"lan-api-item-reorder-{reason}", logs);
    }

    public async Task<LanOrderItemSyncOutcome> SyncLanOrderItemUpsertAsync(
        OrderData order,
        OrderFileItem item,
        string lanApiBaseUrl,
        string actor,
        string reason,
        Func<OrderData, string> orderDisplayIdResolver,
        CancellationToken cancellationToken = default)
    {
        if (order == null || item == null)
            return LanOrderItemSyncOutcome.Noop();

        var upsertResult = await TryUpsertOrderItemViaLanApiAsync(
            order,
            item,
            lanApiBaseUrl,
            actor,
            cancellationToken);

        if (upsertResult.IsSuccess && upsertResult.Order != null)
        {
            ApplyLanOrderItemVersionsSnapshot(order, upsertResult.Order);
            return LanOrderItemSyncOutcome.Success($"lan-api-item-upsert-{reason}");
        }

        if (upsertResult.CurrentVersion > 0)
            order.StorageVersion = upsertResult.CurrentVersion;

        var errorText = string.IsNullOrWhiteSpace(upsertResult.Error)
            ? "LAN item upsert failed"
            : upsertResult.Error;
        return LanOrderItemSyncOutcome.Failed(
            snapshotRefreshReason: $"lan-api-item-upsert-failed-{reason}",
            logs:
            [
                new OrderRunFeedbackLogEntry(
                    $"LAN-API | item-upsert-sync-failed | reason={reason} | order={ResolveOrderDisplayId(order, orderDisplayIdResolver)} | item={item.ItemId} | conflict={(upsertResult.IsConflict ? "1" : "0")} | unavailable={(upsertResult.IsUnavailable ? "1" : "0")} | {errorText}",
                    isWarning: true)
            ]);
    }

    public async Task<LanOrderItemSyncOutcome> SyncLanOrderItemDeleteAsync(
        OrderData order,
        OrderFileItem item,
        string lanApiBaseUrl,
        string actor,
        string reason,
        Func<OrderData, string> orderDisplayIdResolver,
        CancellationToken cancellationToken = default)
    {
        if (order == null || item == null)
            return LanOrderItemSyncOutcome.Noop();

        var deleteResult = await TryDeleteOrderItemViaLanApiAsync(
            order,
            item,
            lanApiBaseUrl,
            actor,
            cancellationToken);

        if (deleteResult.IsSuccess && deleteResult.Order != null)
        {
            ApplyLanOrderItemDeleteSnapshot(order, deleteResult.Order);
            return LanOrderItemSyncOutcome.Success($"lan-api-item-delete-{reason}");
        }

        if (deleteResult.CurrentVersion > 0)
            order.StorageVersion = deleteResult.CurrentVersion;

        var errorText = string.IsNullOrWhiteSpace(deleteResult.Error)
            ? "LAN item delete failed"
            : deleteResult.Error;
        return LanOrderItemSyncOutcome.Failed(
            snapshotRefreshReason: $"lan-api-item-delete-failed-{reason}",
            logs:
            [
                new OrderRunFeedbackLogEntry(
                    $"LAN-API | item-delete-sync-failed | reason={reason} | order={ResolveOrderDisplayId(order, orderDisplayIdResolver)} | item={item.ItemId} | conflict={(deleteResult.IsConflict ? "1" : "0")} | unavailable={(deleteResult.IsUnavailable ? "1" : "0")} | {errorText}",
                    isWarning: true)
            ]);
    }

    private static string ResolveOrderDisplayId(OrderData order, Func<OrderData, string> orderDisplayIdResolver)
    {
        if (order == null)
            return "-";

        if (orderDisplayIdResolver != null)
        {
            var resolved = orderDisplayIdResolver(order);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved.Trim();
        }

        if (!string.IsNullOrWhiteSpace(order.Id))
            return order.Id.Trim();
        if (!string.IsNullOrWhiteSpace(order.InternalId))
            return order.InternalId.Trim();
        return "-";
    }

    private static OrderStatusUiRefreshMode ResolveStatusUiRefreshMode(string source, bool rebuildGrid)
    {
        if (!rebuildGrid)
            return OrderStatusUiRefreshMode.None;

        return string.Equals(source, OrderStatusSourceNames.Processor, StringComparison.OrdinalIgnoreCase)
            ? OrderStatusUiRefreshMode.Coalesced
            : OrderStatusUiRefreshMode.FastRowsThenRebuild;
    }
}

public sealed class LanOrderWriteApplyOutcome
{
    private LanOrderWriteApplyOutcome(
        bool isSuccess,
        OrderData? order,
        bool shouldRefreshSnapshot,
        string snapshotRefreshReason,
        string bottomStatus,
        OrderRunFeedbackDialog? dialog)
    {
        IsSuccess = isSuccess;
        Order = order;
        ShouldRefreshSnapshot = shouldRefreshSnapshot;
        SnapshotRefreshReason = snapshotRefreshReason ?? string.Empty;
        BottomStatus = bottomStatus ?? string.Empty;
        Dialog = dialog;
    }

    public bool IsSuccess { get; }
    public OrderData? Order { get; }
    public bool ShouldRefreshSnapshot { get; }
    public string SnapshotRefreshReason { get; }
    public string BottomStatus { get; }
    public OrderRunFeedbackDialog? Dialog { get; }

    public static LanOrderWriteApplyOutcome Success(OrderData order, string snapshotRefreshReason)
        => new(
            isSuccess: true,
            order: order,
            shouldRefreshSnapshot: !string.IsNullOrWhiteSpace(snapshotRefreshReason),
            snapshotRefreshReason: snapshotRefreshReason,
            bottomStatus: string.Empty,
            dialog: null);

    public static LanOrderWriteApplyOutcome Failed(
        string bottomStatus,
        OrderRunFeedbackDialog? dialog,
        bool shouldRefreshSnapshot,
        string snapshotRefreshReason)
        => new(
            isSuccess: false,
            order: null,
            shouldRefreshSnapshot: shouldRefreshSnapshot,
            snapshotRefreshReason: snapshotRefreshReason,
            bottomStatus: bottomStatus,
            dialog: dialog);
}

public sealed class LanOrderItemsReorderSyncOutcome
{
    private LanOrderItemsReorderSyncOutcome(bool shouldRefreshSnapshot, string snapshotRefreshReason, IReadOnlyList<OrderRunFeedbackLogEntry>? logs)
    {
        ShouldRefreshSnapshot = shouldRefreshSnapshot;
        SnapshotRefreshReason = snapshotRefreshReason ?? string.Empty;
        Logs = logs ?? Array.Empty<OrderRunFeedbackLogEntry>();
    }

    public bool ShouldRefreshSnapshot { get; }
    public string SnapshotRefreshReason { get; }
    public IReadOnlyList<OrderRunFeedbackLogEntry> Logs { get; }

    public static LanOrderItemsReorderSyncOutcome Noop()
        => new(
            shouldRefreshSnapshot: false,
            snapshotRefreshReason: string.Empty,
            logs: Array.Empty<OrderRunFeedbackLogEntry>());

    public static LanOrderItemsReorderSyncOutcome From(string snapshotRefreshReason, IReadOnlyList<OrderRunFeedbackLogEntry>? logs)
        => new(
            shouldRefreshSnapshot: !string.IsNullOrWhiteSpace(snapshotRefreshReason),
            snapshotRefreshReason: snapshotRefreshReason,
            logs: logs);
}

public sealed class LanOrderItemSyncOutcome
{
    private LanOrderItemSyncOutcome(bool isSuccess, bool shouldRefreshSnapshot, string snapshotRefreshReason, IReadOnlyList<OrderRunFeedbackLogEntry>? logs)
    {
        IsSuccess = isSuccess;
        ShouldRefreshSnapshot = shouldRefreshSnapshot;
        SnapshotRefreshReason = snapshotRefreshReason ?? string.Empty;
        Logs = logs ?? Array.Empty<OrderRunFeedbackLogEntry>();
    }

    public bool IsSuccess { get; }
    public bool ShouldRefreshSnapshot { get; }
    public string SnapshotRefreshReason { get; }
    public IReadOnlyList<OrderRunFeedbackLogEntry> Logs { get; }

    public static LanOrderItemSyncOutcome Noop()
        => new(
            isSuccess: false,
            shouldRefreshSnapshot: false,
            snapshotRefreshReason: string.Empty,
            logs: Array.Empty<OrderRunFeedbackLogEntry>());

    public static LanOrderItemSyncOutcome Success(string snapshotRefreshReason)
        => new(
            isSuccess: true,
            shouldRefreshSnapshot: !string.IsNullOrWhiteSpace(snapshotRefreshReason),
            snapshotRefreshReason: snapshotRefreshReason,
            logs: Array.Empty<OrderRunFeedbackLogEntry>());

    public static LanOrderItemSyncOutcome Failed(string snapshotRefreshReason, IReadOnlyList<OrderRunFeedbackLogEntry>? logs)
        => new(
            isSuccess: false,
            shouldRefreshSnapshot: !string.IsNullOrWhiteSpace(snapshotRefreshReason),
            snapshotRefreshReason: snapshotRefreshReason,
            logs: logs);
}

public sealed class LanOrderStatusPersistOutcome
{
    private LanOrderStatusPersistOutcome(bool isPersisted, bool shouldRefreshSnapshot, string snapshotRefreshReason, IReadOnlyList<OrderRunFeedbackLogEntry>? logs)
    {
        IsPersisted = isPersisted;
        ShouldRefreshSnapshot = shouldRefreshSnapshot;
        SnapshotRefreshReason = snapshotRefreshReason ?? string.Empty;
        Logs = logs ?? Array.Empty<OrderRunFeedbackLogEntry>();
    }

    public bool IsPersisted { get; }
    public bool ShouldRefreshSnapshot { get; }
    public string SnapshotRefreshReason { get; }
    public IReadOnlyList<OrderRunFeedbackLogEntry> Logs { get; }

    public static LanOrderStatusPersistOutcome Persisted(string snapshotRefreshReason)
        => new(
            isPersisted: true,
            shouldRefreshSnapshot: !string.IsNullOrWhiteSpace(snapshotRefreshReason),
            snapshotRefreshReason: snapshotRefreshReason,
            logs: Array.Empty<OrderRunFeedbackLogEntry>());

    public static LanOrderStatusPersistOutcome NotPersisted(IReadOnlyList<OrderRunFeedbackLogEntry>? logs)
        => new(
            isPersisted: false,
            shouldRefreshSnapshot: false,
            snapshotRefreshReason: string.Empty,
            logs: logs);
}

public sealed class OrderStatusApplyOutcome
{
    private OrderStatusApplyOutcome(
        bool changed,
        StatusTransitionResult? transition,
        bool shouldSaveLocalHistory,
        bool shouldRefreshSnapshot,
        string snapshotRefreshReason,
        OrderStatusUiRefreshMode uiRefreshMode,
        IReadOnlyList<OrderRunFeedbackLogEntry>? logs)
    {
        Changed = changed;
        Transition = transition;
        ShouldSaveLocalHistory = shouldSaveLocalHistory;
        ShouldRefreshSnapshot = shouldRefreshSnapshot;
        SnapshotRefreshReason = snapshotRefreshReason ?? string.Empty;
        UiRefreshMode = uiRefreshMode;
        Logs = logs ?? Array.Empty<OrderRunFeedbackLogEntry>();
    }

    public bool Changed { get; }
    public StatusTransitionResult? Transition { get; }
    public bool ShouldSaveLocalHistory { get; }
    public bool ShouldRefreshSnapshot { get; }
    public string SnapshotRefreshReason { get; }
    public OrderStatusUiRefreshMode UiRefreshMode { get; }
    public IReadOnlyList<OrderRunFeedbackLogEntry> Logs { get; }

    public static OrderStatusApplyOutcome NotChanged()
        => new(
            changed: false,
            transition: null,
            shouldSaveLocalHistory: false,
            shouldRefreshSnapshot: false,
            snapshotRefreshReason: string.Empty,
            uiRefreshMode: OrderStatusUiRefreshMode.None,
            logs: Array.Empty<OrderRunFeedbackLogEntry>());

    public static OrderStatusApplyOutcome FromChanged(
        StatusTransitionResult transition,
        bool shouldSaveLocalHistory,
        bool shouldRefreshSnapshot,
        string snapshotRefreshReason,
        OrderStatusUiRefreshMode uiRefreshMode,
        IReadOnlyList<OrderRunFeedbackLogEntry>? logs)
        => new(
            changed: true,
            transition: transition,
            shouldSaveLocalHistory: shouldSaveLocalHistory,
            shouldRefreshSnapshot: shouldRefreshSnapshot,
            snapshotRefreshReason: snapshotRefreshReason,
            uiRefreshMode: uiRefreshMode,
            logs: logs);
}

public enum OrderStatusUiRefreshMode
{
    None = 0,
    Coalesced = 1,
    FastRowsThenRebuild = 2
}

public enum OrderGridRefreshMode
{
    None = 0,
    FastRowsThenRebuild = 1,
    RebuildOnly = 2
}

public sealed class OrderGridMutationUiPlan
{
    private OrderGridMutationUiPlan(bool shouldSaveHistory, bool shouldUpdateActionButtons, OrderGridRefreshMode refreshMode)
    {
        ShouldSaveHistory = shouldSaveHistory;
        ShouldUpdateActionButtons = shouldUpdateActionButtons;
        RefreshMode = refreshMode;
    }

    public bool ShouldSaveHistory { get; }
    public bool ShouldUpdateActionButtons { get; }
    public OrderGridRefreshMode RefreshMode { get; }

    public static OrderGridMutationUiPlan Noop()
        => new(
            shouldSaveHistory: false,
            shouldUpdateActionButtons: false,
            refreshMode: OrderGridRefreshMode.None);

    public static OrderGridMutationUiPlan Changed()
        => new(
            shouldSaveHistory: true,
            shouldUpdateActionButtons: true,
            refreshMode: OrderGridRefreshMode.FastRowsThenRebuild);
}
