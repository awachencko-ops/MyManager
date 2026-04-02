namespace Replica
{
    internal sealed class OrdersGridVisibleRowSnapshot
    {
        public OrdersGridVisibleRowSnapshot(string rowTag, string orderNumber, string printDisplayValue)
        {
            RowTag = rowTag ?? string.Empty;
            OrderNumber = orderNumber ?? string.Empty;
            PrintDisplayValue = printDisplayValue ?? string.Empty;
        }

        public string RowTag { get; }
        public string OrderNumber { get; }
        public string PrintDisplayValue { get; }
    }
}
