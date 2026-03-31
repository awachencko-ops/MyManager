using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private void InitializeLanOrderPushBridge()
        {
            _lanOrderPushClient.EventReceived -= LanOrderPushClient_EventReceived;
            _lanOrderPushClient.ConnectionStateChanged -= LanOrderPushClient_ConnectionStateChanged;

            if (!ShouldUseLanRunApi())
            {
                SetLanPushConnectionState(LanOrderPushConnectionStates.Stopped, isConnected: false);
                return;
            }

            _lanOrderPushClient.EventReceived += LanOrderPushClient_EventReceived;
            _lanOrderPushClient.ConnectionStateChanged += LanOrderPushClient_ConnectionStateChanged;
            _ = StartLanOrderPushBridgeAsync();
        }

        private async Task StartLanOrderPushBridgeAsync()
        {
            SetLanPushConnectionState("connecting", isConnected: false);

            try
            {
                await _lanOrderPushClient.StartAsync(_lanApiBaseUrl, ResolveLanApiActor());
            }
            catch (Exception ex)
            {
                SetLanPushConnectionState(LanOrderPushConnectionStates.StartFailed, isConnected: false);
                Logger.Warn($"LAN-PUSH | start-failed | {ex.Message}");
            }
        }

        private void DisposeLanOrderPushBridge()
        {
            _lanOrderPushClient.EventReceived -= LanOrderPushClient_EventReceived;
            _lanOrderPushClient.ConnectionStateChanged -= LanOrderPushClient_ConnectionStateChanged;

            try
            {
                _lanOrderPushClient.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.Warn($"LAN-PUSH | stop-failed | {ex.Message}");
            }

            try
            {
                _lanOrderPushClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.Warn($"LAN-PUSH | dispose-failed | {ex.Message}");
            }

            SetLanPushConnectionState(LanOrderPushConnectionStates.Stopped, isConnected: false);
        }

        private void LanOrderPushClient_EventReceived(object? sender, LanOrderPushEventArgs e)
        {
            if (e?.PushEvent == null)
                return;

            QueueLanPushSnapshotRefresh(e.PushEvent);
        }

        private void LanOrderPushClient_ConnectionStateChanged(object? sender, LanOrderPushConnectionStateChangedEventArgs e)
        {
            if (e == null)
                return;

            ApplyLanPushConnectionStateMetrics(e.State, e.Error);

            if (!string.IsNullOrWhiteSpace(e.Message))
                Logger.Info($"LAN-PUSH | state={e.State} | {e.Message}");

            if (string.Equals(e.State, LanOrderPushConnectionStates.Reconnecting, StringComparison.OrdinalIgnoreCase))
            {
                RequestLanServerProbe("push-reconnecting", force: true);
                return;
            }

            if (!string.Equals(e.State, LanOrderPushConnectionStates.Reconnected, StringComparison.OrdinalIgnoreCase))
                return;

            RequestLanServerProbe("push-reconnected", force: true);
            QueueLanPushSnapshotRefresh(new LanOrderPushEvent(
                LanOrderPushEventNames.ForceRefresh,
                string.Empty,
                "reconnect-resync",
                DateTime.UtcNow));
        }

        private void QueueLanPushSnapshotRefresh(LanOrderPushEvent pushEvent)
        {
            if (!ShouldUseLanRunApi() || pushEvent == null || Disposing || IsDisposed)
                return;

            RecordLanPushEvent(pushEvent);

            lock (_lanPushRefreshSync)
            {
                _lanPushPendingEvent = pushEvent;
            }

            var previousPending = Interlocked.Exchange(ref _lanPushRefreshPending, 1);
            if (previousPending == 1)
            {
                var coalescedEventsCount = Interlocked.Increment(ref _lanPushCoalescedEventsCount);
                if (coalescedEventsCount % 50 == 0)
                    Logger.Info($"LAN-PUSH | coalesced-events={coalescedEventsCount}");
                TryEmitLanPushPressureAlert("coalesced");
            }

            if (Interlocked.CompareExchange(ref _lanPushRefreshInProgress, 1, 0) != 0)
                return;

            _ = RunLanPushSnapshotRefreshLoopAsync();
        }

        private async Task RunLanPushSnapshotRefreshLoopAsync()
        {
            try
            {
                while (true)
                {
                    if (Interlocked.Exchange(ref _lanPushRefreshPending, 0) == 0)
                        break;

                    LanOrderPushEvent pushEvent;
                    lock (_lanPushRefreshSync)
                    {
                        pushEvent = _lanPushPendingEvent;
                    }

                    var throttleDelay = ResolveLanPushRefreshThrottleDelay();
                    if (throttleDelay > TimeSpan.Zero)
                    {
                        var throttleDelayCount = Interlocked.Increment(ref _lanPushThrottleDelayCount);
                        if (throttleDelayCount % 50 == 0)
                            Logger.Info($"LAN-PUSH | throttle-delays={throttleDelayCount}");
                        TryEmitLanPushPressureAlert("throttled");
                        await Task.Delay(throttleDelay);
                    }

                    try
                    {
                        await ApplyLanPushSnapshotRefreshOnceAsync(pushEvent);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"LAN-PUSH | refresh-iteration-failed | {ex.Message}");
                    }
                    finally
                    {
                        MarkLanPushRefreshApplied();
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _lanPushRefreshInProgress, 0);

                if (Interlocked.CompareExchange(ref _lanPushRefreshPending, 0, 0) == 1
                    && Interlocked.CompareExchange(ref _lanPushRefreshInProgress, 1, 0) == 0)
                {
                    _ = RunLanPushSnapshotRefreshLoopAsync();
                }
            }
        }

        private async Task ApplyLanPushSnapshotRefreshOnceAsync(LanOrderPushEvent pushEvent)
        {
            if (!ShouldUseLanRunApi() || Disposing || IsDisposed)
                return;

            var usersSnapshot = _filterUsers.Count > 0
                ? _filterUsers.ToList()
                : new List<string> { UserIdentityResolver.DefaultDisplayName };
            var defaultUserName = usersSnapshot[0];

            var loadResult = await Task.Run(() =>
            {
                if (!TryLoadHistoryFromConfiguredRepository(
                        out var storageOrders,
                        allowReadOnlyCacheFallback: false,
                        out _))
                    return (Success: false, Orders: new List<OrderData>(), PostLoad: (OrdersHistoryPostLoadResult?)null);

                var postLoad = _orderApplicationService.ApplyHistoryPostLoad(
                    storageOrders,
                    userName => NormalizeOrderUserNameForBootstrap(userName, usersSnapshot, defaultUserName),
                    hashBackfillBudget: 0,
                    onTopologyIssue: (order, issue) =>
                        Logger.Warn($"TOPOLOGY | order={order?.Id} | {issue}"));

                return (Success: true, Orders: storageOrders, PostLoad: postLoad);
            });

            if (!loadResult.Success)
            {
                Logger.Warn(
                    $"LAN-PUSH | snapshot-refresh-failed | event={pushEvent.EventType} | order={pushEvent.OrderId} | reason={pushEvent.Reason}");
                RequestLanServerProbe("push-refresh-failed", force: true);
                return;
            }

            RunOnUiThread(() => ApplyLanPushSnapshotOnUi(
                pushEvent,
                loadResult.Orders,
                loadResult.PostLoad));
        }

        private void ApplyLanPushSnapshotOnUi(
            LanOrderPushEvent pushEvent,
            IReadOnlyCollection<OrderData> storageOrders,
            OrdersHistoryPostLoadResult? postLoad)
        {
            if (Disposing || IsDisposed || !ShouldUseLanRunApi())
                return;

            var selectedTag = dgvJobs.CurrentRow?.Tag?.ToString();
            MergeStorageSnapshotIntoLocalHistory(storageOrders);

            if (postLoad != null && postLoad.Changed && postLoad.MigrationLog.Count > 0)
            {
                foreach (var line in postLoad.MigrationLog)
                    Logger.Info(line);
            }

            var normalizedOrderId = pushEvent.OrderId?.Trim() ?? string.Empty;
            var preferFullRebuild = string.Equals(pushEvent.EventType, LanOrderPushEventNames.ForceRefresh, StringComparison.OrdinalIgnoreCase)
                                    || string.IsNullOrWhiteSpace(normalizedOrderId);

            RequestCoalescedGridRefresh(
                selectedTag: selectedTag,
                targetOrderInternalId: normalizedOrderId,
                preferFullRebuild: preferFullRebuild);

            if (string.Equals(pushEvent.EventType, LanOrderPushEventNames.ForceRefresh, StringComparison.OrdinalIgnoreCase)
                && string.Equals(pushEvent.Reason, "users-changed", StringComparison.OrdinalIgnoreCase))
            {
                RefreshUsersDirectory(forceRefresh: true, refreshGrid: true);
            }
        }

        private void ApplyLanPushConnectionStateMetrics(string state, Exception? error)
        {
            var normalizedState = string.IsNullOrWhiteSpace(state)
                ? LanOrderPushConnectionStates.Closed
                : state.Trim();

            var isConnected = string.Equals(normalizedState, LanOrderPushConnectionStates.Connected, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(normalizedState, LanOrderPushConnectionStates.Reconnected, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(normalizedState, LanOrderPushConnectionStates.Reconnected, StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref _lanPushReconnectCount);

            lock (_lanPushMetricsSync)
            {
                _lanPushConnected = isConnected;
                _lanPushConnectionState = normalizedState;
                _lanPushConnectionStateAtUtc = DateTime.UtcNow;
            }

            if (error != null)
                Logger.Warn($"LAN-PUSH | state={normalizedState} | error={error.Message}");

            RunOnUiThread(UpdateTrayConnectionIndicator);
        }

        private void SetLanPushConnectionState(string state, bool isConnected)
        {
            var normalizedState = string.IsNullOrWhiteSpace(state)
                ? LanOrderPushConnectionStates.Stopped
                : state.Trim();

            lock (_lanPushMetricsSync)
            {
                _lanPushConnected = isConnected;
                _lanPushConnectionState = normalizedState;
                _lanPushConnectionStateAtUtc = DateTime.UtcNow;
            }

            RunOnUiThread(UpdateTrayConnectionIndicator);
        }

        private void RecordLanPushEvent(LanOrderPushEvent pushEvent)
        {
            if (pushEvent == null)
                return;

            var nowUtc = DateTime.UtcNow;
            var lagMs = -1d;
            if (pushEvent.OccurredAtUtc > DateTime.MinValue)
                lagMs = Math.Max(0d, (nowUtc - pushEvent.OccurredAtUtc.ToUniversalTime()).TotalMilliseconds);

            Interlocked.Increment(ref _lanPushEventsReceivedCount);
            lock (_lanPushMetricsSync)
            {
                _lanPushLastEventAtUtc = nowUtc;
                _lanPushLastEventType = string.IsNullOrWhiteSpace(pushEvent.EventType)
                    ? LanOrderPushEventNames.ForceRefresh
                    : pushEvent.EventType.Trim();
                _lanPushLastEventLagMs = lagMs;
                if (!string.IsNullOrWhiteSpace(pushEvent.Reason))
                {
                    _lanPushLastForceRefreshReason = pushEvent.Reason.Trim();
                    if (_lanPushReasonCounters.TryGetValue(_lanPushLastForceRefreshReason, out var reasonCount))
                        _lanPushReasonCounters[_lanPushLastForceRefreshReason] = reasonCount + 1;
                    else
                        _lanPushReasonCounters[_lanPushLastForceRefreshReason] = 1;
                }
            }
        }

        private TimeSpan ResolveLanPushRefreshThrottleDelay()
        {
            lock (_lanPushMetricsSync)
            {
                if (_lanPushLastRefreshAtUtc <= DateTime.MinValue)
                    return TimeSpan.Zero;

                var elapsed = DateTime.UtcNow - _lanPushLastRefreshAtUtc;
                var minInterval = TimeSpan.FromMilliseconds(LanPushMinRefreshIntervalMs);
                if (elapsed >= minInterval)
                    return TimeSpan.Zero;

                return minInterval - elapsed;
            }
        }

        private void MarkLanPushRefreshApplied()
        {
            Interlocked.Increment(ref _lanPushRefreshAppliedCount);
            lock (_lanPushMetricsSync)
            {
                _lanPushLastRefreshAtUtc = DateTime.UtcNow;
            }
        }

        private IReadOnlyList<string> BuildLanPushDiagnosticsLines()
        {
            LanPushDiagnosticsSnapshot snapshot;
            var eventsReceivedCount = Interlocked.Read(ref _lanPushEventsReceivedCount);
            var refreshAppliedCount = Interlocked.Read(ref _lanPushRefreshAppliedCount);
            var coalescedEventsCount = Interlocked.Read(ref _lanPushCoalescedEventsCount);
            var throttleDelayCount = Interlocked.Read(ref _lanPushThrottleDelayCount);
            var reconnectCount = Interlocked.CompareExchange(ref _lanPushReconnectCount, 0, 0);
            var nowUtc = DateTime.UtcNow;
            lock (_lanPushMetricsSync)
            {
                if (LanPushPressureAlertEvaluator.ShouldResetState(
                        nowUtc,
                        _lanPushLastPressureAlertAtUtc,
                        LanPushPressureStateResetWindow))
                {
                    Interlocked.Exchange(ref _lanPushPressureAlertCount, 0);
                    _lanPushLastPressureAlertAtUtc = DateTime.MinValue;
                }

                snapshot = new LanPushDiagnosticsSnapshot
                {
                    IsConnected = _lanPushConnected,
                    ConnectionState = _lanPushConnectionState,
                    ConnectionStateAtUtc = _lanPushConnectionStateAtUtc,
                    LastEventAtUtc = _lanPushLastEventAtUtc,
                    LastRefreshAtUtc = _lanPushLastRefreshAtUtc,
                    LastEventType = _lanPushLastEventType,
                    LastForceRefreshReason = _lanPushLastForceRefreshReason,
                    LastPressureAlertAtUtc = _lanPushLastPressureAlertAtUtc,
                    PressureHintActive = LanPushPressureAlertEvaluator.IsHintActive(
                        nowUtc,
                        _lanPushLastPressureAlertAtUtc,
                        LanPushPressureHintActiveWindow),
                    ReasonCountersSummary = BuildLanPushReasonCountersSummary(_lanPushReasonCounters),
                    LastEventLagMs = _lanPushLastEventLagMs,
                    EventsReceivedCount = eventsReceivedCount,
                    RefreshAppliedCount = refreshAppliedCount,
                    CoalescedEventsCount = coalescedEventsCount,
                    ThrottleDelayCount = throttleDelayCount,
                    ReconnectCount = reconnectCount,
                    PressureAlertCount = Interlocked.Read(ref _lanPushPressureAlertCount)
                };
            }

            var lines = new List<string>
            {
                $"Push: {snapshot.ConnectionState}{(snapshot.IsConnected ? " (up)" : " (down)")}",
                $"Push events/refresh: {snapshot.EventsReceivedCount}/{snapshot.RefreshAppliedCount}"
            };

            if (snapshot.ConnectionStateAtUtc > DateTime.MinValue)
                lines.Add($"Push state at: {FormatLanProbeStamp(snapshot.ConnectionStateAtUtc)}");
            if (snapshot.LastRefreshAtUtc > DateTime.MinValue)
                lines.Add($"Push last refresh: {FormatLanProbeStamp(snapshot.LastRefreshAtUtc)}");
            if (snapshot.LastEventAtUtc > DateTime.MinValue)
            {
                var lastEventType = string.IsNullOrWhiteSpace(snapshot.LastEventType)
                    ? "-"
                    : snapshot.LastEventType;
                lines.Add($"Push last event: {lastEventType} ({FormatLanProbeStamp(snapshot.LastEventAtUtc)})");
            }

            if (snapshot.LastEventLagMs >= 0)
                lines.Add($"Push lag (last): {snapshot.LastEventLagMs:F0} ms");
            if (snapshot.ReconnectCount > 0)
                lines.Add($"Push reconnects: {snapshot.ReconnectCount}");
            if (snapshot.PressureAlertCount > 0)
                lines.Add($"Push pressure alerts: {snapshot.PressureAlertCount}");
            if (snapshot.LastPressureAlertAtUtc > DateTime.MinValue)
                lines.Add($"Push last pressure alert: {FormatLanProbeStamp(snapshot.LastPressureAlertAtUtc)}");
            if (snapshot.CoalescedEventsCount > 0 || snapshot.ThrottleDelayCount > 0)
            {
                var coalescedRate = snapshot.EventsReceivedCount > 0
                    ? (double)snapshot.CoalescedEventsCount / snapshot.EventsReceivedCount
                    : 0d;
                var throttleBase = snapshot.RefreshAppliedCount > 0
                    ? snapshot.RefreshAppliedCount
                    : snapshot.EventsReceivedCount;
                var throttledRate = throttleBase > 0
                    ? (double)snapshot.ThrottleDelayCount / throttleBase
                    : 0d;
                lines.Add(
                    $"Push coalesced/throttled: {snapshot.CoalescedEventsCount}/{snapshot.ThrottleDelayCount} ({coalescedRate:P0}/{throttledRate:P0})");
            }
            if (!string.IsNullOrWhiteSpace(snapshot.LastForceRefreshReason))
                lines.Add($"Push last force-refresh reason: {snapshot.LastForceRefreshReason}");
            if (!string.IsNullOrWhiteSpace(snapshot.ReasonCountersSummary))
                lines.Add($"Push reason counters: {snapshot.ReasonCountersSummary}");
            if (snapshot.PressureHintActive)
                lines.Add("Push hint: высокий поток событий, обновления могут приходить с небольшой задержкой.");

            return lines;
        }

        private string BuildLanPushReasonCountersSummary(IDictionary<string, long> counters)
        {
            if (counters == null || counters.Count == 0)
                return string.Empty;

            var parts = counters
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value > 0)
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Take(LanPushReasonCountersMaxItems)
                .Select(x => $"{x.Key}={x.Value}")
                .ToList();

            return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
        }

        private void TryEmitLanPushPressureAlert(string trigger)
        {
            var eventsReceived = Interlocked.Read(ref _lanPushEventsReceivedCount);
            var refreshApplied = Interlocked.Read(ref _lanPushRefreshAppliedCount);
            var coalesced = Interlocked.Read(ref _lanPushCoalescedEventsCount);
            var throttled = Interlocked.Read(ref _lanPushThrottleDelayCount);
            var nowUtc = DateTime.UtcNow;

            string reasonCounters;
            LanPushPressureAlertDecision decision;
            lock (_lanPushMetricsSync)
            {
                decision = LanPushPressureAlertEvaluator.Evaluate(
                    eventsReceived,
                    refreshApplied,
                    coalesced,
                    throttled,
                    LanPushPressureAlertMinEvents,
                    LanPushCoalescedRateAlertThreshold,
                    LanPushThrottledRateAlertThreshold,
                    nowUtc,
                    _lanPushLastPressureAlertAtUtc,
                    LanPushPressureAlertCooldown);

                if (!decision.ShouldAlert)
                    return;

                _lanPushLastPressureAlertAtUtc = nowUtc;
                Interlocked.Increment(ref _lanPushPressureAlertCount);
                reasonCounters = BuildLanPushReasonCountersSummary(_lanPushReasonCounters);
            }

            Logger.Warn(
                $"LAN-PUSH | pressure-alert | trigger={trigger} | events={eventsReceived} | refresh={refreshApplied} | coalesced={coalesced} ({decision.CoalescedRate:P0}) | throttled={throttled} ({decision.ThrottledRate:P0}) | reasons={reasonCounters}");
        }

        private bool IsLanPushPressureAckAvailable()
        {
            lock (_lanPushMetricsSync)
            {
                var pressureAlertCount = Interlocked.Read(ref _lanPushPressureAlertCount);
                if (pressureAlertCount <= 0)
                    return false;

                return LanPushPressureAlertEvaluator.IsHintActive(
                    DateTime.UtcNow,
                    _lanPushLastPressureAlertAtUtc,
                    LanPushPressureHintActiveWindow);
            }
        }

        private bool TryAcknowledgeLanPushPressureAlerts()
        {
            DateTime acknowledgedAtUtc;
            lock (_lanPushMetricsSync)
            {
                var pressureAlertCount = Interlocked.Read(ref _lanPushPressureAlertCount);
                var hasPressureState = pressureAlertCount > 0 || _lanPushLastPressureAlertAtUtc > DateTime.MinValue;
                if (!hasPressureState)
                    return false;

                Interlocked.Exchange(ref _lanPushPressureAlertCount, 0);
                _lanPushLastPressureAlertAtUtc = DateTime.MinValue;
                acknowledgedAtUtc = DateTime.UtcNow;
            }

            Logger.Info($"LAN-PUSH | pressure-alert-acknowledged | at={acknowledgedAtUtc:O}");
            return true;
        }

        private sealed class LanPushDiagnosticsSnapshot
        {
            public bool IsConnected { get; init; }
            public string ConnectionState { get; init; } = string.Empty;
            public DateTime ConnectionStateAtUtc { get; init; }
            public DateTime LastEventAtUtc { get; init; }
            public DateTime LastRefreshAtUtc { get; init; }
            public string LastEventType { get; init; } = string.Empty;
            public string LastForceRefreshReason { get; init; } = string.Empty;
            public DateTime LastPressureAlertAtUtc { get; init; }
            public bool PressureHintActive { get; init; }
            public string ReasonCountersSummary { get; init; } = string.Empty;
            public double LastEventLagMs { get; init; } = -1;
            public long EventsReceivedCount { get; init; }
            public long RefreshAppliedCount { get; init; }
            public long CoalescedEventsCount { get; init; }
            public long ThrottleDelayCount { get; init; }
            public int ReconnectCount { get; init; }
            public long PressureAlertCount { get; init; }
        }

        private void MergeStorageSnapshotIntoLocalHistory(IReadOnlyCollection<OrderData> storageOrders)
        {
            var storageByInternalId = new Dictionary<string, OrderData>(StringComparer.Ordinal);
            foreach (var storageOrder in storageOrders ?? Array.Empty<OrderData>())
            {
                if (storageOrder == null || string.IsNullOrWhiteSpace(storageOrder.InternalId))
                    continue;

                storageByInternalId[storageOrder.InternalId] = storageOrder;
            }

            foreach (var storageOrder in storageByInternalId.Values)
            {
                var localOrder = FindOrderByInternalId(storageOrder.InternalId);
                if (localOrder != null && _runTokensByOrder.ContainsKey(storageOrder.InternalId))
                {
                    _orderApplicationService.ApplyLanStatusSnapshot(localOrder, storageOrder);
                    _orderApplicationService.ApplyLanOrderItemVersionsSnapshot(localOrder, storageOrder);
                    continue;
                }

                if (localOrder != null)
                {
                    ApplyStorageOrderSnapshot(localOrder, storageOrder);
                    continue;
                }

                _orderHistory.Add(CloneStorageOrder(storageOrder));
            }

            for (var index = _orderHistory.Count - 1; index >= 0; index--)
            {
                var localOrder = _orderHistory[index];
                if (localOrder == null || string.IsNullOrWhiteSpace(localOrder.InternalId))
                {
                    _orderHistory.RemoveAt(index);
                    continue;
                }

                if (storageByInternalId.ContainsKey(localOrder.InternalId))
                    continue;
                if (_runTokensByOrder.ContainsKey(localOrder.InternalId))
                    continue;

                _expandedOrderIds.Remove(localOrder.InternalId);
                _orderHistory.RemoveAt(index);
            }

            var activeOrderIds = _orderHistory
                .Where(order => order != null && !string.IsNullOrWhiteSpace(order.InternalId))
                .Select(order => order.InternalId)
                .ToHashSet(StringComparer.Ordinal);
            _expandedOrderIds.RemoveWhere(orderInternalId => !activeOrderIds.Contains(orderInternalId));

            foreach (var key in _runProgressByOrderInternalId.Keys.ToList())
            {
                if (activeOrderIds.Contains(key))
                    continue;

                _runProgressByOrderInternalId.Remove(key);
            }
        }

        private static void ApplyStorageOrderSnapshot(OrderData localOrder, OrderData storageOrder)
        {
            if (localOrder == null || storageOrder == null)
                return;

            localOrder.InternalId = string.IsNullOrWhiteSpace(storageOrder.InternalId)
                ? localOrder.InternalId
                : storageOrder.InternalId;
            localOrder.StorageVersion = storageOrder.StorageVersion;
            localOrder.Id = storageOrder.Id ?? string.Empty;
            localOrder.StartMode = storageOrder.StartMode;
            localOrder.FileTopologyMarker = storageOrder.FileTopologyMarker;
            localOrder.Keyword = storageOrder.Keyword ?? string.Empty;
            localOrder.UserName = storageOrder.UserName ?? string.Empty;
            localOrder.ArrivalDate = storageOrder.ArrivalDate;
            localOrder.OrderDate = storageOrder.OrderDate;
            localOrder.FolderName = storageOrder.FolderName ?? string.Empty;
            localOrder.Status = storageOrder.Status ?? string.Empty;
            localOrder.SourcePath = storageOrder.SourcePath ?? string.Empty;
            localOrder.SourceFileSizeBytes = storageOrder.SourceFileSizeBytes;
            localOrder.SourceFileHash = storageOrder.SourceFileHash ?? string.Empty;
            localOrder.PreparedPath = storageOrder.PreparedPath ?? string.Empty;
            localOrder.PreparedFileSizeBytes = storageOrder.PreparedFileSizeBytes;
            localOrder.PreparedFileHash = storageOrder.PreparedFileHash ?? string.Empty;
            localOrder.PrintPath = storageOrder.PrintPath ?? string.Empty;
            localOrder.PrintFileSizeBytes = storageOrder.PrintFileSizeBytes;
            localOrder.PrintFileHash = storageOrder.PrintFileHash ?? string.Empty;
            localOrder.PitStopAction = storageOrder.PitStopAction ?? "-";
            localOrder.ImposingAction = storageOrder.ImposingAction ?? "-";
            localOrder.LastStatusReason = storageOrder.LastStatusReason ?? string.Empty;
            localOrder.LastStatusSource = storageOrder.LastStatusSource ?? string.Empty;
            localOrder.LastStatusAt = storageOrder.LastStatusAt;

            ApplyStorageOrderItemsSnapshot(localOrder, storageOrder.Items);
        }

        private static void ApplyStorageOrderItemsSnapshot(OrderData localOrder, IReadOnlyCollection<OrderFileItem>? storageItems)
        {
            localOrder.Items ??= [];
            var localItemsById = localOrder.Items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ItemId))
                .ToDictionary(item => item.ItemId, item => item, StringComparer.Ordinal);

            var mergedItems = new List<OrderFileItem>();
            foreach (var storageItem in storageItems ?? Array.Empty<OrderFileItem>())
            {
                if (storageItem == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(storageItem.ItemId)
                    && localItemsById.TryGetValue(storageItem.ItemId, out var localItem))
                {
                    ApplyStorageOrderItemSnapshot(localItem, storageItem);
                    mergedItems.Add(localItem);
                    continue;
                }

                mergedItems.Add(CloneStorageOrderItem(storageItem));
            }

            localOrder.Items.Clear();
            localOrder.Items.AddRange(mergedItems);
        }

        private static void ApplyStorageOrderItemSnapshot(OrderFileItem localItem, OrderFileItem storageItem)
        {
            if (localItem == null || storageItem == null)
                return;

            localItem.ItemId = string.IsNullOrWhiteSpace(storageItem.ItemId)
                ? localItem.ItemId
                : storageItem.ItemId;
            localItem.StorageVersion = storageItem.StorageVersion;
            localItem.ClientFileLabel = storageItem.ClientFileLabel ?? string.Empty;
            localItem.Variant = storageItem.Variant ?? string.Empty;
            localItem.SourcePath = storageItem.SourcePath ?? string.Empty;
            localItem.SourceFileSizeBytes = storageItem.SourceFileSizeBytes;
            localItem.SourceFileHash = storageItem.SourceFileHash ?? string.Empty;
            localItem.PreparedPath = storageItem.PreparedPath ?? string.Empty;
            localItem.PreparedFileSizeBytes = storageItem.PreparedFileSizeBytes;
            localItem.PreparedFileHash = storageItem.PreparedFileHash ?? string.Empty;
            localItem.PrintPath = storageItem.PrintPath ?? string.Empty;
            localItem.PrintFileSizeBytes = storageItem.PrintFileSizeBytes;
            localItem.PrintFileHash = storageItem.PrintFileHash ?? string.Empty;
            localItem.TechnicalFiles = storageItem.TechnicalFiles == null
                ? []
                : storageItem.TechnicalFiles.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
            localItem.FileStatus = storageItem.FileStatus ?? string.Empty;
            localItem.LastReason = storageItem.LastReason ?? string.Empty;
            localItem.UpdatedAt = storageItem.UpdatedAt;
            localItem.PitStopAction = storageItem.PitStopAction ?? "-";
            localItem.ImposingAction = storageItem.ImposingAction ?? "-";
            localItem.SequenceNo = storageItem.SequenceNo;
        }

        private static OrderData CloneStorageOrder(OrderData storageOrder)
        {
            var clonedOrder = new OrderData();
            ApplyStorageOrderSnapshot(clonedOrder, storageOrder);
            if (string.IsNullOrWhiteSpace(clonedOrder.InternalId))
                clonedOrder.InternalId = Guid.NewGuid().ToString("N");
            return clonedOrder;
        }

        private static OrderFileItem CloneStorageOrderItem(OrderFileItem storageItem)
        {
            var clonedItem = new OrderFileItem();
            ApplyStorageOrderItemSnapshot(clonedItem, storageItem);
            if (string.IsNullOrWhiteSpace(clonedItem.ItemId))
                clonedItem.ItemId = Guid.NewGuid().ToString("N");
            return clonedItem;
        }
    }
}
