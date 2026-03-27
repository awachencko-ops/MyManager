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
                return;

            _lanOrderPushClient.EventReceived += LanOrderPushClient_EventReceived;
            _lanOrderPushClient.ConnectionStateChanged += LanOrderPushClient_ConnectionStateChanged;
            _ = StartLanOrderPushBridgeAsync();
        }

        private async Task StartLanOrderPushBridgeAsync()
        {
            try
            {
                await _lanOrderPushClient.StartAsync(_lanApiBaseUrl, ResolveLanApiActor());
            }
            catch (Exception ex)
            {
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

            lock (_lanPushRefreshSync)
            {
                _lanPushPendingEvent = pushEvent;
            }

            Interlocked.Exchange(ref _lanPushRefreshPending, 1);
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

                    await ApplyLanPushSnapshotRefreshOnceAsync(pushEvent);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"LAN-PUSH | refresh-loop-failed | {ex.Message}");
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
