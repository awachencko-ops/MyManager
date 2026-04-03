using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Replica
{
    internal sealed class DataGridViewOrdersGridAdapter : IOrdersGridAdapter
    {
        private readonly DataGridView _grid;
        private readonly Func<IEnumerable<OrderData>> _orderHistoryProvider;
        private readonly Func<int> _focusColumnIndexProvider;

        public DataGridViewOrdersGridAdapter(
            DataGridView grid,
            Func<IEnumerable<OrderData>> orderHistoryProvider,
            Func<int> focusColumnIndexProvider)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _orderHistoryProvider = orderHistoryProvider ?? throw new ArgumentNullException(nameof(orderHistoryProvider));
            _focusColumnIndexProvider = focusColumnIndexProvider ?? throw new ArgumentNullException(nameof(focusColumnIndexProvider));
        }

        public string AdapterName => "DataGridView";

        public string? GetCurrentSelectedTag()
        {
            return _grid.CurrentRow?.Tag?.ToString();
        }

        public bool TryRestoreSelectedRowByTag(string selectedTag)
        {
            if (string.IsNullOrWhiteSpace(selectedTag))
                return false;

            var before = _grid.CurrentRow?.Tag?.ToString();
            var focusColumnIndex = _focusColumnIndexProvider.Invoke();
            OrderGridLogic.TryRestoreSelectedRowByTag(_grid, focusColumnIndex, selectedTag);
            var after = _grid.CurrentRow?.Tag?.ToString();
            return string.Equals(after, selectedTag, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(before) && string.Equals(before, after, StringComparison.Ordinal));
        }

        public IReadOnlyList<string> GetSelectedOrderInternalIds()
        {
            var selectedOrders = OrderGridLogic.GetSelectedOrders(_grid, _orderHistoryProvider.Invoke());
            return selectedOrders
                .Where(order => order != null && !string.IsNullOrWhiteSpace(order.InternalId))
                .Select(order => order.InternalId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public IReadOnlyList<OrdersGridVisibleRowSnapshot> GetVisibleOrderRows()
        {
            var snapshots = new List<OrdersGridVisibleRowSnapshot>(_grid.Rows.Count);
            var orderNumberColumn = TryGetColumnByName("colOrderNumber");
            var printColumn = TryGetColumnByName("colPrint");
            var orderNumberColumnIndex = orderNumberColumn?.Index ?? -1;
            var printColumnIndex = printColumn?.Index ?? -1;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow || !row.Visible)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (string.IsNullOrWhiteSpace(rowTag))
                    continue;

                var orderNumber = orderNumberColumnIndex >= 0 && orderNumberColumnIndex < row.Cells.Count
                    ? row.Cells[orderNumberColumnIndex].Value?.ToString() ?? string.Empty
                    : string.Empty;
                var printDisplay = printColumnIndex >= 0 && printColumnIndex < row.Cells.Count
                    ? row.Cells[printColumnIndex].Value?.ToString() ?? string.Empty
                    : string.Empty;

                snapshots.Add(new OrdersGridVisibleRowSnapshot(rowTag, orderNumber, printDisplay));
            }

            return snapshots;
        }

        public bool ApplySelectionByOrderInternalIds(ISet<string> selectedOrderInternalIds, string? preferredOrderInternalId, int preferredColumnIndex)
        {
            if (selectedOrderInternalIds == null)
                throw new ArgumentNullException(nameof(selectedOrderInternalIds));

            _grid.ClearSelection();

            if (selectedOrderInternalIds.Count == 0)
            {
                _grid.CurrentCell = null;
                return false;
            }

            var targetColumnIndex = ResolveSelectionColumnIndex(preferredColumnIndex);
            if (targetColumnIndex < 0)
                return false;
            DataGridViewRow? firstSelectedRow = null;
            DataGridViewRow? preferredSelectedRow = null;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow || !row.Visible)
                    continue;

                var rowOrderInternalId = ExtractOrderInternalIdFromTag(row.Tag?.ToString());
                if (string.IsNullOrWhiteSpace(rowOrderInternalId))
                    continue;

                if (!selectedOrderInternalIds.Contains(rowOrderInternalId))
                    continue;

                row.Selected = true;
                firstSelectedRow ??= row;

                if (!string.IsNullOrWhiteSpace(preferredOrderInternalId)
                    && string.Equals(rowOrderInternalId, preferredOrderInternalId, StringComparison.Ordinal))
                {
                    preferredSelectedRow = row;
                }
            }

            var rowToFocus = preferredSelectedRow ?? firstSelectedRow;
            if (rowToFocus == null)
            {
                _grid.CurrentCell = null;
                return false;
            }

            _grid.CurrentCell = rowToFocus.Cells[targetColumnIndex];
            return true;
        }

        public void ClearSelection()
        {
            _grid.ClearSelection();
            _grid.CurrentCell = null;
        }

        private DataGridViewColumn? TryGetColumnByName(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
                return null;

            return _grid.Columns.Contains(columnName)
                ? _grid.Columns[columnName]
                : null;
        }

        private int ResolveSelectionColumnIndex(int preferredColumnIndex)
        {
            if (preferredColumnIndex >= 0 && preferredColumnIndex < _grid.ColumnCount)
                return preferredColumnIndex;

            var fallbackFromProvider = _focusColumnIndexProvider.Invoke();
            if (fallbackFromProvider >= 0 && fallbackFromProvider < _grid.ColumnCount)
                return fallbackFromProvider;

            return _grid.ColumnCount > 0 ? 0 : -1;
        }

        private static string? ExtractOrderInternalIdFromTag(string? rowTag)
        {
            if (string.IsNullOrWhiteSpace(rowTag))
                return null;

            var normalized = rowTag.Trim();
            if (!normalized.StartsWith("order:", StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parts = normalized.Split(':');
            if (parts.Length < 2)
                return null;

            return parts[1].Trim();
        }
    }
}
