using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private void InitializeTreePrototypeLauncher()
        {
            if (_useOlvOrdersGridFeatureFlag)
            {
                btnTreePrototype.Visible = false;
                EnsureEmbeddedOrdersTreePrototypeControl();
                RefreshEmbeddedOrdersTreePrototypeSnapshot();
                return;
            }

            btnTreePrototype.Visible = true;
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
            prototypeForm.StageCellContextMenuRequested += PrototypeForm_StageCellContextMenuRequested;
            prototypeForm.StageFileDropRequested += PrototypeForm_StageFileDropRequested;
            prototypeForm.ShowDialog(this);
        }

        private void EnsureEmbeddedOrdersTreePrototypeControl()
        {
            if (_embeddedOrdersTreePrototypeControl != null && !_embeddedOrdersTreePrototypeControl.IsDisposed)
                return;

            _embeddedOrdersTreePrototypeControl = new OrdersTreePrototypeControl(BuildOrdersTreePrototypeSnapshot())
            {
                Visible = false
            };
            _embeddedOrdersTreePrototypeControl.RefreshRequested += (_, _) => RefreshEmbeddedOrdersTreePrototypeSnapshot();
            _embeddedOrdersTreePrototypeControl.StageCellClick += PrototypeForm_StageCellClick;
            _embeddedOrdersTreePrototypeControl.StageCellContextMenuRequested += PrototypeForm_StageCellContextMenuRequested;
            _embeddedOrdersTreePrototypeControl.StageFileDropRequested += PrototypeForm_StageFileDropRequested;
            _embeddedOrdersTreePrototypeControl.SelectionRowTagChanged += EmbeddedOrdersTreePrototypeControl_SelectionRowTagChanged;
            pnlTable.Controls.Add(_embeddedOrdersTreePrototypeControl);
        }

        private bool IsEmbeddedOrdersTreePrototypeEnabled()
        {
            return _useOlvOrdersGridFeatureFlag
                && _embeddedOrdersTreePrototypeControl != null
                && !_embeddedOrdersTreePrototypeControl.IsDisposed;
        }

        private void RefreshEmbeddedOrdersTreePrototypeSnapshot()
        {
            if (!IsEmbeddedOrdersTreePrototypeEnabled())
                return;

            _embeddedOrdersTreePrototypeControl!.LoadRoots(BuildOrdersTreePrototypeSnapshot());
        }

        private void RefreshPrototypeSurfaceFromSender(object? sender)
        {
            if (sender is OrdersTreePrototypeForm prototypeForm)
            {
                prototypeForm.RefreshFromSource();
                return;
            }

            if (sender is OrdersTreePrototypeControl prototypeControl)
            {
                prototypeControl.LoadRoots(BuildOrdersTreePrototypeSnapshot());
                return;
            }

            RefreshEmbeddedOrdersTreePrototypeSnapshot();
        }

        private void EmbeddedOrdersTreePrototypeControl_SelectionRowTagChanged(object? sender, string? selectedRowTag)
        {
            if (!_useOlvOrdersGridFeatureFlag)
                return;

            if (string.IsNullOrWhiteSpace(selectedRowTag))
            {
                _ordersGridAdapter?.ClearSelection();
                return;
            }

            _ordersGridAdapter?.TryRestoreSelectedRowByTag(selectedRowTag);
        }

        private async void PrototypeForm_StageCellClick(object? sender, OrdersPrototypeStageCellClickEventArgs e)
        {
            if (e == null || e.Node == null || !OrderStages.IsFileStage(e.Stage))
                return;

            if (!EnsureServerWriteAllowed("Файловая операция"))
                return;

            var node = e.Node;

            try
            {
                await OpenOrPickPrototypeStageFileAsync(node, e.Stage);
                RefreshPrototypeSurfaceFromSender(sender);
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

        private async void PrototypeForm_StageFileDropRequested(object? sender, OrdersPrototypeStageFileDropEventArgs e)
        {
            if (e == null || e.Node == null || !OrderStages.IsFileStage(e.Stage))
                return;

            if (!EnsureServerWriteAllowed("Drag&Drop"))
                return;

            var sourceFile = CleanPath(e.SourceFilePath);
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
                return;

            try
            {
                await AddPrototypeFileToStageAsync(e.Node, e.Stage, sourceFile);
                RefreshPrototypeSurfaceFromSender(sender);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось добавить файл в OLV: {ex.Message}");
                MessageBox.Show(
                    this,
                    $"Не удалось добавить файл в OLV: {ex.Message}",
                    "OLV prototype",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void PrototypeForm_StageCellContextMenuRequested(object? sender, OrdersPrototypeStageCellContextMenuEventArgs e)
        {
            if (e == null || e.Node == null || !OrderStages.IsFileStage(e.Stage))
                return;

            if (!TryResolvePrototypeNodeContext(e.Node, out var order, out var item))
                return;

            var menu = new ContextMenuStrip();
            menu.Closed += (_, _) => menu.Dispose();

            var currentPath = item != null
                ? GetItemStagePath(item, e.Stage)
                : ResolveSingleOrderDisplayPath(order, e.Stage);
            var hasFile = HasExistingFile(currentPath);
            var isGroupContainer = item == null && OrderTopologyService.IsMultiOrder(order);

            AddPrototypeMenuItem(
                menu,
                "Открыть файл",
                hasFile && !isGroupContainer ? () => OpenFileDefault(currentPath) : null,
                hasFile && !isGroupContainer,
                iconFolder: "file export",
                iconHint: "file_export");

            AddPrototypeMenuItem(
                menu,
                "Открыть папку этапа",
                () =>
                {
                    if (item != null)
                        OpenOrderStageFolder(order, item, e.Stage);
                    else
                        OpenOrderStageFolder(order, e.Stage);
                },
                enabled: true,
                iconFolder: "folder open",
                iconHint: "folder_open");

            AddPrototypeMenuItem(
                menu,
                hasFile ? "Заменить файл..." : "Добавить файл...",
                isGroupContainer
                    ? null
                    : async () =>
                    {
                        await OpenOrPickPrototypeStageFileAsync(e.Node, e.Stage);
                        RefreshPrototypeSurfaceFromSender(sender);
                    },
                enabled: !isGroupContainer,
                iconFolder: "addfile",
                iconHint: "attach_file_add");

            AddPrototypeMenuItem(
                menu,
                "Удалить файл",
                hasFile && !isGroupContainer
                    ? () =>
                    {
                        if (item != null)
                            RemoveFileFromItem(order, item, e.Stage);
                        else
                            RemoveFileFromOrder(order, e.Stage);
                        RefreshPrototypeSurfaceFromSender(sender);
                    }
                    : null,
                hasFile && !isGroupContainer,
                iconFolder: "delete",
                iconHint: "delete");

            AddPrototypeMenuItem(
                menu,
                "Переименовать файл",
                hasFile && !isGroupContainer
                    ? () =>
                    {
                        if (item != null)
                            RenameFileForItem(order, item, e.Stage);
                        else
                            RenameFileForOrder(order, e.Stage);
                        RefreshPrototypeSurfaceFromSender(sender);
                    }
                    : null,
                hasFile && !isGroupContainer,
                iconFolder: "files",
                iconHint: "files");

            AddPrototypeMenuItem(
                menu,
                "Копировать путь",
                !string.IsNullOrWhiteSpace(currentPath)
                    ? () => CopyExistingPathToClipboard(currentPath)
                    : null,
                !string.IsNullOrWhiteSpace(currentPath),
                iconFolder: "move to inbox",
                iconHint: "move_to_inbox");

            AddPrototypeMenuItem(
                menu,
                "Вставить из буфера",
                isGroupContainer
                    ? null
                    : async () =>
                    {
                        if (item != null)
                            await PasteFileFromClipboardAsync(order, item, e.Stage);
                        else
                            await PasteFileFromClipboardAsync(order, e.Stage);
                        RefreshPrototypeSurfaceFromSender(sender);
                    },
                enabled: !isGroupContainer,
                iconFolder: "addbox",
                iconHint: "add_box");

            if (menu.Items.Count == 0)
                return;

            menu.Show(e.ScreenLocation);
        }

        private static void AddPrototypeMenuItem(
            ContextMenuStrip menu,
            string text,
            Action? action,
            bool enabled,
            string? iconFolder = null,
            string? iconHint = null)
        {
            var item = new ToolStripMenuItem(text)
            {
                Enabled = enabled
            };

            if (!string.IsNullOrWhiteSpace(iconFolder))
            {
                var icon = LoadStatusCellIcon(iconFolder, iconHint ?? string.Empty);
                if (icon != null)
                    item.Image = icon;
            }

            if (enabled && action != null)
                item.Click += (_, _) => action();

            menu.Items.Add(item);
        }

        private async Task OpenOrPickPrototypeStageFileAsync(OrdersTreePrototypeNode node, int stage)
        {
            if (!TryResolvePrototypeNodeContext(node, out var order, out var item))
                return;

            if (item == null && OrderTopologyService.IsMultiOrder(order))
            {
                SetBottomStatus("В group-order у контейнера файлы заполняются только в строках item");
                return;
            }

            if (item != null)
            {
                var itemPath = GetItemStagePath(item, stage);
                if (HasExistingFile(itemPath))
                {
                    OpenFileDefault(itemPath);
                    return;
                }

                await PickAndCopyFileForItemAsync(order, item, stage);
                return;
            }

            var orderPath = ResolveSingleOrderDisplayPath(order, stage);
            if (HasExistingFile(orderPath))
            {
                OpenFileDefault(orderPath);
                return;
            }

            await PickAndCopyFileForOrderAsync(order, stage);
        }

        private async Task AddPrototypeFileToStageAsync(OrdersTreePrototypeNode node, int stage, string sourceFile)
        {
            if (!TryResolvePrototypeNodeContext(node, out var order, out var item))
                return;

            if (item != null)
            {
                await AddFileToItemAsync(order, item, sourceFile, stage);
                return;
            }

            if (OrderTopologyService.IsMultiOrder(order))
            {
                SetBottomStatus("В group-order добавляйте файлы в строки item");
                return;
            }

            await AddFileToOrderAsync(order, sourceFile, stage);
        }

        private bool TryResolvePrototypeNodeContext(OrdersTreePrototypeNode node, out OrderData order, out OrderFileItem? item)
        {
            order = null!;
            item = null;
            if (node == null || string.IsNullOrWhiteSpace(node.OrderInternalId))
                return false;

            var resolvedOrder = FindOrderByInternalId(node.OrderInternalId);
            if (resolvedOrder == null)
            {
                SetBottomStatus("Не удалось найти заказ для операции в OLV прототипе");
                return false;
            }

            if (OrderGridLogic.IsItemTag(node.RowTag))
            {
                if (string.IsNullOrWhiteSpace(node.ItemId))
                {
                    SetBottomStatus("ItemId отсутствует в строке OLV");
                    return false;
                }

                var resolvedItem = resolvedOrder.Items?
                    .FirstOrDefault(x => x != null && string.Equals(x.ItemId, node.ItemId, StringComparison.Ordinal));
                if (resolvedItem == null)
                {
                    SetBottomStatus("Item не найден для выбранной строки OLV");
                    return false;
                }

                item = resolvedItem;
            }

            order = resolvedOrder;
            return true;
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
