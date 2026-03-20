using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Svg;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private void InitializeTrayIndicators()
        {
            toolStatus.Spring = true;
            toolStatus.TextAlign = ContentAlignment.MiddleLeft;

            toolProgress.Visible = false;
            toolProgress.Style = ProgressBarStyle.Continuous;
            toolProgress.Minimum = 0;
            toolProgress.Maximum = 100;
            toolProgress.Value = 0;

            toolAlerts.IsLink = true;
            toolAlerts.LinkBehavior = LinkBehavior.HoverUnderline;
            toolAlerts.Click += ToolAlerts_Click;

            _trayIndicatorsTimer ??= new System.Windows.Forms.Timer
            {
                Interval = TrayIndicatorsRefreshIntervalMs
            };
            _trayIndicatorsTimer.Tick -= TrayIndicatorsTimer_Tick;
            _trayIndicatorsTimer.Tick += TrayIndicatorsTimer_Tick;
            _trayIndicatorsTimer.Start();

            _acknowledgedErrorCount = CountOrdersWithErrors();
            RefreshTrayIndicators();
        }

        private void ToolAlerts_Click(object? sender, EventArgs e)
        {
            AcknowledgeErrorNotifications();
            OpenLogForSelectionOrManager();
        }

        private void TrayIndicatorsTimer_Tick(object? sender, EventArgs e)
        {
            RefreshArchivedStatuses();
            if (BackfillMissingFileHashesIncrementally(maxFilesToHash: 2))
                SaveHistory();
            RefreshUsersDirectoryIfNeeded();
            UpdateTrayConnectionIndicator();
            UpdateTrayDiskIndicator();
        }

        private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
        {
            _ordersViewWarmupCoordinator?.Dispose();
            _ordersViewWarmupCoordinator = null;

            DisposeStatusCellVisuals();

            _printTileOrderFont?.Dispose();
            _printTileOrderFont = null;
            _printTilesContextMenu.Dispose();
            _gridHoverActivateTimer?.Stop();
            if (_gridHoverActivateTimer != null)
            {
                _gridHoverActivateTimer.Tick -= GridHoverActivateTimer_Tick;
                _gridHoverActivateTimer.Dispose();
                _gridHoverActivateTimer = null;
            }
            _tileHoverActivateTimer?.Stop();
            if (_tileHoverActivateTimer != null)
            {
                _tileHoverActivateTimer.Tick -= TileHoverActivateTimer_Tick;
                _tileHoverActivateTimer.Dispose();
                _tileHoverActivateTimer = null;
            }

            if (_trayIndicatorsTimer == null)
                return;

            _trayIndicatorsTimer.Stop();
            _trayIndicatorsTimer.Tick -= TrayIndicatorsTimer_Tick;
            _trayIndicatorsTimer.Dispose();
            _trayIndicatorsTimer = null;
        }

        private void RefreshTrayIndicators()
        {
            UpdateTrayStatsIndicator();
            UpdateTrayConnectionIndicator();
            UpdateTrayDiskIndicator();
            UpdateTrayErrorIndicator();
            UpdateTrayProgressIndicator();
        }

        private void UpdateTrayStatsIndicator()
        {
            if (toolStats.IsDisposed || dgvJobs.IsDisposed)
                return;

            if (_ordersViewMode == OrdersViewMode.Tiles && !_lvPrintTiles.IsDisposed)
            {
                toolStats.Text = $"Плиток: {_lvPrintTiles.Items.Count} | Выделено: {_lvPrintTiles.SelectedItems.Count}";
                return;
            }

            var visibleOrders = 0;
            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow || !row.Visible)
                    continue;

                visibleOrders++;
            }

            var selectedOrderIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataGridViewRow row in dgvJobs.SelectedRows)
            {
                if (row.IsNewRow)
                    continue;

                var orderInternalId = ExtractOrderInternalIdFromTag(row.Tag?.ToString());
                if (!string.IsNullOrWhiteSpace(orderInternalId))
                    selectedOrderIds.Add(orderInternalId);
            }

            toolStats.Text = $"Строк: {visibleOrders} | Выделено: {selectedOrderIds.Count}";
        }

        private void UpdateTrayConnectionIndicator()
        {
            if (toolConnection.IsDisposed)
                return;

            var isConnected = CanAccessPath(_ordersRootPath);
            var dependencyHealthLevel = GetWorstDependencyHealthLevel();
            if (!isConnected)
            {
                toolConnection.Text = "● Сервер: автономно";
                toolConnection.ForeColor = Color.Firebrick;
            }
            else if (dependencyHealthLevel == DependencyHealthLevel.Unavailable)
            {
                toolConnection.Text = "● Сервер: hotfolder недоступен";
                toolConnection.ForeColor = Color.Firebrick;
            }
            else if (dependencyHealthLevel == DependencyHealthLevel.Degraded)
            {
                toolConnection.Text = "● Сервер: подключен (деградация)";
                toolConnection.ForeColor = Color.DarkOrange;
            }
            else
            {
                toolConnection.Text = "● Сервер: подключен";
                toolConnection.ForeColor = Color.SeaGreen;
            }

            var connectionStatusText = isConnected
                ? "Рабочая папка заказов доступна."
                : "Рабочая папка заказов недоступна.";
            var dependencyHealthSummary = BuildDependencyHealthSummary();
            toolConnection.ToolTipText = string.IsNullOrWhiteSpace(dependencyHealthSummary)
                ? $"{connectionStatusText}\n{_usersDirectoryStatusText}"
                : $"{connectionStatusText}\n{dependencyHealthSummary}\n{_usersDirectoryStatusText}";
            UpdateServerHeaderConnectionState(isConnected, dependencyHealthLevel);
        }

        private void ApplyProcessorDependencyHealthSignal(DependencyHealthSignal signal)
        {
            if (signal == null || string.IsNullOrWhiteSpace(signal.DependencyName))
                return;

            _dependencyHealthByName[signal.DependencyName] = signal.Level;
            UpdateTrayConnectionIndicator();

            if (signal.Level == DependencyHealthLevel.Unavailable)
            {
                SetBottomStatus($"Зависимость недоступна: {signal.DependencyName}");
            }
        }

        private DependencyHealthLevel GetWorstDependencyHealthLevel()
        {
            if (_dependencyHealthByName.Count == 0)
                return DependencyHealthLevel.Healthy;

            var level = DependencyHealthLevel.Healthy;
            foreach (var dependencyLevel in _dependencyHealthByName.Values)
            {
                if (dependencyLevel > level)
                    level = dependencyLevel;
            }

            return level;
        }

        private string BuildDependencyHealthSummary()
        {
            if (_dependencyHealthByName.Count == 0)
                return string.Empty;

            var parts = new List<string>();
            foreach (var dependency in _dependencyHealthByName.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var levelText = dependency.Value switch
                {
                    DependencyHealthLevel.Unavailable => "недоступно",
                    DependencyHealthLevel.Degraded => "деградация",
                    _ => "ok"
                };

                parts.Add($"{dependency.Key}: {levelText}");
            }

            return "Hotfolder health: " + string.Join(", ", parts);
        }

        private void UpdateTrayDiskIndicator()
        {
            if (toolDiskFree.IsDisposed)
                return;

            if (!TryGetProbeDrive(out var drive))
            {
                toolDiskFree.Text = "Свободно: н/д";
                toolDiskFree.ForeColor = Color.Gray;
                toolDiskFree.ToolTipText = "Не удалось определить диск.";
                return;
            }

            long freeBytes;
            try
            {
                freeBytes = drive.AvailableFreeSpace;
            }
            catch
            {
                toolDiskFree.Text = $"Свободно {drive.Name} н/д";
                toolDiskFree.ForeColor = Color.Gray;
                toolDiskFree.ToolTipText = "Нет доступа к данным о диске.";
                return;
            }

            toolDiskFree.Text = $"Свободно {drive.Name} {FormatStorageSize(freeBytes)}";
            toolDiskFree.ToolTipText = $"Свободное место на диске {drive.Name}";

            if (freeBytes <= DiskCriticalThresholdBytes)
                toolDiskFree.ForeColor = Color.Firebrick;
            else if (freeBytes <= DiskWarningThresholdBytes)
                toolDiskFree.ForeColor = Color.DarkOrange;
            else
                toolDiskFree.ForeColor = Color.SeaGreen;
        }

        private void UpdateTrayErrorIndicator()
        {
            if (toolAlerts.IsDisposed)
                return;

            var errorCount = CountOrdersWithErrors();
            if (errorCount <= 0)
                _acknowledgedErrorCount = 0;
            else if (_acknowledgedErrorCount > errorCount)
                _acknowledgedErrorCount = errorCount;

            var hasUnseenErrors = errorCount > _acknowledgedErrorCount;
            toolAlerts.Text = $"⚠ {errorCount}";
            toolAlerts.ToolTipText = errorCount == 0
                ? "Ошибок нет. Нажмите, чтобы открыть лог."
                : hasUnseenErrors
                    ? $"Есть новые ошибки: {errorCount}. Нажмите, чтобы открыть лог."
                    : $"Ошибок: {errorCount}. Нажмите, чтобы открыть лог.";

            if (errorCount == 0)
                toolAlerts.ForeColor = Color.Gray;
            else if (hasUnseenErrors)
                toolAlerts.ForeColor = Color.Firebrick;
            else
                toolAlerts.ForeColor = Color.Goldenrod;
        }

        private void ApplyProcessorProgress(string orderId, int progressValue)
        {
            var boundedValue = Math.Clamp(progressValue, 0, 100);
            var matchedAny = false;

            foreach (var internalId in _runTokensByOrder.Keys.ToList())
            {
                var runningOrder = FindOrderByInternalId(internalId);
                if (runningOrder == null)
                    continue;

                if (!string.Equals(runningOrder.Id ?? string.Empty, orderId ?? string.Empty, StringComparison.Ordinal))
                    continue;

                _runProgressByOrderInternalId[internalId] = boundedValue;
                matchedAny = true;
            }

            if (!matchedAny && _runTokensByOrder.Count == 1)
            {
                var onlyInternalId = _runTokensByOrder.Keys.First();
                _runProgressByOrderInternalId[onlyInternalId] = boundedValue;
            }

            UpdateTrayProgressIndicator();
        }

        private void BeginFileTransferStatus(string operationText)
        {
            var nextText = string.IsNullOrWhiteSpace(operationText)
                ? "Копирование файла"
                : operationText.Trim();

            RunOnUiThread(() =>
            {
                _activeFileTransfers++;
                _fileTransferStatusText = nextText;
                _fileTransferProgressPercent = 0;
                _fileTransferIsIndeterminate = true;
                UpdateTrayProgressIndicator();
            });
        }

        private void ReportFileTransferStatus(string operationText, long copiedBytes, long totalBytes)
        {
            var nextText = string.IsNullOrWhiteSpace(operationText)
                ? "Копирование файла"
                : operationText.Trim();

            var hasKnownSize = totalBytes > 0;
            var nextProgress = hasKnownSize
                ? Math.Clamp((int)Math.Round((double)copiedBytes * 100d / totalBytes), 0, 100)
                : 0;

            RunOnUiThread(() =>
            {
                if (_activeFileTransfers <= 0)
                    return;

                _fileTransferStatusText = nextText;
                _fileTransferIsIndeterminate = !hasKnownSize;
                _fileTransferProgressPercent = nextProgress;
                UpdateTrayProgressIndicator();
            });
        }

        private void EndFileTransferStatus()
        {
            RunOnUiThread(() =>
            {
                if (_activeFileTransfers > 0)
                    _activeFileTransfers--;

                if (_activeFileTransfers == 0)
                {
                    _fileTransferStatusText = string.Empty;
                    _fileTransferProgressPercent = -1;
                    _fileTransferIsIndeterminate = false;
                }

                UpdateTrayProgressIndicator();
            });
        }

        private void UpdateTrayProgressIndicator()
        {
            if (toolProgress.IsDisposed)
                return;

            if (_activeFileTransfers > 0)
            {
                var progressValue = Math.Clamp(_fileTransferProgressPercent, 0, 100);
                if (_fileTransferIsIndeterminate)
                {
                    toolProgress.Style = ProgressBarStyle.Marquee;
                    toolProgress.MarqueeAnimationSpeed = 30;
                }
                else
                {
                    toolProgress.Style = ProgressBarStyle.Continuous;
                    toolProgress.MarqueeAnimationSpeed = 0;
                    toolProgress.Value = progressValue;
                }

                toolProgress.Visible = true;
                toolProgress.ToolTipText = _fileTransferIsIndeterminate
                    ? $"{_fileTransferStatusText}: выполняется."
                    : $"{_fileTransferStatusText}: {progressValue}%.";
                RefreshBottomStatusLabel();
                return;
            }

            toolProgress.Style = ProgressBarStyle.Continuous;
            toolProgress.MarqueeAnimationSpeed = 0;

            if (_runProgressByOrderInternalId.Count == 0)
            {
                toolProgress.Visible = false;
                toolProgress.Value = 0;
                toolProgress.ToolTipText = "Нет активной обработки.";
                RefreshBottomStatusLabel();
                return;
            }

            var averageProgress = (int)Math.Round(_runProgressByOrderInternalId.Values.Average());
            var boundedProgress = Math.Clamp(averageProgress, 0, 100);
            toolProgress.Value = boundedProgress;
            toolProgress.Visible = true;
            toolProgress.ToolTipText = _runProgressByOrderInternalId.Count == 1
                ? $"Прогресс обработки: {boundedProgress}%."
                : $"Средний прогресс по {_runProgressByOrderInternalId.Count} заказам: {boundedProgress}%.";
            RefreshBottomStatusLabel();
        }

        private void AcknowledgeErrorNotifications()
        {
            _acknowledgedErrorCount = CountOrdersWithErrors();
            UpdateTrayErrorIndicator();
        }

        private int CountOrdersWithErrors()
        {
            var count = 0;
            foreach (var order in _orderHistory)
            {
                if (order == null)
                    continue;

                var normalizedStatus = NormalizeStatus(order.Status);
                if (string.Equals(normalizedStatus, WorkflowStatusNames.Error, StringComparison.Ordinal))
                    count++;
            }

            return count;
        }

        private bool TryGetProbeDrive(out DriveInfo drive)
        {
            drive = null!;

            var probePath = FirstNotEmpty(_ordersRootPath, _tempRootPath, _grandpaFolder);
            if (string.IsNullOrWhiteSpace(probePath))
                return false;

            string root;
            try
            {
                root = Path.GetPathRoot(probePath) ?? string.Empty;
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(root))
                return false;

            try
            {
                drive = new DriveInfo(root);
                return drive.IsReady;
            }
            catch
            {
                return false;
            }
        }

        private static bool CanAccessPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                    return true;

                var root = Path.GetPathRoot(fullPath);
                return !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
            }
            catch
            {
                return false;
            }
        }

        private static string FormatStorageSize(long bytes)
        {
            if (bytes < 0)
                bytes = 0;

            string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? $"{value:0} {units[unit]}"
                : $"{value:0.0} {units[unit]}";
        }

        private void SetBottomStatus(string text)
        {
            if (Disposing || IsDisposed)
                return;

            var nextText = string.IsNullOrWhiteSpace(text)
                ? DefaultTrayStatusText
                : text.Trim();

            _baseBottomStatusText = nextText;
            RefreshBottomStatusLabel();
        }

        private void RefreshBottomStatusLabel()
        {
            if (Disposing || IsDisposed)
                return;

            void Apply()
            {
                if (toolStatus.IsDisposed)
                    return;

                toolStatus.Text = ComposeBottomStatusText();
            }

            if (InvokeRequired)
                BeginInvoke((Action)Apply);
            else
                Apply();
        }

        private string ComposeBottomStatusText()
        {
            var baseText = string.IsNullOrWhiteSpace(_baseBottomStatusText)
                ? DefaultTrayStatusText
                : _baseBottomStatusText.Trim();

            var fileTransferCaption = BuildFileTransferCaption();
            var runningOrdersCaption = BuildRunningOrdersCaption();

            if (string.IsNullOrWhiteSpace(fileTransferCaption) && string.IsNullOrWhiteSpace(runningOrdersCaption))
                return baseText;

            if (string.IsNullOrWhiteSpace(fileTransferCaption))
                return $"{baseText} | {runningOrdersCaption}";

            if (string.IsNullOrWhiteSpace(runningOrdersCaption))
                return $"{baseText} | {fileTransferCaption}";

            return $"{baseText} | {fileTransferCaption} | {runningOrdersCaption}";
        }

        private string BuildFileTransferCaption()
        {
            if (_activeFileTransfers <= 0)
                return string.Empty;

            var statusText = string.IsNullOrWhiteSpace(_fileTransferStatusText)
                ? "Копирование файла"
                : _fileTransferStatusText;

            var progressCaption = _fileTransferIsIndeterminate
                ? "выполняется"
                : $"{Math.Clamp(_fileTransferProgressPercent, 0, 100)}%";

            if (_activeFileTransfers <= 1)
                return $"{statusText}: {progressCaption}";

            return $"{statusText}: {progressCaption} (+{_activeFileTransfers - 1})";
        }

        private string BuildRunningOrdersCaption()
        {
            if (_runTokensByOrder.Count == 0)
                return string.Empty;

            var runningOrderIds = new List<string>();
            foreach (var internalId in _runTokensByOrder.Keys)
            {
                var order = FindOrderByInternalId(internalId);
                if (order == null)
                    continue;

                var displayId = GetOrderDisplayId(order);
                if (!string.IsNullOrWhiteSpace(displayId))
                    runningOrderIds.Add(displayId);
            }

            if (runningOrderIds.Count == 0)
                return $"В работе: {_runTokensByOrder.Count}";

            var distinctIds = runningOrderIds
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (distinctIds.Count == 1)
                return $"Обработка: {distinctIds[0]}";

            const int maxShownOrders = 3;
            if (distinctIds.Count <= maxShownOrders)
                return $"Обработка: {string.Join(", ", distinctIds)}";

            var shownIds = string.Join(", ", distinctIds.Take(maxShownOrders));
            return $"Обработка: {shownIds} (+{distinctIds.Count - maxShownOrders})";
        }

        private string GetOrderLogFilePath(OrderData order)
        {
            var safeId = string.IsNullOrWhiteSpace(order.InternalId) ? order.Id : order.InternalId;
            if (string.IsNullOrWhiteSpace(safeId))
                safeId = "unknown-order";

            foreach (var c in Path.GetInvalidFileNameChars())
                safeId = safeId.Replace(c, '_');

            var logFolder = StoragePaths.ResolveFolderPath(_orderLogsFolderPath, "order-logs");
            Directory.CreateDirectory(logFolder);
            return Path.Combine(logFolder, $"{safeId}.log");
        }

        private void AppendOrderStatusLog(OrderData order, string oldStatus, string newStatus, string source, string reason)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | status: {oldStatus} -> {newStatus} | source: {source} | reason: {reason} | print-weight: {DescribePrintWeight(order)}";
                File.AppendAllText(GetOrderLogFilePath(order), line + Environment.NewLine);
                Logger.Info($"ORDER-STATUS | order={GetOrderDisplayId(order)} | {line}");
            }
            catch
            {
                // Лог не должен ломать основной поток.
            }
        }

        private void AppendOrderOperationLog(OrderData order, string operation, string details)
        {
            if (order == null)
                return;

            try
            {
                var opName = string.IsNullOrWhiteSpace(operation) ? "operation" : operation.Trim();
                var opDetails = string.IsNullOrWhiteSpace(details) ? "-" : details.Trim();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | op: {opName} | details: {opDetails}";
                File.AppendAllText(GetOrderLogFilePath(order), line + Environment.NewLine);
                Logger.Info($"ORDER-OP | order={GetOrderDisplayId(order)} | {line}");

                TryAppendRepositoryEvent(
                    order,
                    itemId: string.Empty,
                    eventType: opName,
                    eventSource: OrderStatusSourceNames.Ui,
                    payload: new
                    {
                        operation = opName,
                        details = opDetails,
                        logged_at = DateTime.Now
                    });
            }
            catch
            {
                // Лог не должен ломать основной поток.
            }
        }

        private void AppendCapturedProcessorLog(OrderData order, string message)
        {
            if (order == null || string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                var trimmed = message.Trim();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {trimmed}";
                File.AppendAllText(GetOrderLogFilePath(order), line + Environment.NewLine);
                Logger.Info($"ORDER-CAPTURED | order={GetOrderDisplayId(order)} | {trimmed}");
            }
            catch
            {
                // Лог не должен ломать основной поток.
            }
        }

        private void RunOnUiThread(Action action)
        {
            if (action == null || Disposing || IsDisposed)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }

            action();
        }

    }
}

