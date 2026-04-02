using System;
using System.Collections.Generic;

namespace Replica
{
    internal sealed class OlvPrototypeOrdersGridAdapter : IOrdersGridAdapter
    {
        public string AdapterName => "OLV-Prototype";

        public string? GetCurrentSelectedTag()
        {
            return null;
        }

        public bool TryRestoreSelectedRowByTag(string selectedTag)
        {
            _ = selectedTag;
            return false;
        }

        public IReadOnlyList<string> GetSelectedOrderInternalIds()
        {
            return Array.Empty<string>();
        }

        public IReadOnlyList<OrdersGridVisibleRowSnapshot> GetVisibleOrderRows()
        {
            return Array.Empty<OrdersGridVisibleRowSnapshot>();
        }
    }
}
