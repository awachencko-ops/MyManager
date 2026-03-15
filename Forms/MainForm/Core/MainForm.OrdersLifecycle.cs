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

        private void LoadHistory()
        {
            _orderHistory.Clear();
            _jsonHistoryFile = StoragePaths.ResolveExistingFilePath(_jsonHistoryFile, "history.json");
            var usersNormalized = false;

            if (File.Exists(_jsonHistoryFile))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<OrderData>>(File.ReadAllText(_jsonHistoryFile));
                    if (parsed != null)
                        _orderHistory.AddRange(parsed);
                }
                catch
                {
                    _orderHistory.Clear();
                }
            }

            foreach (var order in _orderHistory)
            {
                if (string.IsNullOrWhiteSpace(order.InternalId))
                    order.InternalId = Guid.NewGuid().ToString("N");
                if (order.ArrivalDate == default)
                    order.ArrivalDate = order.OrderDate != default ? order.OrderDate : DateTime.Now;

                var normalizedUserName = NormalizeOrderUserName(order.UserName);
                if (!string.Equals(order.UserName, normalizedUserName, StringComparison.Ordinal))
                {
                    order.UserName = normalizedUserName;
                    usersNormalized = true;
                }
            }

            if (NormalizeOrderTopologyInHistory(logIssues: true) || usersNormalized)
                SaveHistory();
        }

        private void SaveHistory()
        {
            NormalizeOrderTopologyInHistory(logIssues: false);

            var targetPath = StoragePaths.ResolveFilePath(_jsonHistoryFile, "history.json");
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(
                targetPath,
                JsonSerializer.Serialize(_orderHistory, new JsonSerializerOptions { WriteIndented = true }));
            _jsonHistoryFile = targetPath;
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

            var normalizedStatus = NormalizeStatus(order.Status) ?? (order.Status ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedStatus))
                normalizedStatus = WorkflowStatusNames.Processing;

            var sourcePath = ResolveSingleOrderDisplayPath(order, 1);
            var preparedPath = ResolveSingleOrderDisplayPath(order, 2);
            var printPath = ResolveSingleOrderDisplayPath(order, 3);
            var pitStopAction = ResolveSingleOrderDisplayAction(order, x => x.PitStopAction, order.PitStopAction);
            var imposingAction = ResolveSingleOrderDisplayAction(order, x => x.ImposingAction, order.ImposingAction);

            var orderRowIndex = dgvJobs.Rows.Add(
                normalizedStatus,
                GetOrderDisplayId(order),
                GetFileName(sourcePath),
                GetFileName(preparedPath),
                pitStopAction,
                imposingAction,
                GetFileName(printPath),
                FormatDate(order.OrderDate),
                FormatDate(order.ArrivalDate));

            dgvJobs.Rows[orderRowIndex].Tag = $"order|{order.InternalId}";
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
            var normalizedOrderAction = NormalizeAction(orderAction);
            if (!string.Equals(normalizedOrderAction, "-", StringComparison.Ordinal))
                return normalizedOrderAction;

            var primaryItem = GetPrimaryItem(order);
            if (primaryItem == null)
                return normalizedOrderAction;

            return NormalizeAction(selector(primaryItem));
        }

        private static OrderFileItem? GetPrimaryItem(OrderData order)
        {
            if (order?.Items == null || order.Items.Count == 0)
                return null;

            return order.Items
                .Where(x => x != null)
                .OrderBy(x => x.SequenceNo)
                .FirstOrDefault();
        }

        private bool OrderMatchesSearch(OrderData order, string searchText)
        {
            if (order == null)
                return false;

            var query = searchText.Trim();
            if (string.IsNullOrWhiteSpace(query))
                return true;

            static bool Contains(string source, string queryValue)
                => !string.IsNullOrWhiteSpace(source) &&
                   source.IndexOf(queryValue, StringComparison.OrdinalIgnoreCase) >= 0;

            if (Contains(order.Id, query)
                || Contains(Path.GetFileName(order.SourcePath), query)
                || Contains(Path.GetFileName(order.PreparedPath), query)
                || Contains(Path.GetFileName(order.PrintPath), query))
            {
                return true;
            }

            if (order.Items == null || order.Items.Count == 0)
                return false;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                if (Contains(item.ClientFileLabel, query)
                    || Contains(Path.GetFileName(item.SourcePath), query)
                    || Contains(Path.GetFileName(item.PreparedPath), query)
                    || Contains(Path.GetFileName(item.PrintPath), query))
                {
                    return true;
                }
            }

            return false;
        }

        private void TryRestoreSelectedRowByTag(string selectedTag)
        {
            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (string.Equals(row.Tag?.ToString(), selectedTag, StringComparison.Ordinal))
                {
                    dgvJobs.CurrentCell = row.Cells[colStatus.Index];
                    return;
                }
            }
        }

        private OrderData? FindOrderByInternalId(string? internalId)
        {
            if (string.IsNullOrWhiteSpace(internalId))
                return null;

            return _orderHistory.FirstOrDefault(x => string.Equals(x.InternalId, internalId, StringComparison.Ordinal));
        }

        private static bool IsOrderTag(string? tag)
        {
            return !string.IsNullOrWhiteSpace(tag) && tag.StartsWith("order|", StringComparison.Ordinal);
        }

        private static bool IsItemTag(string? tag)
        {
            return !string.IsNullOrWhiteSpace(tag) && tag.StartsWith("item|", StringComparison.Ordinal);
        }

        private static string? ExtractOrderInternalIdFromTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var parts = tag.Split('|');
            if (parts.Length < 2)
                return null;

            return parts[1];
        }

        private static string GetOrderDisplayId(OrderData order)
        {
            return string.IsNullOrWhiteSpace(order.Id) ? "—" : order.Id.Trim();
        }

        private static string GetFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "...";

            var normalizedPath = path.Trim();
            if (Directory.Exists(normalizedPath))
                return "...";

            var fileName = Path.GetFileName(normalizedPath);
            return string.IsNullOrWhiteSpace(fileName) ? "..." : fileName;
        }

        private static string FormatDate(DateTime value)
        {
            if (value == default)
                return string.Empty;

            return value.ToString("dd.MM.yyyy");
        }

        private static string NormalizeAction(string? action)
        {
            return string.IsNullOrWhiteSpace(action) ? "-" : action.Trim();
        }

        private OrderData? GetSelectedOrder()
        {
            var selectedRow = dgvJobs.CurrentRow;
            if (selectedRow == null || selectedRow.IsNewRow)
            {
                selectedRow = dgvJobs.SelectedRows
                    .Cast<DataGridViewRow>()
                    .Where(row => !row.IsNewRow)
                    .OrderBy(row => row.Index)
                    .FirstOrDefault();
            }

            if (selectedRow == null)
                return null;

            var rowTag = selectedRow.Tag?.ToString();
            var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return null;

            return FindOrderByInternalId(orderInternalId);
        }

        private List<OrderData> GetSelectedOrders()
        {
            var selectedOrders = new List<OrderData>();
            var uniqueOrderIds = new HashSet<string>(StringComparer.Ordinal);

            var selectedRows = dgvJobs.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .OrderBy(row => row.Index);

            foreach (var row in selectedRows)
            {
                var orderInternalId = ExtractOrderInternalIdFromTag(row.Tag?.ToString());
                if (string.IsNullOrWhiteSpace(orderInternalId) || !uniqueOrderIds.Add(orderInternalId))
                    continue;

                var order = FindOrderByInternalId(orderInternalId);
                if (order != null)
                    selectedOrders.Add(order);
            }

            if (selectedOrders.Count > 0)
                return selectedOrders;

            var singleOrder = GetSelectedOrder();
            if (singleOrder != null && uniqueOrderIds.Add(singleOrder.InternalId))
                selectedOrders.Add(singleOrder);

            return selectedOrders;
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

                SetOrderStatus(
                    order,
                    WorkflowStatusNames.Processing,
                    "ui",
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
                        "ui",
                        "Остановлено пользователем",
                        persistHistory: false,
                        rebuildGrid: false);
                }
                catch (Exception ex)
                {
                    SetOrderStatus(
                        session.Order,
                        WorkflowStatusNames.Error,
                        "ui",
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
            SetOrderStatus(order, WorkflowStatusNames.Cancelled, "ui", "Остановлено пользователем", persistHistory: true, rebuildGrid: true);
            UpdateActionButtonsState();
            SetBottomStatus($"Остановлен заказ {GetOrderDisplayId(order)}");
        }

        private void RemoveSelectedOrder()
        {
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
            SetBottomStatus($"Открыта папка: {targetPath}");
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
            var oldStatus = order.Status ?? string.Empty;
            if (string.Equals(oldStatus, status, StringComparison.Ordinal)
                && string.Equals(order.LastStatusSource ?? string.Empty, source ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(order.LastStatusReason ?? string.Empty, reason ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            order.Status = status;
            order.LastStatusSource = source ?? string.Empty;
            order.LastStatusReason = reason ?? string.Empty;
            order.LastStatusAt = DateTime.Now;
            AppendOrderStatusLog(order, oldStatus, status, source ?? string.Empty, reason ?? string.Empty);

            if (persistHistory)
                SaveHistory();
            if (rebuildGrid)
                RebuildOrdersGrid();

            UpdateTrayErrorIndicator();

            return true;
        }

    }
}
