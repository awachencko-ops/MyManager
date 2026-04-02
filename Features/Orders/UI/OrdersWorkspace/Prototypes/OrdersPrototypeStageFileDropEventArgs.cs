using System;

namespace Replica
{
    internal sealed class OrdersPrototypeStageFileDropEventArgs : EventArgs
    {
        public OrdersPrototypeStageFileDropEventArgs(
            OrdersTreePrototypeNode node,
            int stage,
            string sourceFilePath,
            string sourceRowTag,
            int sourceStage)
        {
            Node = node;
            Stage = stage;
            SourceFilePath = sourceFilePath ?? string.Empty;
            SourceRowTag = sourceRowTag ?? string.Empty;
            SourceStage = sourceStage;
        }

        public OrdersTreePrototypeNode Node { get; }
        public int Stage { get; }
        public string SourceFilePath { get; }
        public string SourceRowTag { get; }
        public int SourceStage { get; }
    }
}
