using System;

namespace Replica
{
    internal sealed class OrdersPrototypeStageCellClickEventArgs : EventArgs
    {
        public OrdersPrototypeStageCellClickEventArgs(OrdersTreePrototypeNode node, int stage, int columnIndex)
        {
            Node = node;
            Stage = stage;
            ColumnIndex = columnIndex;
        }

        public OrdersTreePrototypeNode Node { get; }
        public int Stage { get; }
        public int ColumnIndex { get; }
    }
}
