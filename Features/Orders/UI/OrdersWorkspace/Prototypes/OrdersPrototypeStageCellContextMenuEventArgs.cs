using System;
using System.Drawing;

namespace Replica
{
    internal sealed class OrdersPrototypeStageCellContextMenuEventArgs : EventArgs
    {
        public OrdersPrototypeStageCellContextMenuEventArgs(
            OrdersTreePrototypeNode node,
            int stage,
            int columnIndex,
            Point screenLocation)
        {
            Node = node;
            Stage = stage;
            ColumnIndex = columnIndex;
            ScreenLocation = screenLocation;
        }

        public OrdersTreePrototypeNode Node { get; }
        public int Stage { get; }
        public int ColumnIndex { get; }
        public Point ScreenLocation { get; }
    }
}
