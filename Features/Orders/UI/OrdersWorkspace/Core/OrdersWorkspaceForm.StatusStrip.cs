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
            toolConnection.Click -= ToolConnection_Click;
            toolConnection.Click += ToolConnection_Click;

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
            RequestLanServerProbe("startup", force: true);
        }

        private void ToolAlerts_Click(object? sender, EventArgs e)
        {
            AcknowledgeErrorNotifications();
            OpenLogForSelectionOrManager();
        }

        private void ToolConnection_MouseEnter(object? sender, EventArgs e)
        {
            RequestLanServerProbe("hover", force: true);
            UpdateTrayConnectionIndicator();
        }

        private async void ToolConnection_Click(object? sender, EventArgs e)
        {
            if (!ShouldUseLanRunApi()
                || _lanApiRecoveryInProgress
                || !_lanConnectionRecoveryActionEnabled)
                return;

            _lanApiRecoveryInProgress = true;
            UpdateTrayConnectionIndicator();

            try
            {
                SetBottomStatus("Проверяем подключение к LAN API...");
                TryStartLocalLanApiIfNeeded(out var recoveryMessage);
                if (!string.IsNullOrWhiteSpace(recoveryMessage))
                    SetBottomStatus(recoveryMessage);

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
            RefreshArchivedStatuses();
            if (BackfillMissingFileHashesIncrementally(maxFilesToHash: 2))
                SaveHistory();
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

            toolConnection.MouseEnter -= ToolConnection_MouseEnter;
            toolConnection.Click -= ToolConnection_Click;

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

            var dependencyHealthLevel = GetWorstDependencyHealthLevel();
            if (ShouldUseLanRunApi())
            {
                RequestLanServerProbe("status-refresh");
                UpdateLanApiConnectionIndicator(dependencyHealthLevel);
                return;
            }

            var isConnected = CanAccessPath(_ordersRootPath);
            _lanConnectionRecoveryActionEnabled = false;
            string shortStatusText;
            Color statusColor;
            if (!isConnected)
            {
                shortStatusText = "автономно";
                statusColor = Color.Firebrick;
            }
            else if (dependencyHealthLevel == DependencyHealthLevel.Unavailable)
            {
                shortStatusText = "hotfolder недоступен";
                statusColor = Color.Firebrick;
            }
            else if (dependencyHealthLevel == DependencyHealthLevel.Degraded)
            {
                shortStatusText = "подключен (деградация)";
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
            toolConnection.ToolTipText = string.IsNullOrWhiteSpace(dependencyHealthSummary)
                ? $"{connectionStatusText}\n{_usersDirectoryStatusText}"
                : $"{connectionStatusText}\n{dependencyHealthSummary}\n{_usersDirectoryStatusText}";
            UpdateServerHeaderConnectionState(shortStatusText, statusColor);
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

        private void UpdateLanApiConnectionIndicator(DependencyHealthLevel dependencyHealthLevel)
        {
            var snapshot = GetLanServerProbeSnapshot(out var probeInProgress, out var requestCount);

            var disconnected = LanApiConnectionStatusEvaluator.IsDisconnected(
                snapshot.ApiReachable,
                snapshot.ConsecutiveFailureCount,
                LanServerProbeFailureThreshold);
            _lanConnectionRecoveryActionEnabled = disconnected && !_lanApiRecoveryInProgress;

            string shortStatusText;
            Color statusColor;
            if (disconnected)
            {
                shortStatusText = _lanApiRecoveryInProgress
                    ? "переподключение..."
                    : (!snapshot.ApiReachable
                        ? (snapshot.CompletedAtUtc == DateTime.MinValue ? "проверка..." : "нет ответа API")
                        : "не подключен");
                statusColor = Color.Firebrick;
            }
            else if (LanApiConnectionStatusEvaluator.IsTransientFailure(
                         snapshot.ApiReachable,
                         snapshot.ConsecutiveFailureCount,
                         LanServerProbeFailureThreshold))
            {
                shortStatusText = snapshot.CompletedAtUtc == DateTime.MinValue
                    ? "проверка..."
                    : "нестабильно, перепроверяем";
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
                    ? "подключен (деградация)"
                    : "API доступен, ждём ready";
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
            toolConnection.IsLink = _lanConnectionRecoveryActionEnabled;
            toolConnection.LinkBehavior = _lanConnectionRecoveryActionEnabled
                ? LinkBehavior.HoverUnderline
                : LinkBehavior.NeverUnderline;
            toolConnection.LinkColor = statusColor;
            toolConnection.ActiveLinkColor = statusColor;
            toolConnection.VisitedLinkColor = statusColor;
            toolConnection.ToolTipText = BuildLanConnectionToolTip(snapshot, dependencyHealthLevel, probeInProgress, requestCount);
            UpdateServerHeaderConnectionState(shortStatusText, statusColor);
        }

        private string BuildLanConnectionToolTip(
            LanServerProbeSnapshot snapshot,
            DependencyHealthLevel dependencyHealthLevel,
            bool probeInProgress,
            int requestCount)
        {
            var lines = new List<string>
            {
                "Режим: LAN PostgreSQL",
                $"API: {NormalizeLanApiUrlForUi(_lanApiBaseUrl)}",
                $"Состояние live/ready/slo: {snapshot.LiveStatus}/{snapshot.ReadyStatus}/{snapshot.SloStatus}"
            };

            if (snapshot.RequestedAtUtc > DateTime.MinValue)
                lines.Add($"Последний запрос: {FormatLanProbeStamp(snapshot.RequestedAtUtc)}");
            if (snapshot.CompletedAtUtc > DateTime.MinValue)
                lines.Add($"Последний ответ: {FormatLanProbeStamp(snapshot.CompletedAtUtc)}");
            if (snapshot.SuccessfulAtUtc > DateTime.MinValue)
                lines.Add($"Последний успешный ответ: {FormatLanProbeStamp(snapshot.SuccessfulAtUtc)}");

            if (snapshot.AvailabilityRatio >= 0
                && snapshot.LatencyP95Ms >= 0
                && snapshot.WriteSuccessRatio >= 0)
            {
                lines.Add(
                    $"SLO: avail={snapshot.AvailabilityRatio:0.000}, p95={snapshot.LatencyP95Ms:0.#} ms, write={snapshot.WriteSuccessRatio:0.000}");
            }

            lines.Add($"Проверок за сессию: {requestCount}");
            if (snapshot.ConsecutiveFailureCount > 0)
                lines.Add($"Неудачных проверок подряд: {snapshot.ConsecutiveFailureCount}");

            if (probeInProgress)
                lines.Add("Проверка выполняется...");
            if (!string.IsNullOrWhiteSpace(snapshot.Error))
                lines.Add($"Ошибка: {snapshot.Error}");
            if (dependencyHealthLevel != DependencyHealthLevel.Healthy)
                lines.Add($"Hotfolder: {BuildDependencyHealthSummary()}");
            if (_lanConnectionRecoveryActionEnabled)
                lines.Add("Действие: нажмите на статус '↻ Сервер...' для переподключения.");
            if (!string.IsNullOrWhiteSpace(_usersDirectoryStatusText))
                lines.Add(_usersDirectoryStatusText);

            return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
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
                await Task.WhenAll(liveProbeTask, readyProbeTask, sloProbeTask);

                var liveProbe = await liveProbeTask;
                var readyProbe = await readyProbeTask;
                var sloProbe = await sloProbeTask;
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

                var mergedError = string.Join(
                    " | ",
                    new[] { liveProbe.Error, readyProbe.Error, sloProbe.Error }
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
                    WriteSuccessRatio = writeSuccess
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
                var previousSuccessUtc = _lanServerProbeSnapshot.SuccessfulAtUtc;
                var previousFailureCount = _lanServerProbeSnapshot.ConsecutiveFailureCount;
                var nextFailureCount = nextSnapshot.ApiReachable
                    ? 0
                    : string.Equals(nextSnapshot.LiveStatus, "cancelled", StringComparison.OrdinalIgnoreCase)
                        ? previousFailureCount
                        : previousFailureCount + 1;
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
                        ConsecutiveFailureCount = nextFailureCount
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
                        ConsecutiveFailureCount = nextFailureCount
                    };
                }

                _lanServerProbeSnapshot = nextSnapshot;
                if (nextSnapshot.SuccessfulAtUtc > DateTime.MinValue)
                    _lanServerProbeLastSuccessfulUtc = nextSnapshot.SuccessfulAtUtc;
                _lanServerProbeInProgress = false;
            }

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

        private bool TryStartLocalLanApiIfNeeded(out string message)
        {
            message = string.Empty;
            if (!IsLanApiLocalHost())
            {
                message = "Автовосстановление доступно только для localhost API.";
                return false;
            }

            var runningApiProcesses = Process.GetProcessesByName("Replica.Api");
            if (runningApiProcesses.Length > 0)
            {
                message = "Replica.Api уже запущен. Выполняем повторную проверку.";
                return false;
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
                    message = "Запущен локальный Replica.Api, ждём готовность...";
                    return true;
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
                    message = "Запущен локальный Replica.Api (dotnet), ждём готовность...";
                    return true;
                }
                catch
                {
                    // попробуем следующий кандидат
                }
            }

            message = "Replica.Api не найден рядом с клиентом. Запустите API вручную.";
            return false;
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

        private static IEnumerable<string> ResolveReplicaApiExecutableCandidates()
        {
            return ReplicaApiLaunchLocator.ResolveExecutableCandidates(AppContext.BaseDirectory);
        }

        private static IEnumerable<string> ResolveReplicaApiDllCandidates()
        {
            return ReplicaApiLaunchLocator.ResolveDllCandidates(AppContext.BaseDirectory);
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

        private async Task<EndpointProbeResult> ProbeEndpointStatusAsync(Uri baseUri, string endpointPath, CancellationToken cancellationToken)
        {
            try
            {
                var endpointUri = new Uri(baseUri, endpointPath);
                using var request = new HttpRequestMessage(HttpMethod.Get, endpointUri);
                using var response = await _lanStatusHttpClient.SendAsync(request, cancellationToken);
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                var fallbackStatus = response.IsSuccessStatusCode
                    ? "ok"
                    : response.StatusCode == HttpStatusCode.ServiceUnavailable
                        ? "not_ready"
                        : "error";
                var status = ExtractStatusFromJsonPayload(payload, fallbackStatus);

                return new EndpointProbeResult
                {
                    IsReachable = true,
                    Status = status,
                    Payload = payload,
                    Error = response.IsSuccessStatusCode
                        ? string.Empty
                        : $"{endpointPath}: HTTP {(int)response.StatusCode} ({response.StatusCode})"
                };
            }
            catch (Exception ex)
            {
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

