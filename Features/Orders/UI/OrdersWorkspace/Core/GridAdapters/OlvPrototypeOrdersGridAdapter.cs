using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Replica
{
    internal sealed class OlvPrototypeOrdersGridAdapter : IOrdersGridAdapter
    {
        private readonly DataGridViewOrdersGridAdapter _fallbackDataGridAdapter;

        public OlvPrototypeOrdersGridAdapter(
            DataGridView dataGrid,
            Func<IEnumerable<OrderData>> orderHistoryProvider,
            Func<int> focusColumnIndexProvider)
        {
            _fallbackDataGridAdapter = new DataGridViewOrdersGridAdapter(
                dataGrid,
                orderHistoryProvider,
                focusColumnIndexProvider);
        }

        public string AdapterName => "OLV-Prototype (fallback:DGV)";

        public string? GetCurrentSelectedTag()
        {
            return _fallbackDataGridAdapter.GetCurrentSelectedTag();
        }

        public bool TryRestoreSelectedRowByTag(string selectedTag)
        {
            return _fallbackDataGridAdapter.TryRestoreSelectedRowByTag(selectedTag);
        }

        public IReadOnlyList<string> GetSelectedOrderInternalIds()
        {
            return _fallbackDataGridAdapter.GetSelectedOrderInternalIds();
        }

        public IReadOnlyList<OrdersGridVisibleRowSnapshot> GetVisibleOrderRows()
        {
            return _fallbackDataGridAdapter.GetVisibleOrderRows();
        }
    }
}
