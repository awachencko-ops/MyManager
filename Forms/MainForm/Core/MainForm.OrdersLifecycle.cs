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

namespace MyManager
{
    public partial class MainForm
    {
        private void RefreshQueuePresentation()
        {
            treeView1.Invalidate();

            var userNode = FindUserNode(_currentUserName);
            if (userNode == null)
                return;

            var preferredStatus = GetSelectedQueueStatusName();
            FillQueueCombo(preferredStatus);
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
            }

            if (NormalizeOrderTopologyInHistory(logIssues: true))
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

            HandleOrdersGridChanged();
        }

        private void AddOrderRowsToGrid(OrderData order)
        {
            if (order == null)
                return;

            var normalizedStatus = NormalizeStatus(order.Status) ?? (order.Status ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedStatus))
                normalizedStatus = "Обрабатывается";

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

        private static string ResolveSingleOrderDisplayPath(OrderData order, int stage)
        {
            var orderPath = GetOrderStagePath(order, stage);
            var primaryItem = GetPrimaryItem(order);
            var itemPath = primaryItem == null ? string.Empty : GetItemStagePath(primaryItem, stage);

            if (HasExistingFile(itemPath))
                return itemPath;

            if (HasExistingFile(orderPath))
                return orderPath;

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
            if (dgvJobs.CurrentRow == null)
                return null;

            var rowTag = dgvJobs.CurrentRow.Tag?.ToString();
            var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return null;

            return FindOrderByInternalId(orderInternalId);
        }

        private async Task RunSelectedOrderAsync()
        {
            var order = GetSelectedOrder();
            if (order == null)
            {
                SetBottomStatus("Выберите строку заказа для запуска");
                MessageBox.Show(this, "Выберите строку заказа для запуска.", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(order.Id))
            {
                SetBottomStatus("У заказа не указан номер");
                MessageBox.Show(this, "У заказа не указан № заказа. Перед запуском заполните карточку заказа.", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_runTokensByOrder.ContainsKey(order.InternalId))
            {
                SetBottomStatus($"Заказ {GetOrderDisplayId(order)} уже запущен");
                MessageBox.Show(this, $"Заказ {GetOrderDisplayId(order)} уже запущен.", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_processor == null)
                InitializeProcessor();

            var cts = new CancellationTokenSource();
            _runTokensByOrder[order.InternalId] = cts;
            _runProgressByOrderInternalId[order.InternalId] = 0;
            UpdateTrayProgressIndicator();

            SetOrderStatus(order, "Обрабатывается", "ui", "Запуск из MainForm", persistHistory: true, rebuildGrid: true);
            SetBottomStatus($"Запущен заказ {GetOrderDisplayId(order)}");

            try
            {
                await _processor!.RunAsync(order, cts.Token, selectedItemIds: null);
            }
            catch (OperationCanceledException)
            {
                SetOrderStatus(order, "Отменено", "ui", "Остановлено пользователем", persistHistory: true, rebuildGrid: true);
                SetBottomStatus($"Заказ {GetOrderDisplayId(order)} остановлен");
            }
            catch (Exception ex)
            {
                SetOrderStatus(order, "Ошибка", "ui", ex.Message, persistHistory: true, rebuildGrid: true);
                SetBottomStatus($"Ошибка запуска заказа {GetOrderDisplayId(order)}: {ex.Message}");
                MessageBox.Show(this, $"Не удалось запустить заказ: {ex.Message}", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _runTokensByOrder.Remove(order.InternalId);
                _runProgressByOrderInternalId.Remove(order.InternalId);
                UpdateTrayProgressIndicator();
                SaveHistory();
                RebuildOrdersGrid();
                UpdateActionButtonsState();
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
            SetOrderStatus(order, "Отменено", "ui", "Остановлено пользователем", persistHistory: true, rebuildGrid: true);
            UpdateActionButtonsState();
            SetBottomStatus($"Остановлен заказ {GetOrderDisplayId(order)}");
        }

        private void RemoveSelectedOrder()
        {
            var order = GetSelectedOrder();
            if (order == null)
            {
                SetBottomStatus("Выберите заказ для удаления");
                MessageBox.Show(this, "Выберите заказ для удаления.", "Удаление", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var orderFolder = string.IsNullOrWhiteSpace(order.FolderName)
                ? string.Empty
                : Path.Combine(_ordersRootPath, order.FolderName);

            var decision = MessageBox.Show(
                this,
                $"Заказ №{GetOrderDisplayId(order)}\n\n" +
                "Удалить папку заказа с диска?\n\n" +
                "[Да] — удалить с диска и из списка.\n" +
                "[Нет] — только удалить из списка.\n" +
                "[Отмена] — ничего не менять.",
                "Удаление заказа",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (decision == DialogResult.Cancel)
            {
                SetBottomStatus("Удаление отменено");
                return;
            }

            if (decision == DialogResult.Yes)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(orderFolder) && Directory.Exists(orderFolder))
                        Directory.Delete(orderFolder, true);
                    else
                        DeleteOrderFiles(order);
                }
                catch (Exception ex)
                {
                    SetBottomStatus($"Не удалось удалить файлы заказа: {ex.Message}");
                    MessageBox.Show(this, $"Не удалось удалить файлы заказа: {ex.Message}", "Удаление заказа", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (_runTokensByOrder.TryGetValue(order.InternalId, out var cts))
            {
                cts.Cancel();
                _runTokensByOrder.Remove(order.InternalId);
                _runProgressByOrderInternalId.Remove(order.InternalId);
                UpdateTrayProgressIndicator();
            }

            _expandedOrderIds.Remove(order.InternalId);
            _orderHistory.Remove(order);
            SaveHistory();
            RebuildOrdersGrid();
            UpdateActionButtonsState();
            SetBottomStatus($"Заказ {GetOrderDisplayId(order)} удален");
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
            if (stage is >= 1 and <= 3)
                targetPath = GetStageFolder(order, stage);
            else
                targetPath = GetPreferredOrderFolder(order);

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