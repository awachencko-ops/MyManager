using System;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private void InitializeOrdersGridAdapter()
        {
            _useOlvOrdersGridFeatureFlag = ResolveUseOlvOrdersGridFeatureFlag();

            _ordersGridAdapter = OrdersGridAdapterFactory.Create(
                useOlvAdapter: _useOlvOrdersGridFeatureFlag,
                dataGrid: dgvJobs,
                orderHistoryProvider: () => _orderHistory,
                focusColumnIndexProvider: () =>
                    colStatus != null && colStatus.Index >= 0
                        ? colStatus.Index
                        : 0);

            if (_useOlvOrdersGridFeatureFlag)
            {
                SetBottomStatus(
                    "REPLICA_USE_OLV_GRID=1: активирован адаптер OLV-прототипа (рабочая таблица пока не заменена)");
            }
        }

        private static bool ResolveUseOlvOrdersGridFeatureFlag()
        {
            var raw = Environment.GetEnvironmentVariable("REPLICA_USE_OLV_GRID");
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var normalized = raw.Trim();
            return string.Equals(normalized, "1", StringComparison.Ordinal)
                || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase);
        }
    }
}
