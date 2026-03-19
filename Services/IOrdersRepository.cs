using System.Collections.Generic;

namespace Replica
{
    public interface IOrdersRepository
    {
        string BackendName { get; }
        bool TryLoadAll(out List<OrderData> orders, out string error);
        bool TrySaveAll(IReadOnlyCollection<OrderData> orders, out string error);
    }
}
