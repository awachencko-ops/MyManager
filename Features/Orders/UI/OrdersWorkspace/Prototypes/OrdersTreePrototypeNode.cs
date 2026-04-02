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
            IReadOnlyList<OrdersTreePrototypeNode>? children)
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
                children: children);
        }
    }
}
