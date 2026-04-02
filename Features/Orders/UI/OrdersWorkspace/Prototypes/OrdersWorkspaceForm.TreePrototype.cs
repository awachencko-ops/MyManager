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
            using var prototypeForm = new OrdersTreePrototypeForm(roots, BuildOrdersTreePrototypeSnapshot);
            prototypeForm.StageCellClick += PrototypeForm_StageCellClick;
            prototypeForm.ShowDialog(this);
        }

        private async void PrototypeForm_StageCellClick(object? sender, OrdersPrototypeStageCellClickEventArgs e)
        {
            if (e == null || e.Node == null || !OrderStages.IsFileStage(e.Stage))
                return;

            if (!EnsureServerWriteAllowed("Файловая операция"))
                return;

            var node = e.Node;
            if (string.IsNullOrWhiteSpace(node.OrderInternalId))
                return;

            var order = FindOrderByInternalId(node.OrderInternalId);
            if (order == null)
            {
                SetBottomStatus("Не удалось найти заказ для операции в OLV прототипе");
                return;
            }

            try
            {
                if (OrderGridLogic.IsItemTag(node.RowTag))
                {
                    var itemId = node.ItemId;
                    if (string.IsNullOrWhiteSpace(itemId))
                        return;

                    var item = order.Items?.FirstOrDefault(x => x != null && string.Equals(x.ItemId, itemId, StringComparison.Ordinal));
                    if (item == null)
                    {
                        SetBottomStatus("Item не найден для выбранной строки OLV");
                        return;
                    }

                    var itemPath = GetItemStagePath(item, e.Stage);
                    if (HasExistingFile(itemPath))
                        OpenFileDefault(itemPath);
                    else
                        await PickAndCopyFileForItemAsync(order, item, e.Stage);
                }
                else
                {
                    if (OrderTopologyService.IsMultiOrder(order))
                    {
                        SetBottomStatus("В group-order у контейнера файлы заполняются только в строках item");
                        return;
                    }

                    var orderPath = ResolveSingleOrderDisplayPath(order, e.Stage);
                    if (HasExistingFile(orderPath))
                        OpenFileDefault(orderPath);
                    else
                        await PickAndCopyFileForOrderAsync(order, e.Stage);
                }

                if (sender is OrdersTreePrototypeForm prototypeForm)
                    prototypeForm.RefreshFromSource();
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось обработать действие по файлу (OLV): {ex.Message}");
                MessageBox.Show(
                    this,
                    $"Не удалось обработать действие по файлу (OLV): {ex.Message}",
                    "OLV prototype",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
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
            var orderInternalId = (order.InternalId ?? string.Empty).Trim();
            var orderNumber = string.IsNullOrWhiteSpace(order.Id) ? "-" : order.Id.Trim();
            var rowTag = string.IsNullOrWhiteSpace(orderInternalId)
                ? string.Empty
                : OrderGridLogic.BuildOrderTag(orderInternalId);
            var orderStatus = ResolveOrderStatusForPrototype(order, isMultiOrder);
            var pitStopAction = NormalizeAction(order.PitStopAction);
            var imposingAction = NormalizeAction(order.ImposingAction);

            var sourceDisplay = "-";
            var preparedDisplay = "-";
            var printDisplay = "-";
            var sourcePath = string.Empty;
            var preparedPath = string.Empty;
            var printPath = string.Empty;

            if (!isMultiOrder)
            {
                sourcePath = ResolveSingleOrderDisplayPath(order, OrderStages.Source);
                preparedPath = ResolveSingleOrderDisplayPath(order, OrderStages.Prepared);
                printPath = ResolveSingleOrderDisplayPath(order, OrderStages.Print);

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
                children: children,
                rowTag: rowTag,
                orderInternalId: orderInternalId,
                itemId: null,
                orderNumber: orderNumber,
                receivedSortTicks: order.OrderDate.Ticks,
                createdSortTicks: order.ArrivalDate.Ticks,
                sourcePath: sourcePath,
                preparedPath: preparedPath,
                printPath: printPath);
        }

        private List<OrdersTreePrototypeNode> BuildPrototypeItemNodes(OrderData order)
        {
            var children = new List<OrdersTreePrototypeNode>();
            if (order.Items == null || order.Items.Count == 0)
                return children;

            var orderInternalId = (order.InternalId ?? string.Empty).Trim();
            var orderNumber = string.IsNullOrWhiteSpace(order.Id) ? "-" : order.Id.Trim();

            var orderedItems = order.Items
                .Where(item => item != null)
                .OrderBy(item => item.SequenceNo)
                .ToList();

            for (var index = 0; index < orderedItems.Count; index++)
            {
                var item = orderedItems[index];
                var itemId = (item.ItemId ?? string.Empty).Trim();
                var rowTag = string.IsNullOrWhiteSpace(orderInternalId) || string.IsNullOrWhiteSpace(itemId)
                    ? string.Empty
                    : OrderGridLogic.BuildItemTag(orderInternalId, itemId);
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
                    children: null,
                    rowTag: rowTag,
                    orderInternalId: orderInternalId,
                    itemId: itemId,
                    orderNumber: orderNumber,
                    receivedSortTicks: order.OrderDate.Ticks,
                    createdSortTicks: order.ArrivalDate.Ticks,
                    sourcePath: item.SourcePath,
                    preparedPath: item.PreparedPath,
                    printPath: item.PrintPath));
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
