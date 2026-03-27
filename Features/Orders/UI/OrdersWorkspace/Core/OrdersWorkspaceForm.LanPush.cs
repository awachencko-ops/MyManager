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
                if (!TryLoadHistoryFromConfiguredRepository(out var storageOrders))
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
            if (Disposing || IsDisposed)
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
                    _lanPushLastForceRefreshReason = pushEvent.Reason.Trim();
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
            lock (_lanPushMetricsSync)
            {
                snapshot = new LanPushDiagnosticsSnapshot
                {
                    IsConnected = _lanPushConnected,
                    ConnectionState = _lanPushConnectionState,
                    ConnectionStateAtUtc = _lanPushConnectionStateAtUtc,
                    LastEventAtUtc = _lanPushLastEventAtUtc,
                    LastRefreshAtUtc = _lanPushLastRefreshAtUtc,
                    LastEventType = _lanPushLastEventType,
                    LastForceRefreshReason = _lanPushLastForceRefreshReason,
                    LastEventLagMs = _lanPushLastEventLagMs,
                    EventsReceivedCount = Interlocked.Read(ref _lanPushEventsReceivedCount),
                    RefreshAppliedCount = Interlocked.Read(ref _lanPushRefreshAppliedCount),
                    CoalescedEventsCount = Interlocked.Read(ref _lanPushCoalescedEventsCount),
                    ThrottleDelayCount = Interlocked.Read(ref _lanPushThrottleDelayCount),
                    ReconnectCount = Interlocked.CompareExchange(ref _lanPushReconnectCount, 0, 0)
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
            if (snapshot.CoalescedEventsCount > 0 || snapshot.ThrottleDelayCount > 0)
                lines.Add($"Push coalesced/throttled: {snapshot.CoalescedEventsCount}/{snapshot.ThrottleDelayCount}");
            if (!string.IsNullOrWhiteSpace(snapshot.LastForceRefreshReason))
                lines.Add($"Push last force-refresh reason: {snapshot.LastForceRefreshReason}");

            return lines;
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
            public double LastEventLagMs { get; init; } = -1;
            public long EventsReceivedCount { get; init; }
            public long RefreshAppliedCount { get; init; }
            public long CoalescedEventsCount { get; init; }
            public long ThrottleDelayCount { get; init; }
            public int ReconnectCount { get; init; }
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

                _orderApplicationService.UpsertOrderInHistory(_orderHistory, storageOrder);
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
    }
}
