using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private void InitializeTreePrototypeLauncher()
        {
            btnTreePrototype.Click -= BtnTreePrototype_Click;
            btnTreePrototype.Click += BtnTreePrototype_Click;
        }

        private void BtnTreePrototype_Click(object? sender, EventArgs e)
        {
            OpenOrdersTreePrototype();
        }

        private void OpenOrdersTreePrototype()
        {
            var roots = BuildOrdersTreePrototypeSnapshot();
            using var prototypeForm = new OrdersTreePrototypeForm(roots);
            prototypeForm.ShowDialog(this);
        }

        private List<OrdersTreePrototypeNode> BuildOrdersTreePrototypeSnapshot()
        {
            var roots = new List<OrdersTreePrototypeNode>(_orderHistory.Count);
            var searchText = (tbSearch.Text ?? string.Empty).Trim();

            var sortedOrders = _orderHistory
                .Where(order => order != null)
                .OrderByDescending(order => order.ArrivalDate)
                .ToList();

            foreach (var order in sortedOrders)
            {
                if (!string.IsNullOrWhiteSpace(searchText) && !OrderMatchesSearch(order, searchText))
                    continue;

                if (!MatchesActiveFiltersForPrototype(order))
                    continue;

                roots.Add(BuildPrototypeOrderNode(order));
            }

            return roots;
        }

        private OrdersTreePrototypeNode BuildPrototypeOrderNode(OrderData order)
        {
            var isMultiOrder = OrderTopologyService.IsMultiOrder(order);
            var orderStatus = ResolveOrderStatusForPrototype(order, isMultiOrder);
            var pitStopAction = NormalizeAction(order.PitStopAction);
            var imposingAction = NormalizeAction(order.ImposingAction);

            var sourceDisplay = "-";
            var preparedDisplay = "-";
            var printDisplay = "-";

            if (!isMultiOrder)
            {
                var sourcePath = ResolveSingleOrderDisplayPath(order, OrderStages.Source);
                var preparedPath = ResolveSingleOrderDisplayPath(order, OrderStages.Prepared);
                var printPath = ResolveSingleOrderDisplayPath(order, OrderStages.Print);

                sourceDisplay = GetFileName(sourcePath);
                preparedDisplay = GetFileName(preparedPath);
                printDisplay = GetFileName(printPath);
                pitStopAction = ResolveSingleOrderDisplayAction(order, x => x.PitStopAction, order.PitStopAction);
                imposingAction = ResolveSingleOrderDisplayAction(order, x => x.ImposingAction, order.ImposingAction);
            }

            var children = isMultiOrder
                ? BuildPrototypeItemNodes(order)
                : [];

            return new OrdersTreePrototypeNode(
                title: BuildPrototypeOrderTitle(order, isMultiOrder),
                status: orderStatus,
                source: sourceDisplay,
                prepared: preparedDisplay,
                pitStop: pitStopAction,
                imposing: imposingAction,
                print: printDisplay,
                received: FormatDate(order.OrderDate),
                created: FormatDate(order.ArrivalDate),
                isContainer: true,
                children: children);
        }

        private List<OrdersTreePrototypeNode> BuildPrototypeItemNodes(OrderData order)
        {
            var children = new List<OrdersTreePrototypeNode>();
            if (order.Items == null || order.Items.Count == 0)
                return children;

            var orderedItems = order.Items
                .Where(item => item != null)
                .OrderBy(item => item.SequenceNo)
                .ToList();

            for (var index = 0; index < orderedItems.Count; index++)
            {
                var item = orderedItems[index];
                var itemStatus = NormalizeStatus(item.FileStatus) ?? (item.FileStatus ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(itemStatus))
                    itemStatus = WorkflowStatusNames.Waiting;

                var pitStopAction = NormalizeAction(item.PitStopAction);
                if (string.Equals(pitStopAction, "-", StringComparison.Ordinal))
                    pitStopAction = NormalizeAction(order.PitStopAction);

                var imposingAction = NormalizeAction(item.ImposingAction);
                if (string.Equals(imposingAction, "-", StringComparison.Ordinal))
                    imposingAction = NormalizeAction(order.ImposingAction);

                children.Add(new OrdersTreePrototypeNode(
                    title: BuildPrototypeItemTitle(item, index),
                    status: itemStatus,
                    source: GetFileName(item.SourcePath),
                    prepared: GetFileName(item.PreparedPath),
                    pitStop: pitStopAction,
                    imposing: imposingAction,
                    print: GetFileName(item.PrintPath),
                    received: FormatDate(order.OrderDate),
                    created: FormatDate(order.ArrivalDate),
                    isContainer: false,
                    children: null));
            }

            return children;
        }

        private bool MatchesActiveFiltersForPrototype(OrderData order)
        {
            if (order == null)
                return false;

            var selectedQueueStatus = GetSelectedQueueStatusName();
            var queueFilterActive = !string.IsNullOrWhiteSpace(selectedQueueStatus)
                && !string.Equals(selectedQueueStatus, QueueStatusNames.AllJobs, StringComparison.Ordinal);

            var normalizedStatus = ResolveNormalizedStatusForPrototypeFilter(order);

            if (queueFilterActive && !MatchesQueueStatus(selectedQueueStatus, normalizedStatus))
                return false;

            if (_selectedFilterStatuses.Count > 0
                && (normalizedStatus == null || !_selectedFilterStatuses.Contains(normalizedStatus)))
            {
                return false;
            }

            if (_selectedFilterUsers.Count > 0)
            {
                var normalizedUserName = NormalizeOrderUserName(order.UserName);
                if (!_selectedFilterUsers.Contains(normalizedUserName))
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(_orderNumberFilterText))
            {
                var orderNumber = string.IsNullOrWhiteSpace(order.Id) ? string.Empty : order.Id.Trim();
                if (orderNumber.IndexOf(_orderNumberFilterText, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            if (_createdDateFilterKind != CreatedDateFilterKind.None
                && !MatchesCreatedDateFilter(FormatDate(order.ArrivalDate)))
            {
                return false;
            }

            if (_receivedDateFilterKind != CreatedDateFilterKind.None
                && !MatchesReceivedDateFilter(FormatDate(order.OrderDate)))
            {
                return false;
            }

            return true;
        }

        private static string BuildPrototypeItemTitle(OrderFileItem item, int index)
        {
            var candidate = (item.ClientFileLabel ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;

            candidate = Path.GetFileName(item.SourcePath ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;

            candidate = Path.GetFileName(item.PreparedPath ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;

            candidate = Path.GetFileName(item.PrintPath ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;

            return $"Файл {index + 1}";
        }

        private string BuildPrototypeOrderTitle(OrderData order, bool isMultiOrder)
        {
            _ = isMultiOrder;
            return string.IsNullOrWhiteSpace(order.Id) ? "-" : order.Id.Trim();
        }

        private static string ResolveOrderStatusForPrototype(OrderData order, bool isMultiOrder)
        {
            var normalizedStatus = NormalizeStatus(order.Status) ?? (order.Status ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedStatus))
                normalizedStatus = WorkflowStatusNames.Waiting;

            return isMultiOrder ? WorkflowStatusNames.Group : normalizedStatus;
        }

        private static string? ResolveNormalizedStatusForPrototypeFilter(OrderData order)
        {
            var isMultiOrder = OrderTopologyService.IsMultiOrder(order);
            var displayStatus = ResolveOrderStatusForPrototype(order, isMultiOrder);
            return NormalizeStatus(displayStatus);
        }
    }
}
