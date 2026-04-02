using System;
using System.Collections.Generic;

namespace Replica
{
    internal sealed class OrdersTreePrototypeNode
    {
        public OrdersTreePrototypeNode(
            string title,
            string status,
            string source,
            string prepared,
            string pitStop,
            string imposing,
            string print,
            string received,
            string created,
            bool isContainer,
            IReadOnlyList<OrdersTreePrototypeNode>? children,
            string? rowTag = null,
            string? orderInternalId = null,
            string? itemId = null,
            string? orderNumber = null,
            long receivedSortTicks = 0,
            long createdSortTicks = 0,
            string? sourcePath = null,
            string? preparedPath = null,
            string? printPath = null)
        {
            Title = title ?? string.Empty;
            Status = status ?? string.Empty;
            Source = source ?? string.Empty;
            Prepared = prepared ?? string.Empty;
            PitStop = pitStop ?? string.Empty;
            Imposing = imposing ?? string.Empty;
            Print = print ?? string.Empty;
            Received = received ?? string.Empty;
            Created = created ?? string.Empty;
            IsContainer = isContainer;
            Children = children ?? Array.Empty<OrdersTreePrototypeNode>();
            RowTag = rowTag ?? string.Empty;
            OrderInternalId = orderInternalId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            OrderNumber = orderNumber ?? string.Empty;
            ReceivedSortTicks = receivedSortTicks;
            CreatedSortTicks = createdSortTicks;
            SourcePath = sourcePath ?? string.Empty;
            PreparedPath = preparedPath ?? string.Empty;
            PrintPath = printPath ?? string.Empty;
        }

        public string Title { get; }
        public string Status { get; }
        public string Source { get; }
        public string Prepared { get; }
        public string PitStop { get; }
        public string Imposing { get; }
        public string Print { get; }
        public string Received { get; }
        public string Created { get; }
        public bool IsContainer { get; }
        public IReadOnlyList<OrdersTreePrototypeNode> Children { get; }
        public string RowTag { get; }
        public string OrderInternalId { get; }
        public string ItemId { get; }
        public string OrderNumber { get; }
        public long ReceivedSortTicks { get; }
        public long CreatedSortTicks { get; }
        public string SourcePath { get; }
        public string PreparedPath { get; }
        public string PrintPath { get; }
        public bool HasChildren => Children.Count > 0;

        public OrdersTreePrototypeNode WithChildren(IReadOnlyList<OrdersTreePrototypeNode> children)
        {
            return new OrdersTreePrototypeNode(
                title: Title,
                status: Status,
                source: Source,
                prepared: Prepared,
                pitStop: PitStop,
                imposing: Imposing,
                print: Print,
                received: Received,
                created: Created,
                isContainer: IsContainer,
                children: children,
                rowTag: RowTag,
                orderInternalId: OrderInternalId,
                itemId: ItemId,
                orderNumber: OrderNumber,
                receivedSortTicks: ReceivedSortTicks,
                createdSortTicks: CreatedSortTicks,
                sourcePath: SourcePath,
                preparedPath: PreparedPath,
                printPath: PrintPath);
        }
    }
}
