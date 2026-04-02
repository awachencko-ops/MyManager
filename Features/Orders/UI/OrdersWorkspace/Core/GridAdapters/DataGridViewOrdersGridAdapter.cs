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
    }
}
