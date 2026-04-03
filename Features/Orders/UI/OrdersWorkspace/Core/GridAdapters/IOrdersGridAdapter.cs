using System.Collections.Generic;

namespace Replica
{
    internal interface IOrdersGridAdapter
    {
        string AdapterName { get; }
        string? GetCurrentSelectedTag();
        bool TryRestoreSelectedRowByTag(string selectedTag);
        IReadOnlyList<string> GetSelectedOrderInternalIds();
        IReadOnlyList<OrdersGridVisibleRowSnapshot> GetVisibleOrderRows();
        bool ApplySelectionByOrderInternalIds(ISet<string> selectedOrderInternalIds, string? preferredOrderInternalId, int preferredColumnIndex);
        void ClearSelection();
    }
}
