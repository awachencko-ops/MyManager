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
    public partial class MainForm
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
            if (_ordersRepository != null)
                return;

            var settings = AppSettings.Load();
            _ordersStorageBackend = settings.OrdersStorageBackend;
            _lanPostgreSqlConnectionString = settings.LanPostgreSqlConnectionString;
            _ordersRepository = OrdersRepositoryFactory.Create(settings, _jsonHistoryFile);
        }

        private bool TryLoadHistoryFromConfiguredRepository(out List<OrderData> orders)
        {
            EnsureOrdersRepository();
            var primaryError = string.Empty;

            if (_ordersRepository != null
                && _ordersRepository.TryLoadAll(out orders, out primaryError))
            {
                if (orders.Count == 0 && _ordersStorageBackend == OrdersStorageMode.LanPostgreSql)
                {
                    var bootstrapRepository = OrdersRepositoryFactory.CreateFileSystem(_jsonHistoryFile);
                    if (bootstrapRepository.TryLoadAll(out var bootstrapOrders, out var bootstrapLoadError)
                        && bootstrapOrders.Count > 0)
                    {
                        orders = bootstrapOrders;
                        Logger.Warn(
                            $"HISTORY | bootstrap-load | source={bootstrapRepository.BackendName} | target={_ordersRepository.BackendName} | orders={bootstrapOrders.Count}");

                        if (!_ordersRepository.TrySaveAll(bootstrapOrders, out var bootstrapSaveError))
                        {
                            Logger.Warn(
                                $"HISTORY | bootstrap-save-failed | backend={_ordersRepository.BackendName} | {bootstrapSaveError}");
                        }
                        else
                        {
                            Logger.Info(
                                $"HISTORY | bootstrap-save-success | backend={_ordersRepository.BackendName} | orders={bootstrapOrders.Count}");
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(bootstrapLoadError))
                    {
                        Logger.Warn(
                            $"HISTORY | bootstrap-load-failed | backend={bootstrapRepository.BackendName} | {bootstrapLoadError}");
                    }
                }

                return true;
            }

            if (!string.IsNullOrWhiteSpace(primaryError))
            {
                Logger.Warn(
                    $"HISTORY | load-failed | backend={_ordersRepository?.BackendName ?? "unknown"} | {primaryError}");
            }

            var fallbackRepository = OrdersRepositoryFactory.CreateFileSystem(_jsonHistoryFile);
            if (fallbackRepository.TryLoadAll(out orders, out var fallbackError))
            {
                Logger.Warn(
                    $"HISTORY | fallback-load | backend={fallbackRepository.BackendName}");
                return true;
            }

            Logger.Error(
                $"HISTORY | fallback-load-failed | backend={fallbackRepository.BackendName} | {fallbackError}");
            orders = new List<OrderData>();
            return false;
        }

        private bool TrySaveHistoryToConfiguredRepository(out string error)
        {
            EnsureOrdersRepository();
            var primaryError = string.Empty;

            if (_ordersRepository != null
                && _ordersRepository.TrySaveAll(_orderHistory, out primaryError))
            {
                error = string.Empty;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(primaryError))
            {
                Logger.Warn(
                    $"HISTORY | save-failed | backend={_ordersRepository?.BackendName ?? "unknown"} | {primaryError}");

                if (_ordersStorageBackend == OrdersStorageMode.LanPostgreSql
                    && primaryError.Contains("concurrency conflict", StringComparison.OrdinalIgnoreCase))
                {
                    error = primaryError;
                    return false;
                }
            }

            var fallbackRepository = OrdersRepositoryFactory.CreateFileSystem(_jsonHistoryFile);
            if (fallbackRepository.TrySaveAll(_orderHistory, out var fallbackError))
            {
                Logger.Warn(
                    $"HISTORY | fallback-save | backend={fallbackRepository.BackendName}");
                error = string.Empty;
                return true;
            }

            error = fallbackError;
            return false;
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
            if (_ordersRepository == null)
                return;

            var orderInternalId = order.InternalId ?? string.Empty;
            var payloadJson = payload == null
                ? "{}"
                : JsonSerializer.Serialize(payload);

            if (_ordersRepository.TryAppendEvent(
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
                    $"HISTORY | event-append-failed | backend={_ordersRepository.BackendName} | order={GetOrderDisplayId(order)} | event={eventType} | {appendError}");
            }
        }

        private void LoadHistory()
        {
            _orderHistory.Clear();
            _jsonHistoryFile = StoragePaths.ResolveExistingFilePath(_jsonHistoryFile, "history.json");
            var usersNormalized = false;
            var historyChanged = false;
            var migrationLog = new List<string>();
            var remainingHashBackfillBudget = 32;

            if (TryLoadHistoryFromConfiguredRepository(out var loadedOrders) && loadedOrders.Count > 0)
                _orderHistory.AddRange(loadedOrders);

            foreach (var order in _orderHistory)
            {
                if (string.IsNullOrWhiteSpace(order.InternalId))
                    order.InternalId = Guid.NewGuid().ToString("N");
                if (order.ArrivalDate == default)
                    order.ArrivalDate = order.OrderDate != default ? order.OrderDate : DateTime.Now;

                if (remainingHashBackfillBudget > 0
                    && PopulateKnownFileHashes(order, migrationLog, ref remainingHashBackfillBudget))
                    historyChanged = true;

                if (PopulateKnownFileSizes(order, migrationLog))
                    historyChanged = true;

                var normalizedUserName = NormalizeOrderUserName(order.UserName);
                if (!string.Equals(order.UserName, normalizedUserName, StringComparison.Ordinal))
                {
                    order.UserName = normalizedUserName;
                    usersNormalized = true;
                }
            }

            if (NormalizeOrderTopologyInHistory(logIssues: true) || usersNormalized || historyChanged)
            {
                foreach (var line in migrationLog)
                    Logger.Info(line);
                SaveHistory();
            }
        }

        private void SaveHistory()
        {
            NormalizeOrderTopologyInHistory(logIssues: false);
            PopulateKnownFileSizesInHistory();
            _jsonHistoryFile = StoragePaths.ResolveFilePath(_jsonHistoryFile, "history.json");

            if (TrySaveHistoryToConfiguredRepository(out var saveError))
                return;

            if (!string.IsNullOrWhiteSpace(saveError))
                Logger.Error($"HISTORY | save-failed-final | {saveError}");
        }

        private bool NormalizeOrderTopologyInHistory(bool logIssues)
        {
            if (_orderHistory.Count == 0)
                return false;

            var changed = false;
            foreach (var order in _orderHistory)
            {
                var result = OrderTopologyService.Normalize(order);
                if (result.Changed)
                    changed = true;

                if (!logIssues || result.Issues.Count == 0)
                    continue;

                foreach (var issue in result.Issues)
                    Logger.Warn($"TOPOLOGY | order={GetOrderDisplayId(order)} | {issue}");
            }

            return changed;
        }

        private void PopulateKnownFileSizesInHistory()
        {
            foreach (var order in _orderHistory)
                PopulateKnownFileSizes(order, null);
        }

        private bool BackfillMissingFileHashesIncrementally(int maxFilesToHash)
        {
            if (maxFilesToHash <= 0 || _orderHistory.Count == 0)
                return false;

            var remainingBudget = maxFilesToHash;
            var changed = false;
            foreach (var order in _orderHistory)
            {
                if (remainingBudget <= 0)
                    break;

                changed |= PopulateKnownFileHashes(order, migrationLog: null, ref remainingBudget);
            }

            return changed;
        }

        private static long? TryGetExistingFileSize(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;

                return new FileInfo(path).Length;
            }
            catch
            {
                return null;
            }
        }

        private static bool PopulateKnownFileSizes(OrderData order, List<string>? migrationLog)
        {
            if (order == null)
                return false;

            var changed = false;
            var sourceSize = TryGetExistingFileSize(order.SourcePath);
            if (sourceSize != null && order.SourceFileSizeBytes != sourceSize)
            {
                order.SourceFileSizeBytes = sourceSize;
                changed = true;
                migrationLog?.Add($"MIGRATION | order={order.Id} | SourceFileSizeBytes={sourceSize} | path={order.SourcePath}");
            }

            var preparedSize = TryGetExistingFileSize(order.PreparedPath);
            if (preparedSize != null && order.PreparedFileSizeBytes != preparedSize)
            {
                order.PreparedFileSizeBytes = preparedSize;
                changed = true;
                migrationLog?.Add($"MIGRATION | order={order.Id} | PreparedFileSizeBytes={preparedSize} | path={order.PreparedPath}");
            }

            var printSize = TryGetExistingFileSize(order.PrintPath);
            if (printSize != null && order.PrintFileSizeBytes != printSize)
            {
                order.PrintFileSizeBytes = printSize;
                changed = true;
                migrationLog?.Add($"MIGRATION | order={order.Id} | PrintFileSizeBytes={printSize} | path={order.PrintPath}");
            }

            if (order.Items == null)
                return changed;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                var itemSourceSize = TryGetExistingFileSize(item.SourcePath);
                if (itemSourceSize != null && item.SourceFileSizeBytes != itemSourceSize)
                {
                    item.SourceFileSizeBytes = itemSourceSize;
                    changed = true;
                    migrationLog?.Add($"MIGRATION | order={order.Id} | item={item.ClientFileLabel} | SourceFileSizeBytes={itemSourceSize} | path={item.SourcePath}");
                }

                var itemPreparedSize = TryGetExistingFileSize(item.PreparedPath);
                if (itemPreparedSize != null && item.PreparedFileSizeBytes != itemPreparedSize)
                {
                    item.PreparedFileSizeBytes = itemPreparedSize;
                    changed = true;
                    migrationLog?.Add($"MIGRATION | order={order.Id} | item={item.ClientFileLabel} | PreparedFileSizeBytes={itemPreparedSize} | path={item.PreparedPath}");
                }

                var itemPrintSize = TryGetExistingFileSize(item.PrintPath);
                if (itemPrintSize != null && item.PrintFileSizeBytes != itemPrintSize)
                {
                    item.PrintFileSizeBytes = itemPrintSize;
                    changed = true;
                    migrationLog?.Add($"MIGRATION | order={order.Id} | item={item.ClientFileLabel} | PrintFileSizeBytes={itemPrintSize} | path={item.PrintPath}");
                }
            }

            return changed;
        }

        private static bool PopulateKnownFileHashes(OrderData order, List<string>? migrationLog, ref int remainingBudget)
        {
            if (order == null || remainingBudget <= 0)
                return false;

            var changed = false;
            changed |= PopulateHashForPath(order, order.SourcePath, order.SourceFileHash, stageName: "Source", migrationLog, ref remainingBudget, valueSetter: hash => order.SourceFileHash = hash);
            changed |= PopulateHashForPath(order, order.PreparedPath, order.PreparedFileHash, stageName: "Prepared", migrationLog, ref remainingBudget, valueSetter: hash => order.PreparedFileHash = hash);
            changed |= PopulateHashForPath(order, order.PrintPath, order.PrintFileHash, stageName: "Print", migrationLog, ref remainingBudget, valueSetter: hash => order.PrintFileHash = hash);

            if (order.Items == null || remainingBudget <= 0)
                return changed;

            foreach (var item in order.Items)
            {
                if (item == null || remainingBudget <= 0)
                    continue;

                changed |= PopulateHashForPath(order, item.SourcePath, item.SourceFileHash, stageName: $"item={item.ClientFileLabel} | Source", migrationLog, ref remainingBudget, valueSetter: hash => item.SourceFileHash = hash);
                changed |= PopulateHashForPath(order, item.PreparedPath, item.PreparedFileHash, stageName: $"item={item.ClientFileLabel} | Prepared", migrationLog, ref remainingBudget, valueSetter: hash => item.PreparedFileHash = hash);
                changed |= PopulateHashForPath(order, item.PrintPath, item.PrintFileHash, stageName: $"item={item.ClientFileLabel} | Print", migrationLog, ref remainingBudget, valueSetter: hash => item.PrintFileHash = hash);
            }

            return changed;
        }

        private static bool PopulateHashForPath(
            OrderData order,
            string? path,
            string? currentHash,
            string stageName,
            List<string>? migrationLog,
            ref int remainingBudget,
            Action<string> valueSetter)
        {
            if (remainingBudget <= 0)
                return false;

            if (!string.IsNullOrWhiteSpace(currentHash))
                return false;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            if (!FileHashService.TryComputeSha256(path, out var hash, out _))
                return false;

            valueSetter(hash);
            remainingBudget--;
            migrationLog?.Add($"MIGRATION | order={order.Id} | {stageName}FileHash={hash} | path={path}");
            return true;
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
            var selectedOrders = GetSelectedOrders();
            if (selectedOrders.Count == 0)
            {
                SetBottomStatus("Выберите строку заказа для запуска");
                MessageBox.Show(this, "Выберите строку заказа для запуска.", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var ordersWithoutNumber = selectedOrders
                .Where(order => string.IsNullOrWhiteSpace(order.Id))
                .ToList();
            var alreadyRunningOrders = selectedOrders
                .Where(order => _runTokensByOrder.ContainsKey(order.InternalId))
                .ToList();

            var runnableOrders = selectedOrders
                .Except(ordersWithoutNumber)
                .Except(alreadyRunningOrders)
                .ToList();

            if (runnableOrders.Count == 0)
            {
                var reasons = new List<string>();
                if (ordersWithoutNumber.Count > 0)
                    reasons.Add($"без номера: {ordersWithoutNumber.Count}");
                if (alreadyRunningOrders.Count > 0)
                    reasons.Add($"уже запущены: {alreadyRunningOrders.Count}");

                var details = reasons.Count == 0 ? "не удалось определить причину" : string.Join(", ", reasons);
                SetBottomStatus($"Нет заказов для запуска ({details})");
                MessageBox.Show(
                    this,
                    $"Нет заказов для запуска ({details}).",
                    "Запуск",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (_processor == null)
                InitializeProcessor();

            var runSessions = new List<(OrderData Order, CancellationTokenSource Cts)>();
            foreach (var order in runnableOrders)
            {
                var cts = new CancellationTokenSource();
                _runTokensByOrder[order.InternalId] = cts;
                _runProgressByOrderInternalId[order.InternalId] = 0;
                runSessions.Add((order, cts));

                AppendOrderOperationLog(
                    order,
                    OrderOperationNames.Run,
                    runnableOrders.Count > 1
                        ? "Пакетный запуск заказа из MainForm"
                        : "Запуск заказа из MainForm");

                SetOrderStatus(
                    order,
                    WorkflowStatusNames.Processing,
                    OrderStatusSourceNames.Ui,
                    runnableOrders.Count > 1 ? "Пакетный запуск из MainForm" : "Запуск из MainForm",
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

            if (ordersWithoutNumber.Count > 0 || alreadyRunningOrders.Count > 0)
            {
                var skippedReasons = new List<string>();
                if (ordersWithoutNumber.Count > 0)
                    skippedReasons.Add($"без номера: {ordersWithoutNumber.Count}");
                if (alreadyRunningOrders.Count > 0)
                    skippedReasons.Add($"уже запущены: {alreadyRunningOrders.Count}");

                SetBottomStatus($"Часть заказов пропущена ({string.Join(", ", skippedReasons)})");
            }

            var runErrors = new ConcurrentQueue<string>();
            var runTasks = runSessions.Select(async session =>
            {
                try
                {
                    await _processor!.RunAsync(session.Order, session.Cts.Token, selectedItemIds: null);
                }
                catch (OperationCanceledException)
                {
                    SetOrderStatus(
                        session.Order,
                        WorkflowStatusNames.Cancelled,
                        OrderStatusSourceNames.Ui,
                        "Остановлено пользователем",
                        persistHistory: false,
                        rebuildGrid: false);
                }
                catch (Exception ex)
                {
                    SetOrderStatus(
                        session.Order,
                        WorkflowStatusNames.Error,
                        OrderStatusSourceNames.Ui,
                        ex.Message,
                        persistHistory: false,
                        rebuildGrid: false);
                    runErrors.Enqueue($"{GetOrderDisplayId(session.Order)}: {ex.Message}");
                }
                finally
                {
                    _runTokensByOrder.Remove(session.Order.InternalId);
                    _runProgressByOrderInternalId.Remove(session.Order.InternalId);
                    UpdateTrayProgressIndicator();
                }
            }).ToList();

            await Task.WhenAll(runTasks);

            SaveHistory();
            RebuildOrdersGrid();
            UpdateActionButtonsState();

            if (!runErrors.IsEmpty)
            {
                var errors = runErrors.ToArray();
                var errorsPreview = string.Join(Environment.NewLine, errors.Take(5));
                if (errors.Length > 5)
                    errorsPreview += $"{Environment.NewLine}... ещё: {errors.Length - 5}";

                SetBottomStatus($"Ошибок запуска: {errors.Length}");
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
        }

        private void StopSelectedOrder()
        {
            var order = GetSelectedOrder();
            if (order == null)
            {
                SetBottomStatus("Выберите заказ для остановки");
                MessageBox.Show(this, "Выберите заказ для остановки.", "Остановка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_runTokensByOrder.TryGetValue(order.InternalId, out var cts))
            {
                SetBottomStatus($"Заказ {GetOrderDisplayId(order)} сейчас не выполняется");
                MessageBox.Show(this, $"Заказ {GetOrderDisplayId(order)} сейчас не выполняется.", "Остановка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            cts.Cancel();
            _runTokensByOrder.Remove(order.InternalId);
            _runProgressByOrderInternalId.Remove(order.InternalId);
            UpdateTrayProgressIndicator();
            AppendOrderOperationLog(order, OrderOperationNames.Stop, "Остановлено пользователем");
            SetOrderStatus(order, WorkflowStatusNames.Cancelled, OrderStatusSourceNames.Ui, "Остановлено пользователем", persistHistory: true, rebuildGrid: true);
            UpdateActionButtonsState();
            SetBottomStatus($"Остановлен заказ {GetOrderDisplayId(order)}");
        }

        private void RemoveSelectedOrder()
        {
            var selectedOrderItems = GetSelectedOrderItems();
            var hasSelectedOrderContainers = HasSelectedOrderContainerRow();
            if (!hasSelectedOrderContainers && selectedOrderItems.Count > 0)
            {
                RemoveSelectedOrderItems(selectedOrderItems);
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
            var removedOrdersCount = 0;
            var failedOrders = new List<string>();

            foreach (var order in selectedOrders)
            {
                try
                {
                    if (removeFilesFromDisk)
                    {
                        var orderFolder = string.IsNullOrWhiteSpace(order.FolderName)
                            ? string.Empty
                            : Path.Combine(_ordersRootPath, order.FolderName);

                        if (!string.IsNullOrWhiteSpace(orderFolder) && Directory.Exists(orderFolder))
                            Directory.Delete(orderFolder, true);
                        else
                            DeleteOrderFiles(order);
                    }

                    if (_runTokensByOrder.TryGetValue(order.InternalId, out var cts))
                    {
                        cts.Cancel();
                        _runTokensByOrder.Remove(order.InternalId);
                        _runProgressByOrderInternalId.Remove(order.InternalId);
                    }

                    _expandedOrderIds.Remove(order.InternalId);
                    AppendOrderOperationLog(
                        order,
                        OrderOperationNames.Delete,
                        removeFilesFromDisk
                            ? "Удален из списка и с диска"
                            : "Удален из списка");
                    _orderHistory.Remove(order);
                    removedOrdersCount++;
                }
                catch (Exception ex)
                {
                    failedOrders.Add($"{GetOrderDisplayId(order)}: {ex.Message}");
                }
            }

            UpdateTrayProgressIndicator();

            if (removedOrdersCount > 0)
            {
                SaveHistory();
                RebuildOrdersGrid();
                UpdateActionButtonsState();
                SetBottomStatus(isBatchDelete
                    ? $"Удалено заказов: {removedOrdersCount}"
                    : $"Заказ {GetOrderDisplayId(firstOrder)} удален");
            }
            else
            {
                SetBottomStatus("Удаление не выполнено");
            }

            if (failedOrders.Count == 0)
                return;

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

        private void RemoveSelectedOrderItems(List<(OrderData Order, OrderFileItem Item)> selectedOrderItems)
        {
            if (selectedOrderItems == null || selectedOrderItems.Count == 0)
            {
                SetBottomStatus("Выберите файл для удаления");
                return;
            }

            var isBatchDelete = selectedOrderItems.Count > 1;
            var firstSelection = selectedOrderItems[0];
            var firstItemName = BuildOrderItemDisplayName(firstSelection.Item);
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
            var removedItemsCount = 0;
            var failedItems = new List<string>();
            var affectedOrders = new Dictionary<string, (OrderData Order, bool WasMultiBefore)>(StringComparer.Ordinal);

            foreach (var (order, item) in selectedOrderItems)
            {
                if (order == null || item == null)
                    continue;

                if (!affectedOrders.ContainsKey(order.InternalId))
                    affectedOrders[order.InternalId] = (order, OrderTopologyService.IsMultiOrder(order));

                var itemName = BuildOrderItemDisplayName(item);
                try
                {
                    if (removeFilesFromDisk)
                        DeleteOrderItemFiles(item);

                    var removed = order.Items?.RemoveAll(x => x != null && string.Equals(x.ItemId, item.ItemId, StringComparison.Ordinal)) > 0;
                    if (!removed)
                    {
                        failedItems.Add($"{GetOrderDisplayId(order)} / {itemName}: файл не найден");
                        continue;
                    }

                    ReindexOrderItems(order);
                    AppendOrderOperationLog(
                        order,
                        OrderOperationNames.RemoveItem,
                        removeFilesFromDisk
                            ? $"Удален файл: {itemName} (с диска и из группы)"
                            : $"Удален файл: {itemName} (из группы)");
                    removedItemsCount++;
                }
                catch (Exception ex)
                {
                    failedItems.Add($"{GetOrderDisplayId(order)} / {itemName}: {ex.Message}");
                }
            }

            foreach (var (_, payload) in affectedOrders)
            {
                NormalizeOrderTopologyAfterItemMutation(
                    payload.Order,
                    payload.WasMultiBefore,
                    "remove-item-row");
            }

            if (removedItemsCount > 0)
            {
                SaveHistory();
                RebuildOrdersGrid();
                UpdateActionButtonsState();
                SetBottomStatus(isBatchDelete
                    ? $"Удалено файлов: {removedItemsCount}"
                    : $"Файл {firstItemName} удален");
            }
            else
            {
                SetBottomStatus("Удаление файла не выполнено");
            }

            if (failedItems.Count == 0)
                return;

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

        private void DeleteOrderFiles(OrderData order)
        {
            foreach (var path in GetOrderAllKnownPaths(order))
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Не удалось удалить файл: {path}", ex);
                }
            }
        }

        private void DeleteOrderItemFiles(OrderFileItem item)
        {
            foreach (var path in GetOrderItemAllKnownPaths(item))
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Не удалось удалить файл: {path}", ex);
                }
            }
        }

        private static IEnumerable<string?> GetOrderAllKnownPaths(OrderData order)
        {
            if (order == null)
                yield break;

            yield return order.SourcePath;
            yield return order.PreparedPath;
            yield return order.PrintPath;

            if (order.Items == null)
                yield break;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                yield return item.SourcePath;
                yield return item.PreparedPath;
                yield return item.PrintPath;
            }
        }

        private static IEnumerable<string?> GetOrderItemAllKnownPaths(OrderFileItem item)
        {
            if (item == null)
                yield break;

            yield return item.SourcePath;
            yield return item.PreparedPath;
            yield return item.PrintPath;

            if (item.TechnicalFiles == null || item.TechnicalFiles.Count == 0)
                yield break;

            foreach (var technicalFilePath in item.TechnicalFiles)
                yield return technicalFilePath;
        }

        private static string BuildOrderItemDisplayName(OrderFileItem item)
        {
            if (item == null)
                return "файл";

            if (!string.IsNullOrWhiteSpace(item.ClientFileLabel))
                return item.ClientFileLabel.Trim();

            if (!string.IsNullOrWhiteSpace(item.ItemId))
                return item.ItemId;

            return "файл";
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
            folderPath = string.Empty;
            reason = "Папка не определена";

            if (order == null)
                return false;

            if (!OrderTopologyService.IsMultiOrder(order))
            {
                folderPath = GetPreferredOrderFolder(order);
                return !string.IsNullOrWhiteSpace(folderPath);
            }

            if (!TryGetCommonFolderForGroupOrder(order, out folderPath, out reason))
                return false;

            return !string.IsNullOrWhiteSpace(folderPath);
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

        private bool TryGetCommonFolderForGroupOrder(OrderData order, out string folderPath, out string reason)
        {
            folderPath = string.Empty;
            reason = "Папка не определена";
            if (order == null)
                return false;

            if (!string.IsNullOrWhiteSpace(order.FolderName))
            {
                folderPath = Path.Combine(_ordersRootPath, order.FolderName);
                return true;
            }

            var directories = GetGroupDirectoryCandidates(order)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (directories.Count == 0)
                return false;

            var distinctRoots = directories
                .Select(GetPathRootSafe)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctRoots.Count > 1)
            {
                reason = "Пути не совпадают";
                return false;
            }

            var commonDirectory = FindCommonDirectory(directories);
            if (string.IsNullOrWhiteSpace(commonDirectory))
                return false;

            folderPath = commonDirectory;
            return true;
        }

        private static IEnumerable<string> GetGroupDirectoryCandidates(OrderData order)
        {
            if (order?.Items != null)
            {
                foreach (var item in order.Items.Where(x => x != null))
                {
                    var itemPaths = new[] { item.SourcePath, item.PreparedPath, item.PrintPath };
                    foreach (var rawPath in itemPaths)
                    {
                        var cleanPath = CleanPath(rawPath);
                        if (string.IsNullOrWhiteSpace(cleanPath))
                            continue;

                        var candidateDirectory = Path.HasExtension(cleanPath)
                            ? Path.GetDirectoryName(cleanPath)
                            : cleanPath;
                        if (!string.IsNullOrWhiteSpace(candidateDirectory))
                            yield return NormalizePath(candidateDirectory);
                    }
                }
            }

            var orderPaths = new[] { order?.SourcePath, order?.PreparedPath, order?.PrintPath };
            foreach (var rawPath in orderPaths)
            {
                var cleanPath = CleanPath(rawPath);
                if (string.IsNullOrWhiteSpace(cleanPath))
                    continue;

                var candidateDirectory = Path.HasExtension(cleanPath)
                    ? Path.GetDirectoryName(cleanPath)
                    : cleanPath;
                if (!string.IsNullOrWhiteSpace(candidateDirectory))
                    yield return NormalizePath(candidateDirectory);
            }
        }

        private static string FindCommonDirectory(IReadOnlyList<string> directories)
        {
            if (directories == null || directories.Count == 0)
                return string.Empty;

            var commonPath = directories[0];
            if (string.IsNullOrWhiteSpace(commonPath))
                return string.Empty;

            for (var i = 1; i < directories.Count; i++)
            {
                var candidatePath = directories[i];
                if (string.IsNullOrWhiteSpace(candidatePath))
                    continue;

                while (!IsDirectoryPrefix(candidatePath, commonPath))
                {
                    var parentPath = Path.GetDirectoryName(commonPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrWhiteSpace(parentPath))
                        return string.Empty;

                    commonPath = parentPath;
                }
            }

            return commonPath;
        }

        private static bool IsDirectoryPrefix(string path, string prefix)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(prefix))
                return false;

            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            if (path.Length == prefix.Length)
                return true;

            var boundary = path[prefix.Length];
            return boundary == Path.DirectorySeparatorChar || boundary == Path.AltDirectorySeparatorChar;
        }

        private static string GetPathRootSafe(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetPathRoot(path) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetPreferredOrderFolder(OrderData order)
        {
            if (!string.IsNullOrWhiteSpace(order.FolderName))
                return Path.Combine(_ordersRootPath, order.FolderName);

            var knownPath = FirstNotEmpty(
                order.PrintPath,
                order.PreparedPath,
                order.SourcePath,
                order.Items?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.PrintPath))?.PrintPath,
                order.Items?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.PreparedPath))?.PreparedPath,
                order.Items?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.SourcePath))?.SourcePath);

            if (!string.IsNullOrWhiteSpace(knownPath))
                return Path.GetDirectoryName(knownPath) ?? _ordersRootPath;

            return !string.IsNullOrWhiteSpace(_tempRootPath) ? _tempRootPath : _ordersRootPath;
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
            var normalizedSource = string.IsNullOrWhiteSpace(source)
                ? OrderStatusSourceNames.Ui
                : source.Trim();
            var normalizedReason = NormalizeFileSyncReason(normalizedSource, reason);
            var oldStatus = order.Status ?? string.Empty;
            if (string.Equals(oldStatus, status, StringComparison.Ordinal)
                && string.Equals(order.LastStatusSource ?? string.Empty, normalizedSource, StringComparison.Ordinal)
                && string.Equals(order.LastStatusReason ?? string.Empty, normalizedReason ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            order.Status = status;
            order.LastStatusSource = normalizedSource;
            order.LastStatusReason = normalizedReason ?? string.Empty;
            order.LastStatusAt = DateTime.Now;
            AppendOrderStatusLog(order, oldStatus, status, normalizedSource, normalizedReason ?? string.Empty);
            TryAppendRepositoryEvent(
                order,
                itemId: string.Empty,
                eventType: "status-change",
                eventSource: normalizedSource,
                payload: new
                {
                    old_status = oldStatus,
                    new_status = status ?? string.Empty,
                    reason = normalizedReason ?? string.Empty,
                    status_at = order.LastStatusAt
                });

            if (persistHistory)
                SaveHistory();
            if (rebuildGrid)
                RebuildOrdersGrid();

            UpdateTrayErrorIndicator();

            return true;
        }

        private static string NormalizeFileSyncReason(string? source, string? reason)
        {
            if (!string.Equals(source, OrderStatusSourceNames.FileSync, StringComparison.OrdinalIgnoreCase))
                return reason ?? string.Empty;

            return (reason ?? string.Empty).Trim() switch
            {
                "stage-1" => "Найден исходный файл",
                "stage-2" => "Найден файл подготовки",
                "stage-3" => "Найден печатный файл",
                _ => reason ?? string.Empty
            };
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
