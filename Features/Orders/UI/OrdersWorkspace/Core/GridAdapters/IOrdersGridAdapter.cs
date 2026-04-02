using System.Collections.Generic;

namespace Replica
{
    internal interface IOrdersGridAdapter
    {
        string AdapterName { get; }
        string? GetCurrentSelectedTag();
        bool TryRestoreSelectedRowByTag(string selectedTag);
        IReadOnlyList<string> GetSelectedOrderInternalIds();
    }
}
