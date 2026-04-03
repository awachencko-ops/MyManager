using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Replica
{
    internal static class OrdersGridAdapterFactory
    {
        public static IOrdersGridAdapter Create(
            bool useOlvAdapter,
            DataGridView dataGrid,
            Func<IEnumerable<OrderData>> orderHistoryProvider,
            Func<int> focusColumnIndexProvider)
        {
            if (useOlvAdapter)
            {
                return new OlvPrototypeOrdersGridAdapter(
                    dataGrid,
                    orderHistoryProvider,
                    focusColumnIndexProvider);
            }

            return new DataGridViewOrdersGridAdapter(dataGrid, orderHistoryProvider, focusColumnIndexProvider);
        }
    }
}
