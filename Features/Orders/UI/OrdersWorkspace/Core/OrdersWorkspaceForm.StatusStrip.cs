using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Replica.Shared;
using Svg;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private void InitializeTrayIndicators()
        {
            statusStrip1.ShowItemToolTips = true;
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

            toolConnection.MouseEnter -= ToolConnection_MouseEnter;
            toolConnection.MouseEnter += ToolConnection_MouseEnter;
            toolConnection.MouseLeave -= ToolConnection_MouseLeave;
            toolConnection.MouseLeave += ToolConnection_MouseLeave;
            toolConnection.Click -= ToolConnection_Click;
            toolConnection.Click += ToolConnection_Click;
            toolConnection.AutoToolTip = false;

            statusStrip1.MouseMove -= StatusStrip1_MouseMove;
            statusStrip1.MouseMove += StatusStrip1_MouseMove;
            statusStrip1.MouseLeave -= StatusStrip1_MouseLeave;
            statusStrip1.MouseLeave += StatusStrip1_MouseLeave;

            _lanServerProbeCts?.Cancel();
            _lanServerProbeCts?.Dispose();
            _lanServerProbeCts = new CancellationTokenSource();

            _trayIndicatorsTimer ??= new System.Windows.Forms.Timer
            {
                Interval = TrayIndicatorsRefreshIntervalMs
            };
            _trayIndicatorsTimer.Tick -= TrayIndicatorsTimer_Tick;
            _trayIndicatorsTimer.Tick += TrayIndicatorsTimer_Tick;
            _trayIndicatorsTimer.Start();

            _acknowledgedErrorCount = CountOrdersWithErrors();
            RefreshTrayIndicators();
            EnsureLocalLanApiStartup();
            InitializeLanOrderPushBridge();
            RequestLanServerProbe("startup", force: true);
        }

        private void ToolAlerts_Click(object? sender, EventArgs e)
        {
            AcknowledgeErrorNotifications();
            OpenLogForSelectionOrManager();
        }

        private void ToolConnection_MouseEnter(object? sender, EventArgs e)
        {
            ShowPersistentConnectionToolTip();
        }

        private void ToolConnection_MouseLeave(object? sender, EventArgs e)
        {
            HidePersistentConnectionToolTip();
        }

        private void StatusStrip1_MouseMove(object? sender, MouseEventArgs e)
        {
            if (statusStrip1.IsDisposed)
                return;

            var hoveredItem = statusStrip1.GetItemAt(e.Location);
            if (hoveredItem == toolConnection)
            {
                ShowPersistentConnectionToolTip();
                return;
            }

            HidePersistentConnectionToolTip();
        }

        private void StatusStrip1_MouseLeave(object? sender, EventArgs e)
        {
            HidePersistentConnectionToolTip();
        }

        private async void ToolConnection_Click(object? sender, EventArgs e)
        {
            if (!ShouldUseLanRunApi()
                || _lanApiRecoveryInProgress)
                return;

            if (!_lanConnectionRecoveryActionEnabled)
            {
                if (TryAcknowledgeLanPushPressureAlerts())
                {
                    SetBottomStatus("Push-предупреждение подтверждено.");
                    UpdateTrayConnectionIndicator();
                }

                return;
            }

            _lanApiRecoveryInProgress = true;
            UpdateTrayConnectionIndicator();

            try
            {
                SetBottomStatus("Проверяем подключение к LAN API...");
                await TryStartLocalLanApiIfNeededAsync();

                RequestLanServerProbe("manual-refresh", force: true);
                await WaitForLanProbeCompletionAsync(TimeSpan.FromSeconds(8));

                var snapshot = GetLanServerProbeSnapshot(out _, out _);
                if (snapshot.ApiReachable && snapshot.IsReady)
                    SetBottomStatus("Подключение к LAN API активно.");
                else if (_lanServerProbeLastSuccessfulUtc > DateTime.MinValue)
                    SetBottomStatus("Сервер временно недоступен, ждём восстановление.");
                else
                    SetBottomStatus("LAN API недоступен. Проверьте сервер или URL.");
            }
            finally
            {
                _lanApiRecoveryInProgress = false;
                UpdateTrayConnectionIndicator();
            }
        }

        private void TrayIndicatorsTimer_Tick(object? sender, EventArgs e)
        {
            RefreshArchivedStatusesIfDue();
            RefreshUsersDirectoryIfNeeded();
            RequestLanServerProbe("timer");
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
            _searchDebounceTimer?.Stop();
            if (_searchDebounceTimer != null)
            {
                _searchDebounceTimer.Tick -= SearchDebounceTimer_Tick;
                _searchDebounceTimer.Dispose();
                _searchDebounceTimer = null;
            }
            _gridRefreshCoalesceTimer?.Stop();
            if (_gridRefreshCoalesceTimer != null)
            {
                _gridRefreshCoalesceTimer.Tick -= GridRefreshCoalesceTimer_Tick;
                _gridRefreshCoalesceTimer.Dispose();
                _gridRefreshCoalesceTimer = null;
            }
            _gridRefreshPending = false;
            _gridRefreshPendingForceFullRebuild = false;
            _gridRefreshPendingSelectedTag = null;
            _gridRefreshPendingTargetOrderInternalId = null;
            _gridDerivedRefreshCoalesceTimer?.Stop();
            if (_gridDerivedRefreshCoalesceTimer != null)
            {
                _gridDerivedRefreshCoalesceTimer.Tick -= GridDerivedRefreshCoalesceTimer_Tick;
                _gridDerivedRefreshCoalesceTimer.Dispose();
                _gridDerivedRefreshCoalesceTimer = null;
            }
            _gridDerivedRefreshPending = false;

            toolConnection.MouseEnter -= ToolConnection_MouseEnter;
            toolConnection.MouseLeave -= ToolConnection_MouseLeave;
            toolConnection.Click -= ToolConnection_Click;
            statusStrip1.MouseMove -= StatusStrip1_MouseMove;
            statusStrip1.MouseLeave -= StatusStrip1_MouseLeave;
            HidePersistentConnectionToolTip();
            _connectionStatusPopup.Dispose();

            if (_trayIndicatorsTimer != null)
            {
                _trayIndicatorsTimer.Stop();
                _trayIndicatorsTimer.Tick -= TrayIndicatorsTimer_Tick;
                _trayIndicatorsTimer.Dispose();
                _trayIndicatorsTimer = null;
            }

            _lanServerProbeCts?.Cancel();
            _lanServerProbeCts?.Dispose();
            _lanServerProbeCts = null;
            DisposeLanOrderPushBridge();
            _lanStatusHttpClient.Dispose();
        }

        private void RefreshTrayIndicators()
        {
            UpdateTrayStatsIndicator();
            UpdateTrayConnectionIndicator();
            UpdateTrayDiskIndicator();
            UpdateTrayErrorIndicator();
            UpdateTrayProgressIndicator();
        }

        private void RefreshTrayIndicatorsForGridChange()
        {
            UpdateTrayStatsIndicator();
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

            var visibleOrders = GetVisibleOrdersCountForStats();

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

        private int GetVisibleOrdersCountForStats()
        {
            if (_visibleOrdersCountCacheValid)
                return _visibleOrdersCountCache;

            var visibleOrders = 0;
            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow || !row.Visible)
                    continue;

                visibleOrders++;
            }

            _visibleOrdersCountCache = visibleOrders;
            _visibleOrdersCountCacheValid = true;
            return visibleOrders;
        }

        private void UpdateTrayConnectionIndicator()
        {
            if (toolConnection.IsDisposed)
                return;

            if (_connectionStatusToolTipVisible)
            {
                _pendingConnectionIndicatorRefresh = true;
                return;
            }

            _pendingConnectionIndicatorRefresh = false;

            var dependencyHealthLevel = GetWorstDependencyHealthLevel();
            if (ShouldUseLanRunApi())
            {
                RequestLanServerProbe("status-refresh");
                UpdateLanApiConnectionIndicator(dependencyHealthLevel);
                return;
            }

            var isConnected = CanAccessPath(_ordersRootPath);
            _lanConnectionRecoveryActionEnabled = false;
            _lanPushPressureAckActionEnabled = false;
            string shortStatusText;
            Color statusColor;
            if (!isConnected)
            {
                shortStatusText = "автономно";
                statusColor = Color.Firebrick;
            }
            else if (dependencyHealthLevel == DependencyHealthLevel.Unavailable)
            {
                shortStatusText = "сервисы недоступны";
                statusColor = Color.Firebrick;
            }
            else if (dependencyHealthLevel == DependencyHealthLevel.Degraded)
            {
                shortStatusText = "есть связь, но есть проблемы";
                statusColor = Color.DarkOrange;
            }
            else
            {
                shortStatusText = "подключен";
                statusColor = Color.SeaGreen;
            }

            toolConnection.IsLink = false;
            toolConnection.LinkBehavior = LinkBehavior.NeverUnderline;
            toolConnection.LinkColor = statusColor;
            toolConnection.ActiveLinkColor = statusColor;
            toolConnection.VisitedLinkColor = statusColor;
            toolConnection.Text = $"● Сервер: {shortStatusText}";
            toolConnection.ForeColor = statusColor;
            var connectionStatusText = isConnected
                ? "Рабочая папка заказов доступна."
                : "Рабочая папка заказов недоступна.";
            var dependencyHealthSummary = BuildDependencyHealthSummary();
            _connectionStatusToolTipContent = string.IsNullOrWhiteSpace(dependencyHealthSummary)
                ? $"{connectionStatusText}\n{_usersDirectoryStatusText}"
                : $"{connectionStatusText}\n{dependencyHealthSummary}\n{_usersDirectoryStatusText}";
            toolConnection.ToolTipText = string.Empty;
            RefreshPersistentConnectionToolTip();
            UpdateServerHeaderConnectionState(shortStatusText, statusColor);
            ApplyServerHardLockState(shouldLock: false, details: string.Empty);
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

            List<string> parts = [];
            foreach (var dependency in _dependencyHealthByName.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var levelText = dependency.Value switch
                {
                    DependencyHealthLevel.Unavailable => "недоступно",
                    DependencyHealthLevel.Degraded => "есть проблемы",
                    _ => "доступно"
                };

                parts.Add($"{dependency.Key}: {levelText}");
            }

            return "Сервисы: " + string.Join(", ", parts);
        }

        private void UpdateLanApiConnectionIndicator(DependencyHealthLevel dependencyHealthLevel)
        {
            var snapshot = GetLanServerProbeSnapshot(out var probeInProgress, out var requestCount);

            var disconnected = LanApiConnectionStatusEvaluator.IsDisconnected(
                snapshot.ApiReachable,
                snapshot.ConsecutiveFailureCount,
                LanServerProbeFailureThreshold);
            _lanConnectionRecoveryActionEnabled = disconnected && !_lanApiRecoveryInProgress;
            _lanPushPressureAckActionEnabled = !_lanConnectionRecoveryActionEnabled
                                               && IsLanPushPressureAckAvailable();

            string shortStatusText;
            Color statusColor;
            if (disconnected)
            {
                shortStatusText = _lanApiRecoveryInProgress
                    ? "переподключение..."
                    : (!snapshot.ApiReachable
                        ? (snapshot.CompletedAtUtc == DateTime.MinValue ? "проверяем связь..." : "нет связи")
                        : "нет связи");
                statusColor = Color.Firebrick;
            }
            else if (LanApiConnectionStatusEvaluator.IsTransientFailure(
                         snapshot.ApiReachable,
                         snapshot.ConsecutiveFailureCount,
                         LanServerProbeFailureThreshold))
            {
                shortStatusText = snapshot.CompletedAtUtc == DateTime.MinValue
                    ? "проверяем связь..."
                    : "связь нестабильна";
                statusColor = Color.DarkOrange;
            }
            else if (LanApiConnectionStatusEvaluator.IsDegraded(
                         snapshot.ApiReachable,
                         snapshot.IsReady,
                         snapshot.IsDegraded,
                         snapshot.SloHealthy,
                         dependencyHealthLevel))
            {
                shortStatusText = snapshot.IsReady
                    ? "есть связь, но есть проблемы"
                    : "сервер запускается";
                statusColor = Color.DarkOrange;
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.ProcessAlert))
            {
                shortStatusText = "есть связь, но сервер с ошибками";
                statusColor = Color.DarkOrange;
            }
            else
            {
                shortStatusText = "подключен";
                statusColor = Color.SeaGreen;
            }

            toolConnection.Text = disconnected
                ? $"↻ Сервер: {shortStatusText}"
                : $"● Сервер: {shortStatusText}";
            toolConnection.ForeColor = statusColor;
            var hasConnectionAction = _lanConnectionRecoveryActionEnabled || _lanPushPressureAckActionEnabled;
            toolConnection.IsLink = hasConnectionAction;
            toolConnection.LinkBehavior = hasConnectionAction
                ? LinkBehavior.HoverUnderline
                : LinkBehavior.NeverUnderline;
            toolConnection.LinkColor = statusColor;
            toolConnection.ActiveLinkColor = statusColor;
            toolConnection.VisitedLinkColor = statusColor;
            _connectionStatusToolTipContent = BuildLanConnectionToolTip(snapshot, dependencyHealthLevel, probeInProgress, requestCount);
            toolConnection.ToolTipText = string.Empty;
            RefreshPersistentConnectionToolTip();
            UpdateServerHeaderConnectionState(shortStatusText, statusColor);
            ApplyServerHardLockFromLanSnapshot(snapshot, probeInProgress);
        }

        private void RefreshPersistentConnectionToolTip()
        {
            if (!_connectionStatusToolTipVisible)
                return;

            // Keep the current tooltip stable while the pointer stays hovered.
            // Connection status updates frequently enough to cause visible flicker
            // if we recreate the tooltip on every probe refresh.
        }

        private void ShowPersistentConnectionToolTip(bool forceRefresh = false)
        {
            if (statusStrip1.IsDisposed || toolConnection.IsDisposed)
                return;

            var toolTipText = _connectionStatusToolTipContent ?? string.Empty;
            if (string.IsNullOrWhiteSpace(toolTipText))
            {
                HidePersistentConnectionToolTip();
                return;
            }

            if (!forceRefresh && _connectionStatusToolTipVisible)
            {
                return;
            }

            var bounds = toolConnection.Bounds;
            var anchorScreenPoint = statusStrip1.PointToScreen(new Point(bounds.Left + 8, bounds.Top - 8));
            _connectionStatusPopup.ShowPopup(toolTipText, anchorScreenPoint);
            _connectionStatusToolTipVisible = true;
        }

        private void HidePersistentConnectionToolTip()
        {
            if (!_connectionStatusToolTipVisible)
                return;

            _connectionStatusPopup.Hide();
            _connectionStatusToolTipVisible = false;

            if (_pendingConnectionIndicatorRefresh && !toolConnection.IsDisposed)
                UpdateTrayConnectionIndicator();
        }

        private string BuildLanConnectionToolTip(
            LanServerProbeSnapshot snapshot,
            DependencyHealthLevel dependencyHealthLevel,
            bool probeInProgress,
            int requestCount)
        {
            var operatorStatus = GetLanOperatorStatusText(snapshot, dependencyHealthLevel, probeInProgress);
            var lines = new List<string>
            {
                "Режим: работа через сервер",
                $"API: {NormalizeLanApiUrlForUi(_lanApiBaseUrl)}",
                $"Статус: {operatorStatus}",
                $"Проверки live/ready/slo: {BuildLanProbeStateSummary(snapshot)}"
            };

            var problemReasons = BuildLanProblemReasons(snapshot, dependencyHealthLevel, probeInProgress);
            if (problemReasons.Count > 0)
            {
                lines.Add("Причины:");
                foreach (var reason in problemReasons)
                    lines.Add($"- {reason}");
            }

            if (snapshot.CompletedAtUtc > DateTime.MinValue)
                lines.Add($"Последняя проверка: {FormatLanProbeStamp(snapshot.CompletedAtUtc)}");
            if (snapshot.SuccessfulAtUtc > DateTime.MinValue)
                lines.Add($"Последний успешный ответ: {FormatLanProbeStamp(snapshot.SuccessfulAtUtc)}");

            lines.Add($"Проверок за сессию: {requestCount}");
            if (snapshot.ConsecutiveFailureCount > 0)
                lines.Add($"Сбоев подряд: {snapshot.ConsecutiveFailureCount}");

            if (probeInProgress)
                lines.Add("Проверка выполняется...");
            if (!string.IsNullOrWhiteSpace(snapshot.Error))
                lines.Add($"Подробно: {TruncateTooltipText(snapshot.Error)}");
            if (snapshot.HttpRequests5xx >= 0 || snapshot.WriteBadRequest >= 0)
            {
                lines.Add($"Ошибки API: 5xx={Math.Max(0, snapshot.HttpRequests5xx)}, bad_request={Math.Max(0, snapshot.WriteBadRequest)}");
            }
            if (snapshot.PushPublishedTotal >= 0 || snapshot.PushPublishFailuresTotal >= 0)
            {
                var successRatioText = snapshot.PushPublishSuccessRatio >= 0
                    ? $"{snapshot.PushPublishSuccessRatio:P0}"
                    : "n/a";
                lines.Add($"Push API publish/fail: {Math.Max(0, snapshot.PushPublishedTotal)}/{Math.Max(0, snapshot.PushPublishFailuresTotal)} ({successRatioText})");
            }
            if (snapshot.LastServerEventAtUtc > DateTime.MinValue)
            {
                var eventOrderText = string.IsNullOrWhiteSpace(snapshot.LastServerEventOrderId)
                    ? "-"
                    : snapshot.LastServerEventOrderId;
                lines.Add($"Последняя серверная операция: {snapshot.LastServerEventType} (order={eventOrderText}, {FormatLanProbeStamp(snapshot.LastServerEventAtUtc)})");
            }
            if (!string.IsNullOrWhiteSpace(snapshot.ProcessAlert))
                lines.Add($"Сбой на сервере: {TruncateTooltipText(snapshot.ProcessAlert)}");
            foreach (var pushLine in BuildLanPushDiagnosticsLines())
                lines.Add(pushLine);
            if (dependencyHealthLevel != DependencyHealthLevel.Healthy)
                lines.Add(BuildDependencyHealthSummary());
            if (_lanConnectionRecoveryActionEnabled)
                lines.Add("Нажмите на красный статус для переподключения.");
            else if (_lanPushPressureAckActionEnabled)
                lines.Add("Нажмите статус сервера, чтобы подтвердить push-предупреждение.");
            if (!string.IsNullOrWhiteSpace(_usersDirectoryStatusText))
                lines.Add(_usersDirectoryStatusText);

            return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        private string GetLanOperatorStatusText(
            LanServerProbeSnapshot snapshot,
            DependencyHealthLevel dependencyHealthLevel,
            bool probeInProgress)
        {
            if (_lanApiRecoveryInProgress)
                return "переподключение...";

            if (probeInProgress && snapshot.CompletedAtUtc <= DateTime.MinValue)
                return "проверяем связь...";

            if (!snapshot.ApiReachable)
                return "нет связи с API";

            if (!snapshot.IsReady)
                return "сервер запускается";

            if (!string.IsNullOrWhiteSpace(snapshot.ProcessAlert))
                return "есть связь, но сервер с ошибками";

            if (snapshot.IsDegraded || !snapshot.SloHealthy || dependencyHealthLevel != DependencyHealthLevel.Healthy)
                return "есть связь, но есть проблемы";

            return "подключен";
        }

        private List<string> BuildLanProblemReasons(
            LanServerProbeSnapshot snapshot,
            DependencyHealthLevel dependencyHealthLevel,
            bool probeInProgress)
        {
            List<string> reasons = [];
            var hasProblemState =
                !snapshot.ApiReachable
                || !snapshot.IsReady
                || snapshot.IsDegraded
                || !snapshot.SloHealthy
                || dependencyHealthLevel != DependencyHealthLevel.Healthy
                || !string.IsNullOrWhiteSpace(snapshot.ProcessAlert)
                || (probeInProgress && snapshot.CompletedAtUtc <= DateTime.MinValue);

            if (!hasProblemState)
                return reasons;

            if (probeInProgress && snapshot.CompletedAtUtc <= DateTime.MinValue)
            {
                reasons.Add("Идет первая проверка сервера.");
                return reasons;
            }

            if (!snapshot.ApiReachable)
            {
                reasons.Add("API не отвечает на health-проверки.");
                return reasons;
            }

            if (!snapshot.IsReady)
            {
                reasons.Add($"Ready: {NormalizeLanProbeStatusForUi(snapshot.ReadyStatus)} (сервер еще не готов к записи).");
            }
            else if (string.Equals(snapshot.ReadyStatus, "degraded", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("Ready: degraded (сервер работает с ограничениями).");
            }

            if (!snapshot.SloHealthy)
                reasons.Add($"SLO: {NormalizeLanProbeStatusForUi(snapshot.SloStatus)}.");

            if (dependencyHealthLevel == DependencyHealthLevel.Unavailable)
                reasons.Add("Локальные зависимости: есть недоступные сервисы.");
            else if (dependencyHealthLevel == DependencyHealthLevel.Degraded)
                reasons.Add("Локальные зависимости: часть сервисов работает нестабильно.");

            if (!string.IsNullOrWhiteSpace(snapshot.ProcessAlert))
                reasons.Add("Сервер сообщил о внутренних ошибках в недавних операциях.");

            if (snapshot.HttpRequests5xx > 0)
                reasons.Add($"Ошибки API 5xx за сессию: {snapshot.HttpRequests5xx}.");
            if (snapshot.WriteBadRequest > 0)
                reasons.Add($"Ошибки API bad_request за сессию: {snapshot.WriteBadRequest}.");

            return reasons;
        }

        private static string BuildLanProbeStateSummary(LanServerProbeSnapshot snapshot)
        {
            return $"{NormalizeLanProbeStatusForUi(snapshot.LiveStatus)}/{NormalizeLanProbeStatusForUi(snapshot.ReadyStatus)}/{NormalizeLanProbeStatusForUi(snapshot.SloStatus)}";
        }

        private static string NormalizeLanProbeStatusForUi(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "нет данных";

            if (string.Equals(status, "unknown", StringComparison.OrdinalIgnoreCase))
                return "проверяется";
            if (string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase))
                return "ready";
            if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                return "ok";
            if (string.Equals(status, "degraded", StringComparison.OrdinalIgnoreCase))
                return "degraded";
            if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                return "ошибка";
            if (string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
                return "отменено";

            return status;
        }

        private static string TruncateTooltipText(string text, int maxLength = 260)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text.Trim();
            if (normalized.Length <= maxLength)
                return normalized;

            return normalized.Substring(0, maxLength - 3) + "...";
        }

        private void RequestLanServerProbe(string reason, bool force = false)
        {
            if (!ShouldUseLanRunApi() || Disposing || IsDisposed)
                return;

            var token = CancellationToken.None;
            lock (_lanServerProbeSync)
            {
                if (_lanServerProbeInProgress)
                    return;

                var nowUtc = DateTime.UtcNow;
                if (!force
                    && _lanServerProbeLastRequestedUtc > DateTime.MinValue
                    && (nowUtc - _lanServerProbeLastRequestedUtc).TotalMilliseconds < LanServerProbeMinIntervalMs)
                {
                    return;
                }

                _lanServerProbeInProgress = true;
                _lanServerProbeLastRequestedUtc = nowUtc;
                _lanServerProbeRequestCount++;
                token = _lanServerProbeCts?.Token ?? CancellationToken.None;
            }

            UpdateTrayConnectionIndicator();
            _ = ProbeLanServerAsync(reason, token);
        }

        private async Task ProbeLanServerAsync(string reason, CancellationToken cancellationToken)
        {
            var requestedAtUtc = DateTime.UtcNow;
            var completedAtUtc = requestedAtUtc;

            LanServerProbeSnapshot nextSnapshot;
            try
            {
                if (!TryResolveLanApiBaseUri(_lanApiBaseUrl, out var baseUri))
                    throw new InvalidOperationException("Некорректный URL LAN API.");

                var liveProbeTask = ProbeEndpointStatusAsync(baseUri, "live", cancellationToken);
                var readyProbeTask = ProbeEndpointStatusAsync(baseUri, "ready", cancellationToken);
                var sloProbeTask = ProbeEndpointStatusAsync(baseUri, "slo", cancellationToken);
                var metricsProbeTask = ProbeEndpointStatusAsync(baseUri, "metrics", cancellationToken, optionalEndpoint: true);
                var diagnosticsProbeTask = ProbeEndpointStatusAsync(baseUri, "api/diagnostics/operations/recent?limit=1", cancellationToken, optionalEndpoint: true);
                var pushDiagnosticsProbeTask = ProbeEndpointStatusAsync(baseUri, "api/diagnostics/push", cancellationToken, optionalEndpoint: true);
                await Task.WhenAll(liveProbeTask, readyProbeTask, sloProbeTask, metricsProbeTask, diagnosticsProbeTask, pushDiagnosticsProbeTask);

                var liveProbe = await liveProbeTask;
                var readyProbe = await readyProbeTask;
                var sloProbe = await sloProbeTask;
                var metricsProbe = await metricsProbeTask;
                var diagnosticsProbe = await diagnosticsProbeTask;
                var pushDiagnosticsProbe = await pushDiagnosticsProbeTask;
                completedAtUtc = DateTime.UtcNow;

                var apiReachable = liveProbe.IsReachable || readyProbe.IsReachable || sloProbe.IsReachable;
                var liveStatus = liveProbe.Status;
                var readyStatus = readyProbe.Status;
                var sloStatus = sloProbe.Status;

                var isReady = string.Equals(readyStatus, "ready", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(readyStatus, "degraded", StringComparison.OrdinalIgnoreCase);
                var isDegraded = !isReady
                                 || string.Equals(readyStatus, "degraded", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(sloStatus, "degraded", StringComparison.OrdinalIgnoreCase);
                var sloHealthy = string.Equals(sloStatus, "ok", StringComparison.OrdinalIgnoreCase);

                var availabilityRatio = -1d;
                var latencyP95 = -1d;
                var writeSuccess = -1d;
                if (!string.IsNullOrWhiteSpace(sloProbe.Payload))
                {
                    TryGetSloCurrentMetric(sloProbe.Payload, "HttpAvailabilityRatio", out availabilityRatio);
                    TryGetSloCurrentMetric(sloProbe.Payload, "HttpLatencyP95Ms", out latencyP95);
                    TryGetSloCurrentMetric(sloProbe.Payload, "WriteSuccessRatio", out writeSuccess);
                }

                var serverNowUtc = DateTime.MinValue;
                if (!string.IsNullOrWhiteSpace(liveProbe.Payload))
                    TryExtractUtcDateTime(liveProbe.Payload, "now", out serverNowUtc);

                var httpRequests5xx = -1L;
                var writeBadRequest = -1L;
                if (!string.IsNullOrWhiteSpace(metricsProbe.Payload))
                {
                    TryGetLongMetric(metricsProbe.Payload, "HttpRequests5xx", out httpRequests5xx);
                    TryGetLongMetric(metricsProbe.Payload, "WriteBadRequest", out writeBadRequest);
                }

                var lastServerEventAtUtc = DateTime.MinValue;
                var lastServerEventType = string.Empty;
                var lastServerEventOrderId = string.Empty;
                if (!string.IsNullOrWhiteSpace(diagnosticsProbe.Payload))
                {
                    TryExtractRecentServerOperation(
                        diagnosticsProbe.Payload,
                        out lastServerEventAtUtc,
                        out lastServerEventType,
                        out lastServerEventOrderId);
                }

                var pushPublishedTotal = -1L;
                var pushPublishFailuresTotal = -1L;
                var pushPublishSuccessRatio = -1d;
                if (!string.IsNullOrWhiteSpace(pushDiagnosticsProbe.Payload))
                {
                    TryGetLongMetric(pushDiagnosticsProbe.Payload, "PublishedTotal", out pushPublishedTotal);
                    TryGetLongMetric(pushDiagnosticsProbe.Payload, "PublishFailuresTotal", out pushPublishFailuresTotal);
                    TryGetDoubleMetric(pushDiagnosticsProbe.Payload, "PublishSuccessRatio", out pushPublishSuccessRatio);
                }

                var mergedError = string.Join(
                    " | ",
                    new[] { liveProbe.Error, readyProbe.Error, sloProbe.Error, metricsProbe.Error, diagnosticsProbe.Error, pushDiagnosticsProbe.Error }
                        .Where(error => !string.IsNullOrWhiteSpace(error)));

                nextSnapshot = new LanServerProbeSnapshot
                {
                    ApiReachable = apiReachable,
                    IsReady = isReady,
                    IsDegraded = isDegraded,
                    SloHealthy = sloHealthy,
                    RequestedAtUtc = requestedAtUtc,
                    CompletedAtUtc = completedAtUtc,
                    SuccessfulAtUtc = apiReachable && isReady ? completedAtUtc : DateTime.MinValue,
                    ServerNowAtUtc = serverNowUtc,
                    LiveStatus = liveStatus,
                    ReadyStatus = readyStatus,
                    SloStatus = sloStatus,
                    Error = mergedError,
                    ProbeReason = reason ?? string.Empty,
                    AvailabilityRatio = availabilityRatio,
                    LatencyP95Ms = latencyP95,
                    WriteSuccessRatio = writeSuccess,
                    HttpRequests5xx = httpRequests5xx,
                    WriteBadRequest = writeBadRequest,
                    LastServerEventAtUtc = lastServerEventAtUtc,
                    LastServerEventType = lastServerEventType,
                    LastServerEventOrderId = lastServerEventOrderId,
                    PushPublishedTotal = pushPublishedTotal,
                    PushPublishFailuresTotal = pushPublishFailuresTotal,
                    PushPublishSuccessRatio = pushPublishSuccessRatio
                };
            }
            catch (OperationCanceledException)
            {
                nextSnapshot = new LanServerProbeSnapshot
                {
                    ApiReachable = false,
                    IsReady = false,
                    IsDegraded = false,
                    SloHealthy = false,
                    RequestedAtUtc = requestedAtUtc,
                    CompletedAtUtc = DateTime.UtcNow,
                    SuccessfulAtUtc = DateTime.MinValue,
                    LiveStatus = "cancelled",
                    ReadyStatus = "cancelled",
                    SloStatus = "cancelled",
                    Error = "Проверка отменена.",
                    ProbeReason = reason ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                nextSnapshot = new LanServerProbeSnapshot
                {
                    ApiReachable = false,
                    IsReady = false,
                    IsDegraded = false,
                    SloHealthy = false,
                    RequestedAtUtc = requestedAtUtc,
                    CompletedAtUtc = DateTime.UtcNow,
                    SuccessfulAtUtc = DateTime.MinValue,
                    LiveStatus = "error",
                    ReadyStatus = "error",
                    SloStatus = "error",
                    Error = ex.Message,
                    ProbeReason = reason ?? string.Empty
                };
            }

            lock (_lanServerProbeSync)
            {
                var previousSnapshot = _lanServerProbeSnapshot;
                var previousSuccessUtc = _lanServerProbeSnapshot.SuccessfulAtUtc;
                var previousFailureCount = _lanServerProbeSnapshot.ConsecutiveFailureCount;
                var nextFailureCount = nextSnapshot.ApiReachable
                    ? 0
                    : string.Equals(nextSnapshot.LiveStatus, "cancelled", StringComparison.OrdinalIgnoreCase)
                        ? previousFailureCount
                        : previousFailureCount + 1;

                var processAlert = string.Empty;
                var processAlertAtUtc = DateTime.MinValue;
                var has5xxCounters = nextSnapshot.HttpRequests5xx >= 0 && previousSnapshot.HttpRequests5xx >= 0;
                var hasWriteBadCounters = nextSnapshot.WriteBadRequest >= 0 && previousSnapshot.WriteBadRequest >= 0;
                var added5xx = has5xxCounters ? Math.Max(0, nextSnapshot.HttpRequests5xx - previousSnapshot.HttpRequests5xx) : 0;
                var addedWriteBad = hasWriteBadCounters ? Math.Max(0, nextSnapshot.WriteBadRequest - previousSnapshot.WriteBadRequest) : 0;

                if (added5xx > 0 || addedWriteBad > 0)
                {
                    processAlert = $"HTTP 5xx +{added5xx}, write bad_request +{addedWriteBad}";
                    processAlertAtUtc = nextSnapshot.CompletedAtUtc > DateTime.MinValue
                        ? nextSnapshot.CompletedAtUtc
                        : DateTime.UtcNow;
                    Logger.Warn($"LAN-SERVER | process-alert | {processAlert}");
                }
                else if (previousSnapshot.ProcessAlertAtUtc > DateTime.MinValue
                         && (DateTime.UtcNow - previousSnapshot.ProcessAlertAtUtc).TotalMinutes <= 15)
                {
                    processAlert = previousSnapshot.ProcessAlert;
                    processAlertAtUtc = previousSnapshot.ProcessAlertAtUtc;
                }

                if (nextSnapshot.SuccessfulAtUtc == DateTime.MinValue && previousSuccessUtc > DateTime.MinValue)
                {
                    nextSnapshot = new LanServerProbeSnapshot
                    {
                        ApiReachable = nextSnapshot.ApiReachable,
                        IsReady = nextSnapshot.IsReady,
                        IsDegraded = nextSnapshot.IsDegraded,
                        SloHealthy = nextSnapshot.SloHealthy,
                        RequestedAtUtc = nextSnapshot.RequestedAtUtc,
                        CompletedAtUtc = nextSnapshot.CompletedAtUtc,
                        SuccessfulAtUtc = previousSuccessUtc,
                        ServerNowAtUtc = nextSnapshot.ServerNowAtUtc,
                        LiveStatus = nextSnapshot.LiveStatus,
                        ReadyStatus = nextSnapshot.ReadyStatus,
                        SloStatus = nextSnapshot.SloStatus,
                        Error = nextSnapshot.Error,
                        ProbeReason = nextSnapshot.ProbeReason,
                        AvailabilityRatio = nextSnapshot.AvailabilityRatio,
                        LatencyP95Ms = nextSnapshot.LatencyP95Ms,
                        WriteSuccessRatio = nextSnapshot.WriteSuccessRatio,
                        ConsecutiveFailureCount = nextFailureCount,
                        HttpRequests5xx = nextSnapshot.HttpRequests5xx,
                        WriteBadRequest = nextSnapshot.WriteBadRequest,
                        LastServerEventAtUtc = nextSnapshot.LastServerEventAtUtc,
                        LastServerEventType = nextSnapshot.LastServerEventType,
                        LastServerEventOrderId = nextSnapshot.LastServerEventOrderId,
                        PushPublishedTotal = nextSnapshot.PushPublishedTotal,
                        PushPublishFailuresTotal = nextSnapshot.PushPublishFailuresTotal,
                        PushPublishSuccessRatio = nextSnapshot.PushPublishSuccessRatio,
                        ProcessAlert = processAlert,
                        ProcessAlertAtUtc = processAlertAtUtc
                    };
                }
                else
                {
                    nextSnapshot = new LanServerProbeSnapshot
                    {
                        ApiReachable = nextSnapshot.ApiReachable,
                        IsReady = nextSnapshot.IsReady,
                        IsDegraded = nextSnapshot.IsDegraded,
                        SloHealthy = nextSnapshot.SloHealthy,
                        RequestedAtUtc = nextSnapshot.RequestedAtUtc,
                        CompletedAtUtc = nextSnapshot.CompletedAtUtc,
                        SuccessfulAtUtc = nextSnapshot.SuccessfulAtUtc,
                        ServerNowAtUtc = nextSnapshot.ServerNowAtUtc,
                        LiveStatus = nextSnapshot.LiveStatus,
                        ReadyStatus = nextSnapshot.ReadyStatus,
                        SloStatus = nextSnapshot.SloStatus,
                        Error = nextSnapshot.Error,
                        ProbeReason = nextSnapshot.ProbeReason,
                        AvailabilityRatio = nextSnapshot.AvailabilityRatio,
                        LatencyP95Ms = nextSnapshot.LatencyP95Ms,
                        WriteSuccessRatio = nextSnapshot.WriteSuccessRatio,
                        ConsecutiveFailureCount = nextFailureCount,
                        HttpRequests5xx = nextSnapshot.HttpRequests5xx,
                        WriteBadRequest = nextSnapshot.WriteBadRequest,
                        LastServerEventAtUtc = nextSnapshot.LastServerEventAtUtc,
                        LastServerEventType = nextSnapshot.LastServerEventType,
                        LastServerEventOrderId = nextSnapshot.LastServerEventOrderId,
                        PushPublishedTotal = nextSnapshot.PushPublishedTotal,
                        PushPublishFailuresTotal = nextSnapshot.PushPublishFailuresTotal,
                        PushPublishSuccessRatio = nextSnapshot.PushPublishSuccessRatio,
                        ProcessAlert = processAlert,
                        ProcessAlertAtUtc = processAlertAtUtc
                    };
                }

                _lanServerProbeSnapshot = nextSnapshot;
                if (nextSnapshot.SuccessfulAtUtc > DateTime.MinValue)
                    _lanServerProbeLastSuccessfulUtc = nextSnapshot.SuccessfulAtUtc;
                _lanServerProbeInProgress = false;
            }

            LogLanServerProbeSnapshot(nextSnapshot);
            RunOnUiThread(UpdateTrayConnectionIndicator);
        }

        private LanServerProbeSnapshot GetLanServerProbeSnapshot(out bool probeInProgress, out int requestCount)
        {
            lock (_lanServerProbeSync)
            {
                probeInProgress = _lanServerProbeInProgress;
                requestCount = _lanServerProbeRequestCount;
                return _lanServerProbeSnapshot;
            }
        }

        private async Task WaitForLanProbeCompletionAsync(TimeSpan timeout)
        {
            var startedAt = DateTime.UtcNow;
            while (DateTime.UtcNow - startedAt < timeout)
            {
                lock (_lanServerProbeSync)
                {
                    if (!_lanServerProbeInProgress)
                        return;
                }

                await Task.Delay(120);
            }
        }

        private Task<bool> TryStartLocalLanApiIfNeededAsync()
        {
            if (!IsLanApiLocalHost())
            {
                SetBottomStatus("Автовосстановление доступно только для localhost API.");
                return Task.FromResult(false);
            }

            var snapshot = GetLanServerProbeSnapshot(out _, out _);
            if (snapshot.ApiReachable)
            {
                SetBottomStatus("LAN API уже доступен. Выполняем повторную проверку.");
                return Task.FromResult(false);
            }

            var runningApiProcesses = Process.GetProcessesByName("Replica.Api");
            if (runningApiProcesses.Length > 0)
            {
                SetBottomStatus("Replica.Api уже запущен. Выполняем повторную проверку.");
                return Task.FromResult(false);
            }

            foreach (var exeCandidate in ResolveReplicaApiExecutableCandidates())
            {
                if (!File.Exists(exeCandidate))
                    continue;

                try
                {
                    var startInfo = BuildHiddenReplicaApiStartInfo(
                        exeCandidate,
                        workingDirectory: Path.GetDirectoryName(exeCandidate) ?? AppContext.BaseDirectory);
                    Process.Start(startInfo);
                    SetBottomStatus("Запущен локальный Replica.Api, ждём готовность...");
                    return Task.FromResult(true);
                }
                catch
                {
                    // попробуем следующий кандидат
                }
            }

            foreach (var dllCandidate in ResolveReplicaApiDllCandidates())
            {
                if (!File.Exists(dllCandidate))
                    continue;

                try
                {
                    var startInfo = BuildHiddenReplicaApiStartInfo(
                        "dotnet",
                        arguments: $"\"{dllCandidate}\"",
                        workingDirectory: Path.GetDirectoryName(dllCandidate) ?? AppContext.BaseDirectory);
                    Process.Start(startInfo);
                    SetBottomStatus("Запущен локальный Replica.Api (dotnet), ждём готовность...");
                    return Task.FromResult(true);
                }
                catch
                {
                    // попробуем следующий кандидат
                }
            }

            foreach (var projectCandidate in ResolveReplicaApiProjectCandidates())
            {
                if (!File.Exists(projectCandidate))
                    continue;

                try
                {
                    var startInfo = BuildHiddenReplicaApiStartInfo(
                        "dotnet",
                        arguments: $"run --project \"{projectCandidate}\"",
                        workingDirectory: Path.GetDirectoryName(projectCandidate) ?? AppContext.BaseDirectory);
                    Process.Start(startInfo);
                    SetBottomStatus("Запущен локальный Replica.Api (dotnet run), ждём готовность...");
                    return Task.FromResult(true);
                }
                catch
                {
                    // попробуем следующий кандидат
                }
            }

            SetBottomStatus("Replica.Api не найден рядом с клиентом. Запустите API вручную.");
            return Task.FromResult(false);
        }

        private bool IsLanApiLocalHost()
        {
            if (!TryResolveLanApiBaseUri(_lanApiBaseUrl, out var baseUri))
                return false;

            var host = baseUri.Host;
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
        }

        private async void EnsureLocalLanApiStartup()
        {
            if (!ShouldUseLanRunApi() || !IsLanApiLocalHost())
                return;

            await TryStartLocalLanApiIfNeededAsync();
        }

        private static IEnumerable<string> ResolveReplicaApiExecutableCandidates()
        {
            return ReplicaApiLaunchLocator.ResolveExecutableCandidates(AppContext.BaseDirectory);
        }

        private static IEnumerable<string> ResolveReplicaApiDllCandidates()
        {
            return ReplicaApiLaunchLocator.ResolveDllCandidates(AppContext.BaseDirectory);
        }

        private static IEnumerable<string> ResolveReplicaApiProjectCandidates()
        {
            return ReplicaApiLaunchLocator.ResolveProjectCandidates(AppContext.BaseDirectory);
        }

        private static ProcessStartInfo BuildHiddenReplicaApiStartInfo(
            string fileName,
            string arguments = "",
            string? workingDirectory = null)
        {
            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? AppContext.BaseDirectory : workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        private async Task<EndpointProbeResult> ProbeEndpointStatusAsync(Uri baseUri, string endpointPath, CancellationToken cancellationToken, bool optionalEndpoint = false)
        {
            try
            {
                var endpointUri = new Uri(baseUri, endpointPath);
                using var request = new HttpRequestMessage(HttpMethod.Get, endpointUri);
                var actor = ResolveLanApiActor();
                if (!string.IsNullOrWhiteSpace(actor))
                {
                    if (CurrentUserHeaderCodec.RequiresEncoding(actor))
                    {
                        request.Headers.TryAddWithoutValidation(
                            CurrentUserHeaderCodec.HeaderName,
                            CurrentUserHeaderCodec.BuildAsciiFallback(actor));
                        request.Headers.TryAddWithoutValidation(
                            CurrentUserHeaderCodec.EncodedHeaderName,
                            CurrentUserHeaderCodec.Encode(actor));
                    }
                    else
                    {
                        request.Headers.TryAddWithoutValidation(CurrentUserHeaderCodec.HeaderName, actor);
                    }
                }
                using var response = await _lanStatusHttpClient.SendAsync(request, cancellationToken);
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                var fallbackStatus = response.IsSuccessStatusCode
                    ? "ok"
                    : response.StatusCode == HttpStatusCode.ServiceUnavailable
                        ? "not_ready"
                        : "error";
                var status = ExtractStatusFromJsonPayload(payload, fallbackStatus);
                var isOptionalUnavailable = optionalEndpoint
                    && (response.StatusCode == HttpStatusCode.NotFound
                        || response.StatusCode == HttpStatusCode.MethodNotAllowed
                        || response.StatusCode == HttpStatusCode.Unauthorized
                        || response.StatusCode == HttpStatusCode.Forbidden);

                return new EndpointProbeResult
                {
                    IsReachable = true,
                    Status = isOptionalUnavailable ? "optional-unavailable" : status,
                    Payload = payload,
                    Error = response.IsSuccessStatusCode || isOptionalUnavailable
                        ? string.Empty
                        : $"{endpointPath}: HTTP {(int)response.StatusCode} ({response.StatusCode})"
                };
            }
            catch (Exception ex)
            {
                if (optionalEndpoint)
                {
                    return new EndpointProbeResult
                    {
                        IsReachable = true,
                        Status = "optional-unavailable",
                        Payload = string.Empty,
                        Error = string.Empty
                    };
                }

                return new EndpointProbeResult
                {
                    IsReachable = false,
                    Status = "unavailable",
                    Payload = string.Empty,
                    Error = $"{endpointPath}: {ex.Message}"
                };
            }
        }

        private static bool TryResolveLanApiBaseUri(string? rawBaseUrl, out Uri baseUri)
        {
            baseUri = default!;
            if (string.IsNullOrWhiteSpace(rawBaseUrl))
                return false;

            var candidate = rawBaseUrl.Trim();
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsedBaseUri)
                && parsedBaseUri != null)
            {
                baseUri = parsedBaseUri;
                return true;
            }

            if (Uri.TryCreate($"http://{candidate}", UriKind.Absolute, out parsedBaseUri)
                && parsedBaseUri != null)
            {
                baseUri = parsedBaseUri;
                return true;
            }

            return false;
        }

        private static string NormalizeLanApiUrlForUi(string? rawBaseUrl)
        {
            if (!TryResolveLanApiBaseUri(rawBaseUrl, out var baseUri))
                return "н/д";

            return baseUri.GetLeftPart(UriPartial.Authority);
        }

        private static string ExtractStatusFromJsonPayload(string payload, string fallbackStatus)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return fallbackStatus;

            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return fallbackStatus;

                if (TryGetPropertyIgnoreCase(document.RootElement, "status", out var statusElement)
                    && statusElement.ValueKind == JsonValueKind.String)
                {
                    var value = statusElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim().ToLowerInvariant();
                }
            }
            catch
            {
                // ignore malformed payload
            }

            return fallbackStatus;
        }

        private static bool TryGetSloCurrentMetric(string payload, string metricName, out double value)
        {
            value = -1;
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                if (!TryGetPropertyIgnoreCase(document.RootElement, "current", out var currentElement)
                    || currentElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!TryGetPropertyIgnoreCase(currentElement, metricName, out var valueElement))
                    return false;

                if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetDouble(out value))
                    return true;
            }
            catch
            {
                // ignore malformed payload
            }

            return false;
        }

        private static bool TryGetLongMetric(string payload, string metricName, out long value)
        {
            value = -1;
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                if (!TryGetPropertyIgnoreCase(document.RootElement, metricName, out var valueElement))
                    return false;

                if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetInt64(out value))
                    return true;
            }
            catch
            {
                // ignore malformed payload
            }

            return false;
        }

        private static bool TryGetDoubleMetric(string payload, string metricName, out double value)
        {
            value = -1;
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                if (!TryGetPropertyIgnoreCase(document.RootElement, metricName, out var valueElement))
                    return false;

                if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetDouble(out value))
                    return true;
            }
            catch
            {
                // ignore malformed payload
            }

            return false;
        }

        private static bool TryExtractRecentServerOperation(
            string payload,
            out DateTime createdAtUtc,
            out string eventType,
            out string orderInternalId)
        {
            createdAtUtc = DateTime.MinValue;
            eventType = string.Empty;
            orderInternalId = string.Empty;

            if (string.IsNullOrWhiteSpace(payload))
                return false;

            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                    return false;

                var first = document.RootElement[0];
                if (first.ValueKind != JsonValueKind.Object)
                    return false;

                if (TryGetPropertyIgnoreCase(first, "eventType", out var eventTypeElement)
                    && eventTypeElement.ValueKind == JsonValueKind.String)
                {
                    eventType = eventTypeElement.GetString()?.Trim() ?? string.Empty;
                }

                if (TryGetPropertyIgnoreCase(first, "orderId", out var orderElement)
                    && orderElement.ValueKind == JsonValueKind.String)
                {
                    orderInternalId = orderElement.GetString()?.Trim() ?? string.Empty;
                }

                if (TryGetPropertyIgnoreCase(first, "createdAtUtc", out var createdAtElement)
                    && createdAtElement.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(
                        createdAtElement.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                {
                    createdAtUtc = parsed;
                }

                return !string.IsNullOrWhiteSpace(eventType) || createdAtUtc > DateTime.MinValue;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryExtractUtcDateTime(string payload, string propertyName, out DateTime value)
        {
            value = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                if (!TryGetPropertyIgnoreCase(document.RootElement, propertyName, out var propertyElement))
                    return false;
                if (propertyElement.ValueKind != JsonValueKind.String)
                    return false;

                var raw = propertyElement.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                if (DateTime.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
                {
                    value = parsed;
                    return true;
                }
            }
            catch
            {
                // ignore malformed payload
            }

            return false;
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                value = property.Value;
                return true;
            }

            value = default;
            return false;
        }

        private void LogLanServerProbeSnapshot(LanServerProbeSnapshot snapshot)
        {
            try
            {
                var fingerprint = string.Join(
                    "|",
                    snapshot.LiveStatus,
                    snapshot.ReadyStatus,
                    snapshot.SloStatus,
                    snapshot.HttpRequests5xx.ToString(CultureInfo.InvariantCulture),
                    snapshot.WriteBadRequest.ToString(CultureInfo.InvariantCulture),
                    snapshot.LastServerEventType,
                    snapshot.LastServerEventOrderId,
                    snapshot.LastServerEventAtUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                    snapshot.ProcessAlert,
                    snapshot.Error);

                if (string.Equals(_lastServerOpsProbeLogFingerprint, fingerprint, StringComparison.Ordinal))
                    return;

                _lastServerOpsProbeLogFingerprint = fingerprint;

                var logFilePath = BuildServerOpsLogFilePath();
                var logDirectory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrWhiteSpace(logDirectory))
                    Directory.CreateDirectory(logDirectory);

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | live/ready/slo={snapshot.LiveStatus}/{snapshot.ReadyStatus}/{snapshot.SloStatus} | " +
                           $"http5xx={snapshot.HttpRequests5xx} | write_bad_request={snapshot.WriteBadRequest} | " +
                           $"event={snapshot.LastServerEventType} | order={snapshot.LastServerEventOrderId} | " +
                           $"event_at={FormatLanProbeStamp(snapshot.LastServerEventAtUtc)} | alert={snapshot.ProcessAlert} | error={snapshot.Error}";
                File.AppendAllText(logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Лог диагностики не должен влиять на работу клиента.
            }
        }

        private string BuildServerOpsLogFilePath()
        {
            var managerLogDirectory = Path.GetDirectoryName(_managerLogFilePath);
            if (string.IsNullOrWhiteSpace(managerLogDirectory))
                managerLogDirectory = AppContext.BaseDirectory;

            return Path.Combine(managerLogDirectory, "server-ops.log");
        }

        private static string FormatLanProbeStamp(DateTime utcValue)
        {
            if (utcValue <= DateTime.MinValue)
                return "н/д";

            return utcValue
                .ToLocalTime()
                .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private sealed class EndpointProbeResult
        {
            public bool IsReachable { get; init; }
            public string Status { get; init; } = "unknown";
            public string Payload { get; init; } = string.Empty;
            public string Error { get; init; } = string.Empty;
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
            RunOnUiThread(UpdateTrayProgressIndicatorCore);
        }

        private void UpdateTrayProgressIndicatorCore()
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

            List<string> runningOrderIds = [];
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

    internal sealed class ConnectionStatusPopup : Form
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExTopmost = 0x00000008;
        private const int WsExNoActivate = 0x08000000;
        private readonly Label _contentLabel;

        internal ConnectionStatusPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(255, 255, 225);
            Padding = new Padding(10, 8, 10, 8);
            AutoScaleMode = AutoScaleMode.None;

            _contentLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                BackColor = Color.Transparent,
                ForeColor = Color.Black
            };

            Controls.Add(_contentLabel);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var createParams = base.CreateParams;
                createParams.ExStyle |= WsExToolWindow | WsExTopmost | WsExNoActivate;
                return createParams;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder(e.Graphics, ClientRectangle, Color.FromArgb(160, 160, 160), ButtonBorderStyle.Solid);
        }

        internal void ShowPopup(string text, Point anchorScreenPoint)
        {
            var normalizedText = text ?? string.Empty;
            if (_contentLabel.Text != normalizedText)
                _contentLabel.Text = normalizedText;

            var preferredSize = _contentLabel.GetPreferredSize(new Size(560, 0));
            ClientSize = new Size(preferredSize.Width + Padding.Horizontal, preferredSize.Height + Padding.Vertical);
            _contentLabel.Location = new Point(Padding.Left, Padding.Top);

            Location = new Point(
                Math.Max(anchorScreenPoint.X, 0),
                Math.Max(anchorScreenPoint.Y - Height, 0));

            if (!Visible)
            {
                Show();
                return;
            }

            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            NativeMethods.SetWindowPos(Handle, NativeMethods.HWndTopMost, Location.X, Location.Y, Width, Height, NativeMethods.SwpNoActivate);
            Invalidate();
        }

        private static class NativeMethods
        {
            internal static readonly IntPtr HWndTopMost = new(-1);
            internal const uint SwpNoActivate = 0x0010;

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            internal static extern bool SetWindowPos(
                IntPtr hWnd,
                IntPtr hWndInsertAfter,
                int x,
                int y,
                int cx,
                int cy,
                uint uFlags);
        }
    }
}

