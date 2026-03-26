using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PdfiumViewer;
using Svg;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private void RefreshQueuePresentation()
        {
            treeView1.Invalidate();

            var preferredStatus = GetSelectedQueueStatusName();
            FillQueueCombo(preferredStatus);
        }

        private void InitializeOrdersViewsWarmupCoordinator()
        {
            _ordersViewWarmupCoordinator ??= new OrdersViewWarmupCoordinator(
                gridWarmupIntervalMs: OrdersGridWarmupIntervalMs,
                shouldWarmupGrid: () =>
                    !_isRebuildingGrid
                    && !IsDisposed
                    && IsHandleCreated
                    && _ordersViewMode == OrdersViewMode.Tiles,
                buildGridSignature: BuildOrdersGridWarmupSignature,
                rebuildGrid: RebuildOrdersGrid);

            _ordersViewWarmupCoordinator.Start();
        }

        private string BuildOrdersGridWarmupSignature()
        {
            if (_orderHistory.Count == 0)
                return string.Empty;

            var builder = new StringBuilder(_orderHistory.Count * 128);
            foreach (var order in _orderHistory.OrderBy(x => x.InternalId, StringComparer.Ordinal))
            {
                if (order == null)
                    continue;

                builder.Append(order.InternalId ?? string.Empty).Append('|');
                builder.Append(order.Status ?? string.Empty).Append('|');
                builder.Append(order.SourcePath ?? string.Empty).Append('|');
                builder.Append(order.PreparedPath ?? string.Empty).Append('|');
                builder.Append(order.PrintPath ?? string.Empty).Append('|');
                builder.Append(order.PitStopAction ?? string.Empty).Append('|');
                builder.Append(order.ImposingAction ?? string.Empty).Append('|');
                builder.Append(order.UserName ?? string.Empty).Append('|');
                builder.Append(order.OrderDate.Ticks).Append('|');
                builder.Append(order.ArrivalDate.Ticks).Append('|');

                if (order.Items != null)
                {
                    foreach (var item in order.Items.Where(item => item != null).OrderBy(item => item.SequenceNo))
                    {
                        builder.Append(item.ItemId ?? string.Empty).Append('|');
                        builder.Append(item.SourcePath ?? string.Empty).Append('|');
                        builder.Append(item.PreparedPath ?? string.Empty).Append('|');
                        builder.Append(item.PrintPath ?? string.Empty).Append('|');
                        builder.Append(item.FileStatus ?? string.Empty).Append('|');
                        builder.Append(item.UpdatedAt.Ticks).Append('|');
                    }
                }

                builder.Append(';');
            }

            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
            return Convert.ToHexString(hashBytes);
        }

        private void HandleOrdersGridChanged()
        {
            if (_isRebuildingGrid)
                return;

            ApplyStatusFilterToGrid();
            UpdateStatusFilterCaption();
            UpdateOrderNoSearchCaption();
            UpdateUserFilterCaption();
            UpdateCreatedDateFilterCaption();
            UpdateReceivedDateFilterCaption();
            RefreshStatusFilterChecklist();
            RefreshUserFilterChecklist();
            RefreshQueuePresentation();
            RefreshPrintTilesFromVisibleRows();
            UpdateActionButtonsState();
            RefreshTrayIndicators();
        }

        private void EnsureOrdersRepository()
        {
            _orderApplicationService.ConfigureHistoryRepository(_ordersStorageBackend, _lanPostgreSqlConnectionString, _jsonHistoryFile);
        }

        private bool TryLoadHistoryFromConfiguredRepository(out List<OrderData> orders)
        {
            EnsureOrdersRepository();
            return _orderApplicationService.TryLoadHistory(out orders);
        }

        private bool TrySaveHistoryToConfiguredRepository(out string error)
        {
            EnsureOrdersRepository();
            return _orderApplicationService.TrySaveHistory(_orderHistory, out error);
        }

        private void TryAppendRepositoryEvent(
            OrderData order,
            string itemId,
            string eventType,
            string eventSource,
            object payload)
        {
            if (order == null)
                return;

            EnsureOrdersRepository();
            var orderInternalId = order.InternalId ?? string.Empty;
            var payloadJson = payload == null
                ? "{}"
                : JsonSerializer.Serialize(payload);

            if (_orderApplicationService.TryAppendHistoryEvent(
                    orderInternalId,
                    itemId ?? string.Empty,
                    eventType ?? string.Empty,
                    eventSource ?? string.Empty,
                    payloadJson,
                    out var appendError))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(appendError))
            {
                Logger.Warn(
                    $"HISTORY | event-append-failed | backend={_orderApplicationService.OrdersRepositoryBackendName} | order={GetOrderDisplayId(order)} | event={eventType} | {appendError}");
            }
        }

        private void LoadHistory()
        {
            _orderHistory.Clear();
            _jsonHistoryFile = StoragePaths.ResolveExistingFilePath(_jsonHistoryFile, "history.json");

            if (TryLoadHistoryFromConfiguredRepository(out var loadedOrders) && loadedOrders.Count > 0)
                _orderHistory.AddRange(loadedOrders);

            var postLoad = _orderApplicationService.ApplyHistoryPostLoad(
                _orderHistory,
                NormalizeOrderUserName,
                hashBackfillBudget: 32,
                onTopologyIssue: (order, issue) =>
                    Logger.Warn($"TOPOLOGY | order={GetOrderDisplayId(order)} | {issue}"));
            if (postLoad.Changed)
            {
                foreach (var line in postLoad.MigrationLog)
                    Logger.Info(line);
                SaveHistory();
            }
        }

        private void SaveHistory()
        {
            _orderApplicationService.ApplyHistoryPreSave(_orderHistory);
            _jsonHistoryFile = StoragePaths.ResolveFilePath(_jsonHistoryFile, "history.json");

            if (TrySaveHistoryToConfiguredRepository(out var saveError))
                return;

            if (!string.IsNullOrWhiteSpace(saveError))
                Logger.Error($"HISTORY | save-failed-final | {saveError}");
        }

        private bool NormalizeOrderTopologyInHistory(bool logIssues)
        {
            return _orderApplicationService.NormalizeHistoryTopology(
                _orderHistory,
                logIssues
                    ? (order, issue) => Logger.Warn($"TOPOLOGY | order={GetOrderDisplayId(order)} | {issue}")
                    : null);
        }

        private bool BackfillMissingFileHashesIncrementally(int maxFilesToHash)
        {
            return _orderApplicationService.BackfillMissingHistoryFileHashes(
                _orderHistory,
                maxFilesToHash);
        }

        private void RebuildOrdersGrid()
        {
            if (_isRebuildingGrid)
                return;

            if (NormalizeOrderTopologyInHistory(logIssues: false))
                SaveHistory();

            _isRebuildingGrid = true;
            dgvJobs.SuspendLayout();

            try
            {
                var selectedTag = dgvJobs.CurrentRow?.Tag?.ToString();
                dgvJobs.Rows.Clear();

                var sortedOrders = _orderHistory
                    .OrderByDescending(x => x.ArrivalDate)
                    .ToList();

                var searchText = (tbSearch.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    sortedOrders = sortedOrders
                        .Where(x => OrderMatchesSearch(x, searchText))
                        .ToList();
                }

                var visibleMultiOrderIds = sortedOrders
                    .Where(OrderTopologyService.IsMultiOrder)
                    .Select(x => x.InternalId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.Ordinal);
                _expandedOrderIds.RemoveWhere(orderInternalId => !visibleMultiOrderIds.Contains(orderInternalId));

                foreach (var order in sortedOrders)
                    AddOrderRowsToGrid(order);

                if (!string.IsNullOrWhiteSpace(selectedTag))
                    TryRestoreSelectedRowByTag(selectedTag);
            }
            finally
            {
                dgvJobs.ResumeLayout();
                _isRebuildingGrid = false;
            }

            _ordersViewWarmupCoordinator?.SyncGridSignature();

            HandleOrdersGridChanged();
        }

        private void AddOrderRowsToGrid(OrderData order)
        {
            if (order == null)
                return;

            var isMultiOrder = OrderTopologyService.IsMultiOrder(order);
            var isExpanded = isMultiOrder && _expandedOrderIds.Contains(order.InternalId);
            var normalizedStatus = NormalizeStatus(order.Status) ?? (order.Status ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedStatus))
                normalizedStatus = WorkflowStatusNames.Processing;
            var displayStatus = isMultiOrder
                ? WorkflowStatusNames.Group
                : normalizedStatus;

            var sourceDisplay = string.Empty;
            var preparedDisplay = string.Empty;
            var printDisplay = string.Empty;
            var pitStopAction = NormalizeAction(order.PitStopAction);
            var imposingAction = NormalizeAction(order.ImposingAction);

            if (isMultiOrder)
            {
                // Group header must not mirror first item file fields.
                sourceDisplay = "-";
                preparedDisplay = "-";
                printDisplay = "-";
            }
            else
            {
                var sourcePath = ResolveSingleOrderDisplayPath(order, OrderStages.Source);
                var preparedPath = ResolveSingleOrderDisplayPath(order, OrderStages.Prepared);
                var printPath = ResolveSingleOrderDisplayPath(order, OrderStages.Print);
                sourceDisplay = GetFileName(sourcePath);
                preparedDisplay = GetFileName(preparedPath);
                printDisplay = GetFileName(printPath);
                pitStopAction = ResolveSingleOrderDisplayAction(order, x => x.PitStopAction, order.PitStopAction);
                imposingAction = ResolveSingleOrderDisplayAction(order, x => x.ImposingAction, order.ImposingAction);
            }

            var orderRowIndex = dgvJobs.Rows.Add(
                displayStatus,
                BuildOrderRowCaption(order, isExpanded),
                sourceDisplay,
                preparedDisplay,
                pitStopAction,
                imposingAction,
                printDisplay,
                FormatDate(order.OrderDate),
                FormatDate(order.ArrivalDate));

            dgvJobs.Rows[orderRowIndex].Tag = OrderGridLogic.BuildOrderTag(order.InternalId);

            if (isMultiOrder && isExpanded)
                AddOrderItemRowsToGrid(order, orderRowIndex);
        }

        private void AddOrderItemRowsToGrid(OrderData order, int parentRowIndex)
        {
            if (order?.Items == null || order.Items.Count == 0)
                return;

            var orderedItems = order.Items
                .Where(x => x != null)
                .OrderBy(x => x.SequenceNo)
                .ToList();
            if (orderedItems.Count == 0)
                return;

            for (var index = 0; index < orderedItems.Count; index++)
            {
                var item = orderedItems[index];
                if (string.IsNullOrWhiteSpace(item.ItemId))
                    item.ItemId = Guid.NewGuid().ToString("N");

                var itemStatus = NormalizeStatus(item.FileStatus) ?? (item.FileStatus ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(itemStatus))
                    itemStatus = WorkflowStatusNames.Waiting;

                var pitStopAction = NormalizeAction(item.PitStopAction);
                if (string.Equals(pitStopAction, "-", StringComparison.Ordinal))
                    pitStopAction = NormalizeAction(order.PitStopAction);

                var imposingAction = NormalizeAction(item.ImposingAction);
                if (string.Equals(imposingAction, "-", StringComparison.Ordinal))
                    imposingAction = NormalizeAction(order.ImposingAction);

                var orderNumberDisplay = string.IsNullOrWhiteSpace(order.Id)
                    ? string.Empty
                    : order.Id.Trim();

                // Insert item rows immediately after parent order container
                var insertIndex = parentRowIndex + 1 + index;
                dgvJobs.Rows.Insert(insertIndex,
                    itemStatus,
                    orderNumberDisplay,
                    GetFileName(item.SourcePath),
                    GetFileName(item.PreparedPath),
                    pitStopAction,
                    imposingAction,
                    GetFileName(item.PrintPath),
                    FormatDate(order.OrderDate),
                    FormatDate(order.ArrivalDate));

                dgvJobs.Rows[insertIndex].Tag = OrderGridLogic.BuildItemTag(order.InternalId, item.ItemId);
            }
        }

        private string BuildOrderRowCaption(OrderData order, bool isExpanded)
        {
            _ = isExpanded;
            return string.IsNullOrWhiteSpace(order.Id) ? string.Empty : order.Id.Trim();
        }

        private static string BuildItemRowCaption(OrderFileItem item, int index)
        {
            _ = item;
            _ = index;
            return string.Empty;
        }

        private void ToggleOrderExpanded(string orderInternalId)
        {
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return;

            if (_expandedOrderIds.Contains(orderInternalId))
                _expandedOrderIds.Remove(orderInternalId);
            else
                _expandedOrderIds.Add(orderInternalId);

            RebuildOrdersGrid();
            TryRestoreSelectedRowByTag(OrderGridLogic.BuildOrderTag(orderInternalId));
        }

        private string ResolveSingleOrderDisplayPath(OrderData order, int stage)
        {
            var orderPath = GetOrderStagePath(order, stage);
            var primaryItem = GetPrimaryItem(order);
            var itemPath = primaryItem == null ? string.Empty : GetItemStagePath(primaryItem, stage);

            if (HasExistingFile(itemPath))
                return itemPath;

            if (HasExistingFile(orderPath))
                return orderPath;

            if (stage == OrderStages.Print && TryResolveArchivedPrintPath(order, out var archivedPrintPath))
                return archivedPrintPath;

            if (!string.IsNullOrWhiteSpace(orderPath))
                return orderPath;

            return itemPath;
        }

        private static string ResolveSingleOrderDisplayAction(OrderData order, Func<OrderFileItem, string> selector, string? orderAction)
        {
            return OrderGridLogic.ResolveSingleOrderDisplayAction(order, selector, orderAction);
        }

        private static OrderFileItem? GetPrimaryItem(OrderData order)
        {
            return OrderGridLogic.GetPrimaryItem(order);
        }

        private bool OrderMatchesSearch(OrderData order, string searchText)
        {
            return OrderGridLogic.OrderMatchesSearch(order, searchText);
        }

        private void TryRestoreSelectedRowByTag(string selectedTag)
        {
            OrderGridLogic.TryRestoreSelectedRowByTag(dgvJobs, colStatus.Index, selectedTag);
        }

        private OrderData? FindOrderByInternalId(string? internalId)
        {
            return OrderGridLogic.FindOrderByInternalId(_orderHistory, internalId);
        }

        private static bool IsOrderTag(string? tag)
        {
            return OrderGridLogic.IsOrderTag(tag);
        }

        private static bool IsItemTag(string? tag)
        {
            return OrderGridLogic.IsItemTag(tag);
        }

        private static string? ExtractOrderInternalIdFromTag(string? tag)
        {
            return OrderGridLogic.ExtractOrderInternalIdFromTag(tag);
        }

        private static string? ExtractItemIdFromTag(string? tag)
        {
            return OrderGridLogic.ExtractItemIdFromTag(tag);
        }

        private static string GetOrderDisplayId(OrderData order)
        {
            return OrderGridLogic.GetOrderDisplayId(order);
        }

        private static string GetFileName(string? path)
        {
            return OrderGridLogic.GetFileName(path);
        }

        private static string FormatDate(DateTime value)
        {
            return OrderGridLogic.FormatDate(value);
        }

        private static string NormalizeAction(string? action)
        {
            return OrderGridLogic.NormalizeAction(action);
        }

        private OrderData? GetSelectedOrder()
        {
            return OrderGridLogic.GetSelectedOrder(dgvJobs, _orderHistory);
        }

        private List<OrderData> GetSelectedOrders()
        {
            return OrderGridLogic.GetSelectedOrders(dgvJobs, _orderHistory);
        }

        private bool HasSelectedOrderContainerRow()
        {
            var currentRow = dgvJobs.CurrentRow;
            if (currentRow != null && !currentRow.IsNewRow && IsOrderTag(currentRow.Tag?.ToString()))
                return true;

            return dgvJobs.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .Any(row => IsOrderTag(row.Tag?.ToString()));
        }

        private List<(OrderData Order, OrderFileItem Item)> GetSelectedOrderItems()
        {
            var selectedOrderItems = new List<(OrderData Order, OrderFileItem Item)>();
            var uniqueItemKeys = new HashSet<string>(StringComparer.Ordinal);

            var candidateRows = dgvJobs.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .OrderBy(row => row.Index)
                .ToList();

            if (candidateRows.Count == 0 && dgvJobs.CurrentRow != null && !dgvJobs.CurrentRow.IsNewRow)
                candidateRows.Add(dgvJobs.CurrentRow);

            foreach (var row in candidateRows)
            {
                var rowTag = row.Tag?.ToString();
                if (!IsItemTag(rowTag))
                    continue;

                var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
                var itemId = ExtractItemIdFromTag(rowTag);
                if (string.IsNullOrWhiteSpace(orderInternalId) || string.IsNullOrWhiteSpace(itemId))
                    continue;

                var uniqueItemKey = $"{orderInternalId}|{itemId}";
                if (!uniqueItemKeys.Add(uniqueItemKey))
                    continue;

                var order = FindOrderByInternalId(orderInternalId);
                var item = order?.Items?.FirstOrDefault(x => x != null && string.Equals(x.ItemId, itemId, StringComparison.Ordinal));
                if (order == null || item == null)
                    continue;

                selectedOrderItems.Add((order, item));
            }

            return selectedOrderItems;
        }

        private async Task RunSelectedOrderAsync()
        {
            if (!EnsureServerWriteAllowed("Запуск заказа"))
                return;

            var selectedOrders = GetSelectedOrders();
            if (selectedOrders.Count == 0)
            {
                SetBottomStatus("Выберите строку заказа для запуска");
                MessageBox.Show(this, "Выберите строку заказа для запуска.", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var correlationScope = Logger.BeginCorrelationScope();
            var useLanApi = ShouldUseLanRunApi();
            using var runScope = Logger.BeginScope(
                ("component", "main_form"),
                ("workflow", "run-selected"),
                ("selected_orders", selectedOrders.Count.ToString(CultureInfo.InvariantCulture)),
                ("use_lan_api", useLanApi ? "1" : "0"));
            Logger.Info("RUN | command-start");

            var startPhase = await _orderApplicationService.PrepareAndBeginRunAsync(
                selectedOrders,
                _runTokensByOrder,
                _runProgressByOrderInternalId,
                useLanApi: useLanApi,
                lanApiBaseUrl: _lanApiBaseUrl,
                actor: ResolveLanApiActor(),
                orderDisplayIdResolver: GetOrderDisplayId,
                tryRefreshSnapshotFromStorage: TryRefreshRepositorySnapshotFromStorage);

            var runPreparation = startPhase.Preparation;
            if (startPhase.Status == OrderRunStartPhaseStatus.Fatal)
            {
                var fatalReason = string.IsNullOrWhiteSpace(runPreparation.FatalError)
                    ? "LAN API недоступен"
                    : runPreparation.FatalError;
                Logger.Warn($"RUN | command-fatal | {fatalReason}");
                SetBottomStatus($"Сервер недоступен: {fatalReason}");
                MessageBox.Show(
                    this,
                    $"Не удалось запустить заказ через LAN API: {fatalReason}",
                    "Запуск",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var runPlan = runPreparation.RunPlan;
            var runnableOrders = runPreparation.RunnableOrders;
            var serverSkipped = runPreparation.SkippedByServer;

            if (startPhase.Status == OrderRunStartPhaseStatus.NoRunnable)
            {
                var details = string.IsNullOrWhiteSpace(startPhase.NoRunnableDetails)
                    ? OrderRunStateService.BuildNoRunnableDetails(runPlan)
                    : startPhase.NoRunnableDetails;
                SetBottomStatus($"Нет заказов для запуска ({details})");
                MessageBox.Show(
                    this,
                    $"Нет заказов для запуска ({details}).",
                    "Запуск",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (startPhase.Status == OrderRunStartPhaseStatus.ServerRejected)
            {
                Logger.Warn("RUN | command-rejected-by-server");
                var skippedPreview = _orderApplicationService.BuildRunServerSkippedPreview(serverSkipped);

                SetBottomStatus("Сервер не подтвердил запуск выбранных заказов");
                MessageBox.Show(
                    this,
                    string.IsNullOrWhiteSpace(skippedPreview)
                        ? "Сервер не подтвердил запуск выбранных заказов."
                        : $"Сервер не подтвердил запуск выбранных заказов:{Environment.NewLine}{skippedPreview}",
                    "Запуск",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (_processor == null)
                InitializeProcessor();

            if (runPreparation.SnapshotRefreshFailed)
            {
                Logger.Warn("RUN | snapshot-refresh-failed | reason=run-start | save may conflict on next history write");
            }

            var runSessions = startPhase.RunSessions;

            foreach (var session in runSessions)
            {
                var order = session.Order;
                AppendOrderOperationLog(
                    order,
                    OrderOperationNames.Run,
                    runnableOrders.Count > 1
                        ? "Пакетный запуск заказа из OrdersWorkspaceForm"
                        : "Запуск заказа из OrdersWorkspaceForm");

                SetOrderStatus(
                    order,
                    WorkflowStatusNames.Processing,
                    OrderStatusSourceNames.Ui,
                    runnableOrders.Count > 1 ? "Пакетный запуск из OrdersWorkspaceForm" : "Запуск из OrdersWorkspaceForm",
                    persistHistory: false,
                    rebuildGrid: false);
            }

            UpdateTrayProgressIndicator();
            SaveHistory();
            RebuildOrdersGrid();

            if (runnableOrders.Count == 1)
                SetBottomStatus($"Запущен заказ {GetOrderDisplayId(runnableOrders[0])}");
            else
                SetBottomStatus($"Запущено заказов: {runnableOrders.Count}");

            if (runPlan.OrdersWithoutNumber.Count > 0 || runPlan.AlreadyRunningOrders.Count > 0 || serverSkipped.Count > 0)
            {
                var skippedDetails = _orderApplicationService.BuildRunSkippedDetails(runPlan, serverSkipped);
                SetBottomStatus(string.IsNullOrWhiteSpace(skippedDetails)
                    ? "Часть заказов пропущена"
                    : $"Часть заказов пропущена ({skippedDetails})");

                if (serverSkipped.Count > 0)
                {
                    var skippedPreview = _orderApplicationService.BuildRunServerSkippedPreview(serverSkipped);

                    MessageBox.Show(
                        this,
                        string.IsNullOrWhiteSpace(skippedPreview)
                            ? "Часть заказов не запущена сервером."
                            : $"Часть заказов не запущена сервером:{Environment.NewLine}{skippedPreview}",
                        "Запуск",
                        MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                }
            }

            var runExecutionResult = await _orderApplicationService.ExecuteRunAsync(
                runSessions,
                _runTokensByOrder,
                _runProgressByOrderInternalId,
                runOrderAsync: (order, cancellationToken) => _processor!.RunAsync(order, cancellationToken, selectedItemIds: null),
                onCancelled: order =>
                {
                    SetOrderStatus(
                        order,
                        WorkflowStatusNames.Cancelled,
                        OrderStatusSourceNames.Ui,
                        "Остановлено пользователем",
                        persistHistory: false,
                        rebuildGrid: false);
                },
                onFailed: (order, ex) =>
                {
                    SetOrderStatus(
                        order,
                        WorkflowStatusNames.Error,
                        OrderStatusSourceNames.Ui,
                        ex.Message,
                        persistHistory: false,
                        rebuildGrid: false);
                },
                onCompleted: _ =>
                {
                    UpdateTrayProgressIndicator();
                });

            SaveHistory();
            RebuildOrdersGrid();
            UpdateActionButtonsState();

            if (runExecutionResult.Errors.Count > 0)
            {
                var errorsPreview = _orderApplicationService.BuildRunExecutionErrorsPreview(runExecutionResult.Errors, GetOrderDisplayId);

                SetBottomStatus($"Ошибок запуска: {runExecutionResult.Errors.Count}");
                MessageBox.Show(
                    this,
                    $"Некоторые заказы завершились с ошибкой:{Environment.NewLine}{errorsPreview}",
                    "Запуск",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else if (runnableOrders.Count > 1)
            {
                SetBottomStatus($"Пакетная обработка завершена: {runnableOrders.Count}");
            }

            Logger.Info($"RUN | command-finish | started={runnableOrders.Count} | errors={runExecutionResult.Errors.Count}");
        }

        private async Task StopSelectedOrderAsync()
        {
            if (!EnsureServerWriteAllowed("Остановка заказа"))
                return;

            var order = GetSelectedOrder();
            if (order == null)
            {
                SetBottomStatus("Выберите заказ для остановки");
                MessageBox.Show(this, "Выберите заказ для остановки.", "Остановка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var correlationScope = Logger.BeginCorrelationScope();
            var useLanApi = ShouldUseLanRunApi();
            using var stopScope = Logger.BeginScope(
                ("component", "main_form"),
                ("workflow", "stop-selected"),
                ("order_id", GetOrderDisplayId(order)),
                ("order_internal_id", order.InternalId),
                ("use_lan_api", useLanApi ? "1" : "0"));
            Logger.Info("RUN | stop-command-start");

            var stopPhase = await _orderApplicationService.ExecuteStopAsync(
                order: order,
                useLanApi: useLanApi,
                lanApiBaseUrl: _lanApiBaseUrl,
                actor: ResolveLanApiActor(),
                runTokensByOrder: _runTokensByOrder,
                runProgressByOrderInternalId: _runProgressByOrderInternalId,
                tryRefreshSnapshotFromStorage: TryRefreshRepositorySnapshotFromStorage,
                applyLocalStopStatus: localOrder =>
                {
                    AppendOrderOperationLog(localOrder, OrderOperationNames.Stop, "Остановлено пользователем");
                    SetOrderStatus(
                        localOrder,
                        WorkflowStatusNames.Cancelled,
                        OrderStatusSourceNames.Ui,
                        "Остановлено пользователем",
                        persistHistory: true,
                        rebuildGrid: true);
                });

            var stopPreparation = stopPhase.Preparation;

            if (!stopPreparation.CanProceed)
            {
                Logger.Warn("RUN | stop-command-skipped | reason=not-running");
                SetBottomStatus($"Заказ {GetOrderDisplayId(order)} сейчас не выполняется");
                MessageBox.Show(this, $"Заказ {GetOrderDisplayId(order)} сейчас не выполняется.", "Остановка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (stopPreparation.LocalCancellationRequested)
            {
                UpdateTrayProgressIndicator();
            }

            if (stopPreparation.SnapshotRefreshFailed)
            {
                Logger.Warn($"RUN | snapshot-refresh-failed | reason=run-stop | order={GetOrderDisplayId(order)}");
            }

            if (stopPhase.ShouldWarnServerUnavailable)
            {
                Logger.Warn($"RUN | stop-server-unavailable | order={GetOrderDisplayId(order)} | {stopPhase.ServerReason}");
                var unavailableMessage = stopPhase.Status == OrderRunStopPhaseStatus.LocalStatusApplied
                    ? $"Остановка на сервере не подтверждена ({stopPhase.ServerReason}).{Environment.NewLine}Локальная остановка будет выполнена, но серверный lock может остаться активным."
                    : $"Остановка на сервере не подтверждена ({stopPhase.ServerReason}).";
                MessageBox.Show(
                    this,
                    unavailableMessage,
                    "Остановка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            if (stopPhase.ShouldLogServerFailure)
            {
                Logger.Warn($"RUN | stop-server-failed | order={GetOrderDisplayId(order)} | {stopPhase.ServerReason}");
            }

            if (stopPhase.Status == OrderRunStopPhaseStatus.LocalStatusApplied)
            {
                UpdateActionButtonsState();
                SetBottomStatus($"Остановлен заказ {GetOrderDisplayId(order)}");
                Logger.Info("RUN | stop-command-finish | local-status-applied=1");
                return;
            }

            if (stopPhase.Status == OrderRunStopPhaseStatus.Conflict)
            {
                SetBottomStatus($"Остановка не подтверждена: конфликт версии ({GetOrderDisplayId(order)})");
                MessageBox.Show(
                    this,
                    "Сервер отклонил остановку из-за конфликта версии. Обновите заказ и повторите операцию.",
                    "Остановка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            SetBottomStatus($"Сервер не подтвердил остановку {GetOrderDisplayId(order)}");
            Logger.Warn("RUN | stop-command-finish | local-status-applied=0");
        }

        private bool ShouldUseLanRunApi()
        {
            return _ordersStorageBackend == OrdersStorageMode.LanPostgreSql
                && !string.IsNullOrWhiteSpace(_lanApiBaseUrl);
        }

        private string ResolveLanApiActor()
        {
            var displayName = string.IsNullOrWhiteSpace(_currentUserName)
                ? GetDefaultUserName()
                : _currentUserName.Trim();

            if (_serverUsersByDisplayName.TryGetValue(displayName, out var serverUserName) &&
                !string.IsNullOrWhiteSpace(serverUserName))
            {
                return serverUserName.Trim();
            }

            var fallbackDisplayName = GetDefaultUserName();
            if (_serverUsersByDisplayName.TryGetValue(fallbackDisplayName, out var fallbackServerUserName) &&
                !string.IsNullOrWhiteSpace(fallbackServerUserName))
            {
                return fallbackServerUserName.Trim();
            }

            return UserIdentityResolver.ResolveServerUserName(displayName);
        }

        private bool TryRefreshRepositorySnapshotFromStorage(IReadOnlyCollection<OrderData> localOrders, string reason)
        {
            if (_ordersStorageBackend != OrdersStorageMode.LanPostgreSql)
                return true;

            if (!TryLoadHistoryFromConfiguredRepository(out var reloadedOrders))
            {
                Logger.Warn($"HISTORY | snapshot-refresh-failed | reason={reason} | backend={_orderApplicationService.OrdersRepositoryBackendName}");
                return false;
            }

            _orderApplicationService.SyncStorageVersions(localOrders, reloadedOrders);
            return true;
        }

        private async Task RemoveSelectedOrderAsync()
        {
            if (!EnsureServerWriteAllowed("Удаление заказа"))
                return;

            var selectedOrderItems = GetSelectedOrderItems();
            var hasSelectedOrderContainers = HasSelectedOrderContainerRow();
            if (!hasSelectedOrderContainers && selectedOrderItems.Count > 0)
            {
                await RemoveSelectedOrderItemsAsync(selectedOrderItems);
                return;
            }

            var selectedOrders = GetSelectedOrders();
            if (selectedOrders.Count == 0)
            {
                SetBottomStatus("Выберите заказ для удаления");
                MessageBox.Show(this, "Выберите заказ для удаления.", "Удаление", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var isBatchDelete = selectedOrders.Count > 1;
            var firstOrder = selectedOrders[0];
            var confirmationText = isBatchDelete
                ? $"Выбрано заказов: {selectedOrders.Count}\n\n" +
                  "Удалить папки заказов с диска?\n\n" +
                  "[Да] — удалить с диска и из списка.\n" +
                  "[Нет] — только удалить из списка.\n" +
                  "[Отмена] — ничего не менять."
                : $"Заказ №{GetOrderDisplayId(firstOrder)}\n\n" +
                  "Удалить папку заказа с диска?\n\n" +
                  "[Да] — удалить с диска и из списка.\n" +
                  "[Нет] — только удалить из списка.\n" +
                  "[Отмена] — ничего не менять.";

            var decision = MessageBox.Show(
                this,
                confirmationText,
                isBatchDelete ? "Удаление заказов" : "Удаление заказа",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (decision == DialogResult.Cancel)
            {
                SetBottomStatus("Удаление отменено");
                return;
            }

            var removeFilesFromDisk = decision == DialogResult.Yes;
            if (ShouldUseLanRunApi())
            {
                await RemoveSelectedOrdersViaLanApiAsync(
                    selectedOrders,
                    removeFilesFromDisk,
                    isBatchDelete,
                    firstOrder);
                return;
            }

            var commandResult = _orderApplicationService.DeleteOrders(
                _orderHistory,
                selectedOrders,
                removeFilesFromDisk,
                _ordersRootPath,
                _runTokensByOrder,
                _runProgressByOrderInternalId,
                _expandedOrderIds,
                (order, removeFromDisk) =>
                {
                    AppendOrderOperationLog(
                        order,
                        OrderOperationNames.Delete,
                        removeFromDisk
                            ? "Удален из списка и с диска"
                            : "Удален из списка");
                });

            var deleteResult = commandResult.DeleteResult;
            UpdateTrayProgressIndicator();

            if (deleteResult.RemovedCount > 0)
            {
                SaveHistory();
                RebuildOrdersGrid();
                UpdateActionButtonsState();
                SetBottomStatus(isBatchDelete
                    ? $"Удалено заказов: {deleteResult.RemovedCount}"
                    : $"Заказ {GetOrderDisplayId(firstOrder)} удален");
            }
            else
            {
                SetBottomStatus("Удаление не выполнено");
            }

            if (deleteResult.FailedOrders.Count == 0)
                return;

            var failedOrders = deleteResult.FailedOrders
                .Select(x => $"{GetOrderDisplayId(x.Order)}: {x.Message}")
                .ToList();

            var failedPreview = string.Join(Environment.NewLine, failedOrders.Take(5));
            if (failedOrders.Count > 5)
                failedPreview += $"{Environment.NewLine}... ещё: {failedOrders.Count - 5}";

            MessageBox.Show(
                this,
                $"Не удалось удалить некоторые заказы:{Environment.NewLine}{failedPreview}",
                isBatchDelete ? "Удаление заказов" : "Удаление заказа",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private async Task RemoveSelectedOrdersViaLanApiAsync(
            IReadOnlyCollection<OrderData> selectedOrders,
            bool removeFilesFromDisk,
            bool isBatchDelete,
            OrderData firstOrder)
        {
            if (selectedOrders == null || selectedOrders.Count == 0)
                return;

            var normalizedOrders = selectedOrders
                .Where(order => order != null)
                .DistinctBy(order => order.InternalId, StringComparer.Ordinal)
                .ToList();

            var removedCount = 0;
            var failures = new List<string>();
            foreach (var order in normalizedOrders)
            {
                if (removeFilesFromDisk)
                {
                    var fileDeleteResult = await Task.Run(() =>
                    {
                        var success = TryDeleteOrderArtifactsFromDisk(order, _ordersRootPath, out var fileDeleteError);
                        return (success, fileDeleteError);
                    });

                    if (!fileDeleteResult.success)
                    {
                        failures.Add($"{GetOrderDisplayId(order)}: {fileDeleteResult.fileDeleteError}");
                        continue;
                    }
                }

                var deleteResult = _orderApplicationService
                    .TryDeleteOrderViaLanApiAsync(
                        order,
                        _lanApiBaseUrl,
                        ResolveLanApiActor());
                var deleteResultValue = await deleteResult;

                if (deleteResultValue.IsSuccess)
                {
                    if (_runTokensByOrder.TryGetValue(order.InternalId, out var cts))
                    {
                        cts.Cancel();
                        cts.Dispose();
                        _runTokensByOrder.Remove(order.InternalId);
                    }

                    _runProgressByOrderInternalId.Remove(order.InternalId);
                    _expandedOrderIds.Remove(order.InternalId);
                    _orderHistory.Remove(order);
                    AppendOrderOperationLog(
                        order,
                        OrderOperationNames.Delete,
                        removeFilesFromDisk
                            ? "Удален из списка и с диска"
                            : "Удален из списка");
                    removedCount++;
                    continue;
                }

                if (deleteResultValue.CurrentVersion > 0)
                    order.StorageVersion = deleteResultValue.CurrentVersion;

                var errorText = string.IsNullOrWhiteSpace(deleteResultValue.Error)
                    ? "LAN API delete order failed"
                    : deleteResultValue.Error;
                failures.Add($"{GetOrderDisplayId(order)}: {errorText}");
            }

            UpdateTrayProgressIndicator();

            if (removedCount > 0)
            {
                SaveHistory();
                TryRefreshRepositorySnapshotFromStorage(_orderHistory, "lan-api-remove-selected-orders");
                RebuildOrdersGrid();
                UpdateActionButtonsState();
                SetBottomStatus(isBatchDelete
                    ? $"Удалено заказов: {removedCount}"
                    : $"Заказ {GetOrderDisplayId(firstOrder)} удален");
            }
            else
            {
                SetBottomStatus("Удаление не выполнено");
            }

            if (failures.Count == 0)
                return;

            var failedPreview = string.Join(Environment.NewLine, failures.Take(5));
            if (failures.Count > 5)
                failedPreview += $"{Environment.NewLine}... ещё: {failures.Count - 5}";

            MessageBox.Show(
                this,
                $"Не удалось удалить некоторые заказы:{Environment.NewLine}{failedPreview}",
                isBatchDelete ? "Удаление заказов" : "Удаление заказа",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static bool TryDeleteOrderArtifactsFromDisk(OrderData order, string ordersRootPath, out string error)
        {
            error = string.Empty;
            if (order == null)
                return true;

            var orderFolder = string.IsNullOrWhiteSpace(order.FolderName)
                ? string.Empty
                : Path.Combine(ordersRootPath ?? string.Empty, order.FolderName);
            if (!string.IsNullOrWhiteSpace(orderFolder) && Directory.Exists(orderFolder))
            {
                try
                {
                    Directory.Delete(orderFolder, recursive: true);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Не удалось удалить папку заказа: {ex.Message}";
                    return false;
                }
            }

            foreach (var rawPath in EnumerateKnownOrderFilePaths(order))
            {
                var path = CleanPath(rawPath);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    error = $"Не удалось удалить файл {Path.GetFileName(path)}: {ex.Message}";
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<string> EnumerateKnownOrderFilePaths(OrderData order)
        {
            if (order == null)
                yield break;

            if (!string.IsNullOrWhiteSpace(order.SourcePath))
                yield return order.SourcePath;
            if (!string.IsNullOrWhiteSpace(order.PreparedPath))
                yield return order.PreparedPath;
            if (!string.IsNullOrWhiteSpace(order.PrintPath))
                yield return order.PrintPath;

            if (order.Items == null)
                yield break;

            foreach (var item in order.Items.Where(item => item != null))
            {
                if (!string.IsNullOrWhiteSpace(item.SourcePath))
                    yield return item.SourcePath;
                if (!string.IsNullOrWhiteSpace(item.PreparedPath))
                    yield return item.PreparedPath;
                if (!string.IsNullOrWhiteSpace(item.PrintPath))
                    yield return item.PrintPath;

                if (item.TechnicalFiles == null)
                    continue;

                foreach (var technicalFile in item.TechnicalFiles)
                {
                    if (!string.IsNullOrWhiteSpace(technicalFile))
                        yield return technicalFile;
                }
            }
        }

        private async Task RemoveSelectedOrderItemsAsync(List<(OrderData Order, OrderFileItem Item)> selectedOrderItems)
        {
            if (selectedOrderItems == null || selectedOrderItems.Count == 0)
            {
                SetBottomStatus("Выберите файл для удаления");
                return;
            }

            var isBatchDelete = selectedOrderItems.Count > 1;
            var firstSelection = selectedOrderItems[0];
            var firstItemName = OrderDeletionWorkflowService.BuildOrderItemDisplayName(firstSelection.Item);
            var confirmationText = isBatchDelete
                ? $"Выбрано файлов: {selectedOrderItems.Count}\n\n" +
                  "Удалить файлы с диска?\n\n" +
                  "Да — удалить с диска и из групп.\n" +
                  "Нет — удалить только из групп."
                : $"Заказ №{GetOrderDisplayId(firstSelection.Order)}\n" +
                  $"Файл: {firstItemName}\n\n" +
                  "Удалить файл с диска?\n\n" +
                  "Да — удалить с диска и из группы.\n" +
                  "Нет — удалить только из группы.";

            var decision = MessageBox.Show(
                this,
                confirmationText,
                isBatchDelete ? "Удаление файлов" : "Удаление файла",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (decision == DialogResult.Cancel)
            {
                SetBottomStatus("Удаление отменено");
                return;
            }

            var removeFilesFromDisk = decision == DialogResult.Yes;
            if (ShouldUseLanRunApi())
            {
                await RemoveSelectedOrderItemsViaLanApiAsync(
                    selectedOrderItems,
                    removeFilesFromDisk,
                    isBatchDelete,
                    firstItemName);
                return;
            }

            var affectedOrders = selectedOrderItems
                .Select(selection => selection.Order)
                .Where(order => order != null)
                .DistinctBy(order => order.InternalId, StringComparer.Ordinal)
                .ToList();
            var commandResult = _orderApplicationService.DeleteOrderItems(
                selectedOrderItems.Select(x => new OrderItemSelection(x.Order, x.Item)).ToList(),
                removeFilesFromDisk,
                (order, _, itemName) =>
                {
                    AppendOrderOperationLog(
                        order,
                        OrderOperationNames.RemoveItem,
                        removeFilesFromDisk
                            ? $"Удален файл: {itemName} (с диска и из группы)"
                            : $"Удален файл: {itemName} (из группы)");
                });

            foreach (var topologyMutation in commandResult.TopologyMutations)
            {
                ApplyTopologyMutationResult(
                    topologyMutation.Order,
                    topologyMutation.MutationResult,
                    "remove-item-row");
            }

            var deleteResult = commandResult.DeleteResult;
            if (deleteResult.RemovedCount > 0)
            {
                SaveHistory();
                TrySyncLanItemReorderForOrders(affectedOrders, "remove-selected-items");
                RebuildOrdersGrid();
                UpdateActionButtonsState();
                SetBottomStatus(isBatchDelete
                    ? $"Удалено файлов: {deleteResult.RemovedCount}"
                    : $"Файл {firstItemName} удален");
            }
            else
            {
                SetBottomStatus("Удаление файла не выполнено");
            }

            if (deleteResult.FailedItems.Count == 0)
                return;

            var failedItems = deleteResult.FailedItems
                .Select(x => $"{GetOrderDisplayId(x.Order)} / {x.ItemDisplayName}: {x.Message}")
                .ToList();

            var failedPreview = string.Join(Environment.NewLine, failedItems.Take(5));
            if (failedItems.Count > 5)
                failedPreview += $"{Environment.NewLine}... ещё: {failedItems.Count - 5}";

            MessageBox.Show(
                this,
                $"Не удалось удалить некоторые файлы:{Environment.NewLine}{failedPreview}",
                isBatchDelete ? "Удаление файлов" : "Удаление файла",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private async Task RemoveSelectedOrderItemsViaLanApiAsync(
            IReadOnlyCollection<(OrderData Order, OrderFileItem Item)> selectedOrderItems,
            bool removeFilesFromDisk,
            bool isBatchDelete,
            string firstItemName)
        {
            if (selectedOrderItems == null || selectedOrderItems.Count == 0)
                return;

            var normalizedSelections = selectedOrderItems
                .Where(x => x.Order != null && x.Item != null)
                .DistinctBy(x => $"{x.Order.InternalId}::{x.Item.ItemId}", StringComparer.Ordinal)
                .ToList();

            var removedCount = 0;
            var failures = new List<string>();
            foreach (var (order, item) in normalizedSelections)
            {
                if (removeFilesFromDisk)
                {
                    var fileDeleteResult = await Task.Run(() =>
                    {
                        var success = TryDeleteOrderItemFilesFromDisk(item, out var fileDeleteError);
                        return (success, fileDeleteError);
                    });

                    if (!fileDeleteResult.success)
                    {
                        failures.Add($"{GetOrderDisplayId(order)} / {OrderDeletionWorkflowService.BuildOrderItemDisplayName(item)}: {fileDeleteResult.fileDeleteError}");
                        continue;
                    }
                }

                var deleteResult = _orderApplicationService
                    .TryDeleteOrderItemViaLanApiAsync(
                        order,
                        item,
                        _lanApiBaseUrl,
                        ResolveLanApiActor());
                var deleteResultValue = await deleteResult;

                if (deleteResultValue.IsSuccess && deleteResultValue.Order != null)
                {
                    UpsertOrderInHistory(deleteResultValue.Order);
                    AppendOrderOperationLog(
                        order,
                        OrderOperationNames.RemoveItem,
                        removeFilesFromDisk
                            ? $"Удален файл: {OrderDeletionWorkflowService.BuildOrderItemDisplayName(item)} (с диска и из группы)"
                            : $"Удален файл: {OrderDeletionWorkflowService.BuildOrderItemDisplayName(item)} (из группы)");
                    removedCount++;
                    continue;
                }

                if (deleteResultValue.CurrentVersion > 0)
                    order.StorageVersion = deleteResultValue.CurrentVersion;

                var errorText = string.IsNullOrWhiteSpace(deleteResultValue.Error)
                    ? "LAN API delete item failed"
                    : deleteResultValue.Error;
                failures.Add($"{GetOrderDisplayId(order)} / {OrderDeletionWorkflowService.BuildOrderItemDisplayName(item)}: {errorText}");
            }

            if (removedCount > 0)
            {
                SaveHistory();
                TryRefreshRepositorySnapshotFromStorage(_orderHistory, "lan-api-remove-selected-items");
                RebuildOrdersGrid();
                UpdateActionButtonsState();
                SetBottomStatus(isBatchDelete
                    ? $"Удалено файлов: {removedCount}"
                    : $"Файл {firstItemName} удален");
            }
            else
            {
                SetBottomStatus("Удаление файла не выполнено");
            }

            if (failures.Count == 0)
                return;

            var failedPreview = string.Join(Environment.NewLine, failures.Take(5));
            if (failures.Count > 5)
                failedPreview += $"{Environment.NewLine}... ещё: {failures.Count - 5}";

            MessageBox.Show(
                this,
                $"Не удалось удалить некоторые файлы:{Environment.NewLine}{failedPreview}",
                isBatchDelete ? "Удаление файлов" : "Удаление файла",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static bool TryDeleteOrderItemFilesFromDisk(OrderFileItem item, out string error)
        {
            error = string.Empty;
            if (item == null)
                return true;

            var paths = new[] { item.SourcePath, item.PreparedPath, item.PrintPath };
            foreach (var rawPath in paths)
            {
                var path = CleanPath(rawPath);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    error = $"Не удалось удалить файл {Path.GetFileName(path)}: {ex.Message}";
                    return false;
                }
            }

            return true;
        }

        private void OpenLogForSelectionOrManager()
        {
            AcknowledgeErrorNotifications();

            var order = GetSelectedOrder();
            if (order != null)
            {
                var orderLogPath = GetOrderLogFilePath(order);
                if (File.Exists(orderLogPath))
                {
                    using var viewer = new OrderLogViewerForm(orderLogPath, GetOrderDisplayId(order));
                    viewer.ShowDialog(this);
                    return;
                }
            }

            if (!File.Exists(_managerLogFilePath))
            {
                SetBottomStatus("Лог пока не создан");
                MessageBox.Show(this, "Лог пока не создан.", "Лог", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _managerLogFilePath,
                UseShellExecute = true
            });
            SetBottomStatus("Открыт лог менеджера");
        }

        private void OpenFolderForSelectedOrder()
        {
            var order = GetSelectedOrder();
            string targetPath;

            if (order == null)
            {
                targetPath = !string.IsNullOrWhiteSpace(_ordersRootPath)
                    ? _ordersRootPath
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            else
            {
                if (!TryGetBrowseFolderPathForOrder(order, out targetPath, out var reason))
                {
                    SetBottomStatus(reason);
                    MessageBox.Show(this, reason, "Папка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            OpenOrderFolderPath(targetPath);
        }

        private void OpenOrderStageFolder(OrderData order, int stage)
        {
            if (order == null)
                return;

            string targetPath;
            if (OrderStages.IsFileStage(stage))
            {
                var stageFilePath = ResolveSingleOrderDisplayPath(order, stage);
                if (HasExistingFile(stageFilePath))
                    targetPath = Path.GetDirectoryName(stageFilePath) ?? GetStageFolder(order, stage);
                else
                    targetPath = GetStageFolder(order, stage);
            }
            else
            {
                if (!TryGetBrowseFolderPathForOrder(order, out targetPath, out _))
                    targetPath = GetPreferredOrderFolder(order);
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                SetBottomStatus("Папка не определена");
                MessageBox.Show(this, "Папка не определена.", "Папка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Directory.CreateDirectory(targetPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });
            SetBottomStatus($"Открыта папка этапа: {targetPath}");
        }

        private bool TryGetBrowseFolderPathForOrder(OrderData order, out string folderPath, out string reason)
        {
            var resolution = _orderApplicationService.ResolveBrowseFolderPath(
                order,
                _ordersRootPath,
                _tempRootPath);
            folderPath = resolution.FolderPath;
            reason = resolution.Reason;
            return resolution.Success;
        }

        private void OpenOrderFolderPath(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                SetBottomStatus("Папка не определена");
                MessageBox.Show(this, "Папка не определена.", "Папка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Directory.CreateDirectory(targetPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetPath,
                    UseShellExecute = true
                });
                SetBottomStatus($"Открыта папка: {targetPath}");
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось открыть папку: {ex.Message}");
                MessageBox.Show(this, $"Не удалось открыть папку: {ex.Message}", "Папка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetPreferredOrderFolder(OrderData order)
        {
            return _orderApplicationService.ResolvePreferredOrderFolder(
                order,
                _ordersRootPath,
                _tempRootPath);
        }

        private static string? FirstNotEmpty(params string?[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return null;
        }

        private bool SetOrderStatus(
            OrderData order,
            string status,
            string source,
            string reason,
            bool persistHistory,
            bool rebuildGrid)
        {
            if (Disposing || IsDisposed)
                return false;

            if (InvokeRequired)
            {
                var result = false;
                Invoke((Action)(() =>
                {
                    result = SetOrderStatusCore(order, status, source, reason, persistHistory, rebuildGrid);
                }));
                return result;
            }

            return SetOrderStatusCore(order, status, source, reason, persistHistory, rebuildGrid);
        }

        private bool SetOrderStatusCore(
            OrderData order,
            string status,
            string source,
            string reason,
            bool persistHistory,
            bool rebuildGrid)
        {
            var transition = _orderApplicationService.ApplyStatusTransition(order, status, source, reason);
            if (!transition.Changed)
                return false;

            AppendOrderStatusLog(order, transition.OldStatus, transition.NewStatus, transition.Source, transition.Reason);
            TryAppendRepositoryEvent(
                order,
                itemId: string.Empty,
                eventType: "status-change",
                eventSource: transition.Source,
                payload: new
                {
                    old_status = transition.OldStatus,
                    new_status = transition.NewStatus,
                    reason = transition.Reason,
                    status_at = transition.StatusAt
                });

            if (persistHistory)
            {
                var persistedViaLanApi = TryPersistOrderStatusViaLanApi(order, transition.Source, transition.Reason);
                if (!persistedViaLanApi)
                {
                    if (ShouldUseLanRunApi())
                    {
                        Logger.Warn(
                            $"LAN-API | status-update-fallback-local-save | order={GetOrderDisplayId(order)} | status={transition.NewStatus}");
                    }

                    SaveHistory();
                }
            }
            if (rebuildGrid)
                RebuildOrdersGrid();

            UpdateTrayErrorIndicator();

            return true;
        }

        private static string DescribePrintWeight(OrderData order)
        {
            var size = order.PrintFileSizeBytes
                ?? order.Items?.FirstOrDefault(x => x != null && x.PrintFileSizeBytes.HasValue)?.PrintFileSizeBytes;

            return size.HasValue
                ? $"{size.Value} байт"
                : "неизвестен";
        }

    }
}

