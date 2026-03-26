using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PdfiumViewer;
using Svg;

namespace Replica
{
    public partial class OrdersWorkspaceForm : Form
    {
        public OrdersWorkspaceForm()
            : this(new FileSettingsProvider(), OrdersWorkspaceCompositionRoot.CreateRuntimeServices())
        {
        }

        internal OrdersWorkspaceForm(ISettingsProvider settingsProvider)
            : this(settingsProvider, OrdersWorkspaceCompositionRoot.CreateRuntimeServices())
        {
        }

        internal OrdersWorkspaceForm(ISettingsProvider settingsProvider, OrdersWorkspaceRuntimeServices runtimeServices)
        {
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            var services = runtimeServices ?? throw new ArgumentNullException(nameof(runtimeServices));
            _orderApplicationService = services.OrderApplicationService;
            _lanApiIdentityService = services.LanApiIdentityService;
            InitializeComponent();
            InitializeDockSidebar();
            InitializeStatusCellVisuals();
            InitializeUserProfilePanel();
            LoadSettings();
            InitializeUsersDirectory();
            InitializeProcessor();
            ApplyQueueVisualStyle();
            InitializeStatusFilter();
            InitializeOrderNoSearch();
            InitializeUserFilter();
            InitializeCreatedDateFilter();
            InitializeReceivedDateFilter();
            InitializeQueueNavigation();
            InitializeOrdersGridVisuals();
            InitializeOrdersTilesView();
            InitializeOrdersViewScrollBar();
            InitializeViewModeSwitches();
            InitializeOrdersDataFlow();
            InitializeOrderRowContextMenu();
            InitializeActionButtonsState();
            InitializeOrdersKeyboardShortcuts();
            InitializeTrayIndicators();
            InitializeServerHardLockUi();
            FormClosed += MainForm_FormClosed;
            SetOrdersViewMode(OrdersViewMode.List);
            SetBottomStatus(DefaultTrayStatusText);
        }

        // обработчик нажатия кнопок в ToolStrip
        private async void TsMainActions_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == tsbNewJob)
            {
                if (!EnsureServerWriteAllowed("Создание заказа"))
                    return;
                await CreateNewOrderAsync();
                return;
            }

            if (e.ClickedItem == tsbAddFile)
            {
                if (!EnsureServerWriteAllowed("Добавление файла"))
                    return;
                await AddFileToSelectedOrderAsync();
                return;
            }

            if (e.ClickedItem == toolStripButton1)
            {
                ShowSettingsDialog();
                return;
            }

            if (e.ClickedItem == tsbRun)
            {
                if (!EnsureServerWriteAllowed("Запуск заказа"))
                    return;
                await RunSelectedOrderAsync();
                return;
            }

            if (e.ClickedItem == tsbStop)
            {
                if (!EnsureServerWriteAllowed("Остановка заказа"))
                    return;
                await StopSelectedOrderAsync();
                return;
            }

            if (e.ClickedItem == tsbRemove)
            {
                if (!EnsureServerWriteAllowed("Удаление заказа"))
                    return;
                await RemoveSelectedOrderAsync();
                return;
            }

            if (e.ClickedItem == tsbConsole)
            {
                OpenLogForSelectionOrManager();
                return;
            }

            if (e.ClickedItem == tsbBrowse)
            {
                OpenFolderForSelectedOrder();
            }
        }

        private async Task CreateNewOrderAsync()
        {
            if (!EnsureServerWriteAllowed("Создание заказа"))
                return;

            var settings = _settingsProvider.Load();
            if (settings.UseExtendedMode)
                await CreateNewExtendedOrderAsync();
            else
                await CreateNewSimpleOrderAsync();
        }

        private async Task CreateNewSimpleOrderAsync()
        {
            using var form = new SimpleOrderForm();
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            var order = new OrderData
            {
                Id = form.OrderNumber.Trim(),
                StartMode = OrderStartMode.Simple,
                Keyword = string.Empty,
                ArrivalDate = DateTime.Now,
                OrderDate = form.OrderDate,
                FolderName = string.Empty,
                Status = WorkflowStatusNames.Waiting,
                PitStopAction = "-",
                ImposingAction = "-"
            };

            await AddCreatedOrderAsync(order);
        }

        private async Task CreateNewExtendedOrderAsync()
        {
            using var form = new OrderForm(_ordersRootPath);
            if (form.ShowDialog(this) != DialogResult.OK || form.ResultOrder == null)
                return;

            await AddCreatedOrderAsync(form.ResultOrder);
        }

        private async Task AddCreatedOrderAsync(OrderData order)
        {
            if (order == null)
                return;

            if (!EnsureServerWriteAllowed("Создание заказа"))
                return;

            if (ShouldUseLanRunApi())
            {
                var writeResult = _orderApplicationService
                    .TryCreateOrderViaLanApiAsync(
                        order,
                        _lanApiBaseUrl,
                        ResolveLanApiActor(),
                        NormalizeOrderUserName);
                var writeResultValue = await writeResult;

                if (!writeResultValue.IsSuccess || writeResultValue.Order == null)
                {
                    ShowLanWriteError(writeResultValue, "Создание заказа");
                    return;
                }

                UpsertOrderInHistory(writeResultValue.Order);
                TryRefreshRepositorySnapshotFromStorage(_orderHistory, "lan-api-create-order");
                RebuildOrdersGrid();
                TryRestoreSelectedRowByTag(OrderGridLogic.BuildOrderTag(writeResultValue.Order.InternalId));
                return;
            }

            var orderInternalId = _orderApplicationService.AddCreatedOrder(
                _orderHistory,
                order,
                NormalizeOrderUserName);
            SaveHistory();
            RebuildOrdersGrid();
            TryRestoreSelectedRowByTag(OrderGridLogic.BuildOrderTag(orderInternalId));
        }

        // Backward-compatible sync entry point used by legacy reflective smoke harness.
        private void AddCreatedOrder(OrderData order)
        {
            AddCreatedOrderAsync(order).GetAwaiter().GetResult();
        }

        private void EditOrderFromGrid(int rowIndex)
        {
            if (!EnsureServerWriteAllowed("Редактирование заказа"))
                return;

            if (rowIndex < 0 || rowIndex >= dgvJobs.Rows.Count)
                return;

            var rowTag = dgvJobs.Rows[rowIndex].Tag?.ToString();
            if (!IsOrderTag(rowTag))
                return;

            var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return;

            var order = FindOrderByInternalId(orderInternalId);
            if (order == null)
                return;

            var settings = _settingsProvider.Load();
            if (settings.UseExtendedMode)
                EditOrderExtended(order);
            else
                EditOrderSimple(order);
        }

        private void EditOrderSimple(OrderData order)
        {
            using var form = new SimpleOrderForm(order);
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            if (ShouldUseLanRunApi())
            {
                var updatedOrder = new OrderData
                {
                    Id = form.OrderNumber?.Trim() ?? string.Empty,
                    OrderDate = form.OrderDate,
                    UserName = order.UserName,
                    Status = order.Status,
                    Keyword = order.Keyword,
                    FolderName = order.FolderName,
                    PitStopAction = order.PitStopAction,
                    ImposingAction = order.ImposingAction
                };

                var writeResult = _orderApplicationService
                    .TryUpdateOrderViaLanApiAsync(
                        order,
                        updatedOrder,
                        _lanApiBaseUrl,
                        ResolveLanApiActor(),
                        NormalizeOrderUserName)
                    .GetAwaiter()
                    .GetResult();

                if (!TryApplyLanOrderUpdateResult(order, writeResult, "Редактирование заказа"))
                    return;

                RebuildOrdersGrid();
                TryRestoreSelectedRowByTag(OrderGridLogic.BuildOrderTag(order.InternalId));
                return;
            }

            _orderApplicationService.ApplySimpleEdit(order, form.OrderNumber, form.OrderDate);

            SaveHistory();
            RebuildOrdersGrid();
            TryRestoreSelectedRowByTag(OrderGridLogic.BuildOrderTag(order.InternalId));
        }

        private void EditOrderExtended(OrderData order)
        {
            using var form = new OrderForm(_ordersRootPath, order);
            if (form.ShowDialog(this) != DialogResult.OK || form.ResultOrder == null)
                return;

            if (ShouldUseLanRunApi())
            {
                var writeResult = _orderApplicationService
                    .TryUpdateOrderViaLanApiAsync(
                        order,
                        form.ResultOrder,
                        _lanApiBaseUrl,
                        ResolveLanApiActor(),
                        NormalizeOrderUserName)
                    .GetAwaiter()
                    .GetResult();

                if (!TryApplyLanOrderUpdateResult(order, writeResult, "Редактирование заказа"))
                    return;

                RebuildOrdersGrid();
                TryRestoreSelectedRowByTag(OrderGridLogic.BuildOrderTag(order.InternalId));
                return;
            }

            _orderApplicationService.ApplyExtendedEdit(order, form.ResultOrder);

            SaveHistory();
            RebuildOrdersGrid();
            TryRestoreSelectedRowByTag(OrderGridLogic.BuildOrderTag(order.InternalId));
        }

        private bool TryApplyLanOrderUpdateResult(OrderData targetOrder, LanOrderWriteCommandResult writeResult, string operationCaption)
        {
            if (targetOrder == null)
                return false;

            if (writeResult.IsSuccess && writeResult.Order != null)
            {
                if (string.Equals(targetOrder.InternalId, writeResult.Order.InternalId, StringComparison.Ordinal))
                    UpsertOrderInHistory(writeResult.Order);

                TryRefreshRepositorySnapshotFromStorage(_orderHistory, "lan-api-update-order");
                return true;
            }

            if (writeResult.CurrentVersion > 0)
                targetOrder.StorageVersion = writeResult.CurrentVersion;

            ShowLanWriteError(writeResult, operationCaption);
            return false;
        }

        private bool TryPersistOrderStatusViaLanApi(OrderData order, string source, string reason)
        {
            if (order == null || !ShouldUseLanRunApi())
                return false;

            var statusUpdateModel = new OrderData
            {
                Id = order.Id,
                OrderDate = order.OrderDate,
                UserName = order.UserName,
                Status = order.Status,
                Keyword = order.Keyword,
                FolderName = order.FolderName,
                PitStopAction = order.PitStopAction,
                ImposingAction = order.ImposingAction
            };

            var writeResult = _orderApplicationService
                .TryUpdateOrderViaLanApiAsync(
                    order,
                    statusUpdateModel,
                    _lanApiBaseUrl,
                    ResolveLanApiActor(),
                    NormalizeOrderUserName)
                .GetAwaiter()
                .GetResult();

            if (writeResult.IsSuccess && writeResult.Order != null)
            {
                ApplyLanStatusSnapshot(order, writeResult.Order);
                TryRefreshRepositorySnapshotFromStorage(_orderHistory, "lan-api-status-update");
                return true;
            }

            if (writeResult.CurrentVersion > 0)
                order.StorageVersion = writeResult.CurrentVersion;

            var errorText = string.IsNullOrWhiteSpace(writeResult.Error)
                ? "LAN status update failed"
                : writeResult.Error;
            Logger.Warn(
                $"LAN-API | status-update-failed | order={GetOrderDisplayId(order)} | source={source} | reason={reason} | conflict={(writeResult.IsConflict ? "1" : "0")} | unavailable={(writeResult.IsUnavailable ? "1" : "0")} | {errorText}");
            return false;
        }

        private static void ApplyLanStatusSnapshot(OrderData localOrder, OrderData serverOrder)
        {
            if (localOrder == null || serverOrder == null)
                return;

            if (serverOrder.StorageVersion > 0)
                localOrder.StorageVersion = serverOrder.StorageVersion;
            if (!string.IsNullOrWhiteSpace(serverOrder.Status))
                localOrder.Status = serverOrder.Status.Trim();
            if (!string.IsNullOrWhiteSpace(serverOrder.LastStatusSource))
                localOrder.LastStatusSource = serverOrder.LastStatusSource.Trim();
            if (!string.IsNullOrWhiteSpace(serverOrder.LastStatusReason))
                localOrder.LastStatusReason = serverOrder.LastStatusReason.Trim();
            if (serverOrder.LastStatusAt != default)
                localOrder.LastStatusAt = serverOrder.LastStatusAt;
        }

        private void TrySyncLanItemReorderForOrders(IEnumerable<OrderData> orders, string reason)
        {
            if (!ShouldUseLanRunApi() || orders == null)
                return;

            var normalizedOrders = orders
                .Where(order => order != null && !string.IsNullOrWhiteSpace(order.InternalId))
                .GroupBy(order => order.InternalId, StringComparer.Ordinal)
                .Select(group => group.First())
                .Where(order => (order.Items?.Count ?? 0) > 1)
                .ToList();
            if (normalizedOrders.Count == 0)
                return;

            foreach (var order in normalizedOrders)
            {
                var reorderResult = _orderApplicationService
                    .TryReorderOrderItemsViaLanApiAsync(
                        order,
                        _lanApiBaseUrl,
                        ResolveLanApiActor())
                    .GetAwaiter()
                    .GetResult();

                if (reorderResult.IsSuccess && reorderResult.Order != null)
                {
                    UpsertOrderInHistory(reorderResult.Order);
                    continue;
                }

                if (reorderResult.CurrentVersion > 0)
                    order.StorageVersion = reorderResult.CurrentVersion;

                var errorText = string.IsNullOrWhiteSpace(reorderResult.Error)
                    ? "LAN reorder failed"
                    : reorderResult.Error;
                Logger.Warn(
                    $"LAN-API | item-reorder-sync-failed | reason={reason} | order={GetOrderDisplayId(order)} | conflict={(reorderResult.IsConflict ? "1" : "0")} | unavailable={(reorderResult.IsUnavailable ? "1" : "0")} | {errorText}");
            }

            TryRefreshRepositorySnapshotFromStorage(_orderHistory, $"lan-api-item-reorder-{reason}");
        }

        private void TrySyncLanOrderItemUpsert(OrderData order, OrderFileItem item, string reason)
        {
            if (!ShouldUseLanRunApi() || order == null || item == null)
                return;

            var upsertResult = _orderApplicationService
                .TryUpsertOrderItemViaLanApiAsync(
                    order,
                    item,
                    _lanApiBaseUrl,
                    ResolveLanApiActor())
                .GetAwaiter()
                .GetResult();

            if (upsertResult.IsSuccess && upsertResult.Order != null)
            {
                ApplyLanOrderItemVersionsSnapshot(order, upsertResult.Order);
                TryRefreshRepositorySnapshotFromStorage(_orderHistory, $"lan-api-item-upsert-{reason}");
                return;
            }

            if (upsertResult.CurrentVersion > 0)
                order.StorageVersion = upsertResult.CurrentVersion;

            var errorText = string.IsNullOrWhiteSpace(upsertResult.Error)
                ? "LAN item upsert failed"
                : upsertResult.Error;
            Logger.Warn(
                $"LAN-API | item-upsert-sync-failed | reason={reason} | order={GetOrderDisplayId(order)} | item={item.ItemId} | conflict={(upsertResult.IsConflict ? "1" : "0")} | unavailable={(upsertResult.IsUnavailable ? "1" : "0")} | {errorText}");
            TryRefreshRepositorySnapshotFromStorage(_orderHistory, $"lan-api-item-upsert-failed-{reason}");
        }

        private bool TrySyncLanOrderItemDelete(OrderData order, OrderFileItem item, string reason)
        {
            if (!ShouldUseLanRunApi() || order == null || item == null)
                return false;

            var deleteResult = _orderApplicationService
                .TryDeleteOrderItemViaLanApiAsync(
                    order,
                    item,
                    _lanApiBaseUrl,
                    ResolveLanApiActor())
                .GetAwaiter()
                .GetResult();

            if (deleteResult.IsSuccess && deleteResult.Order != null)
            {
                ApplyLanOrderItemDeleteSnapshot(order, deleteResult.Order);
                TryRefreshRepositorySnapshotFromStorage(_orderHistory, $"lan-api-item-delete-{reason}");
                return true;
            }

            if (deleteResult.CurrentVersion > 0)
                order.StorageVersion = deleteResult.CurrentVersion;

            var errorText = string.IsNullOrWhiteSpace(deleteResult.Error)
                ? "LAN item delete failed"
                : deleteResult.Error;
            Logger.Warn(
                $"LAN-API | item-delete-sync-failed | reason={reason} | order={GetOrderDisplayId(order)} | item={item.ItemId} | conflict={(deleteResult.IsConflict ? "1" : "0")} | unavailable={(deleteResult.IsUnavailable ? "1" : "0")} | {errorText}");
            TryRefreshRepositorySnapshotFromStorage(_orderHistory, $"lan-api-item-delete-failed-{reason}");
            return false;
        }

        private static void ApplyLanOrderItemVersionsSnapshot(OrderData localOrder, OrderData serverOrder)
        {
            if (localOrder == null || serverOrder == null)
                return;

            if (serverOrder.StorageVersion > 0)
                localOrder.StorageVersion = serverOrder.StorageVersion;

            var serverItemsById = (serverOrder.Items ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ItemId))
                .ToDictionary(item => item.ItemId, item => item, StringComparer.Ordinal);

            foreach (var localItem in (localOrder.Items ?? []).Where(item => item != null))
            {
                if (!serverItemsById.TryGetValue(localItem.ItemId, out var serverItem))
                    continue;

                if (serverItem.StorageVersion > 0)
                    localItem.StorageVersion = serverItem.StorageVersion;
            }
        }

        private static void ApplyLanOrderItemDeleteSnapshot(OrderData localOrder, OrderData serverOrder)
        {
            if (localOrder == null || serverOrder == null)
                return;

            if (serverOrder.StorageVersion > 0)
                localOrder.StorageVersion = serverOrder.StorageVersion;

            var serverIds = (serverOrder.Items ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ItemId))
                .Select(item => item.ItemId)
                .ToHashSet(StringComparer.Ordinal);

            if (localOrder.Items != null)
                localOrder.Items.RemoveAll(localItem => localItem == null || !serverIds.Contains(localItem.ItemId));

            ApplyLanOrderItemVersionsSnapshot(localOrder, serverOrder);
        }

        private void UpsertOrderInHistory(OrderData updatedOrder)
        {
            if (updatedOrder == null)
                return;

            var index = _orderHistory.FindIndex(order =>
                order != null
                && string.Equals(order.InternalId, updatedOrder.InternalId, StringComparison.Ordinal));

            if (index >= 0)
            {
                _orderHistory[index] = updatedOrder;
                return;
            }

            _orderHistory.Add(updatedOrder);
        }

        private void ShowLanWriteError(LanOrderWriteCommandResult writeResult, string operationCaption)
        {
            var defaultError = "LAN API недоступен";
            var errorText = string.IsNullOrWhiteSpace(writeResult.Error)
                ? defaultError
                : writeResult.Error;

            if (writeResult.IsConflict)
            {
                TryRefreshRepositorySnapshotFromStorage(_orderHistory, "lan-api-write-conflict");
                SetBottomStatus($"{operationCaption}: конфликт версии");
                MessageBox.Show(
                    this,
                    $"Сервер отклонил запись из-за конфликта версии.{Environment.NewLine}{errorText}",
                    operationCaption,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            SetBottomStatus($"{operationCaption} не выполнено");
            MessageBox.Show(
                this,
                $"{operationCaption} не выполнено.{Environment.NewLine}{errorText}",
                operationCaption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void LoadSettings()
        {
            var settings = _settingsProvider.Load();
            _ordersRootPath = settings.OrdersRootPath;
            _tempRootPath = settings.TempFolderPath;
            _grandpaFolder = settings.GrandpaPath;
            _archiveDoneSubfolder = settings.ArchiveDoneSubfolder;
            _jsonHistoryFile = settings.HistoryFilePath;
            _ordersStorageBackend = settings.OrdersStorageBackend;
            _lanPostgreSqlConnectionString = settings.LanPostgreSqlConnectionString;
            _lanApiBaseUrl = settings.LanApiBaseUrl;
            _orderApplicationService.ConfigureHistoryRepository(_ordersStorageBackend, _lanPostgreSqlConnectionString, _jsonHistoryFile);
            _managerLogFilePath = settings.ManagerLogFilePath;
            _orderLogsFolderPath = settings.OrderLogsFolderPath;
            _usersSourceFilePath = settings.UsersFilePath;
            _usersCacheFilePath = settings.UsersCacheFilePath;
            _printTilesCacheFolderPath = ResolveLocalThumbnailCacheFolderPath();
            _sharedPrintTilesCacheFolderPath = ResolveOptionalSharedThumbnailCacheFolderPath(settings.SharedThumbnailCachePath);
            Logger.LogFilePath = _managerLogFilePath;
        }

        private void InitializeProcessor()
        {
            _dependencyHealthByName.Clear();
            _processor = new OrderProcessor(_ordersRootPath, _settingsProvider);
            _processor.OnStatusChanged += (orderId, status, reason) =>
            {
                void Apply()
                {
                    var order = _orderHistory.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.Ordinal));
                    if (order == null)
                        return;

                    SetOrderStatus(order, status, OrderStatusSourceNames.Processor, reason, persistHistory: false, rebuildGrid: true);
                }

                if (InvokeRequired)
                    BeginInvoke((Action)Apply);
                else
                    Apply();
            };
            _processor.OnLog += message => SetBottomStatus(message);
            _processor.OnCapturedOrderLog += (orderId, message) =>
            {
                var order = _orderHistory.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.Ordinal));
                if (order == null || string.IsNullOrWhiteSpace(message))
                    return;

                AppendCapturedProcessorLog(order, message);
            };
            _processor.OnProgressChanged += (orderId, progressValue, _) =>
            {
                void Apply()
                {
                    ApplyProcessorProgress(orderId, progressValue);
                }

                if (InvokeRequired)
                    BeginInvoke((Action)Apply);
                else
                    Apply();
            };
            _processor.OnDependencyHealthChanged += signal =>
            {
                void Apply()
                {
                    ApplyProcessorDependencyHealthSignal(signal);
                }

                if (InvokeRequired)
                    BeginInvoke((Action)Apply);
                else
                    Apply();
            };
        }

        private void ApplyQueueVisualStyle()
        {
            scMain.Panel1.Padding = new Padding(1, 0, 0, 0);
            scMain.Panel1.BackColor = QueuePanelDividerColor;
            EnsureQueuePanelLayoutOrder();

            treeView1.HideSelection = false;
            treeView1.DrawMode = TreeViewDrawMode.OwnerDrawText;
            treeView1.FullRowSelect = true;
            treeView1.BorderStyle = BorderStyle.None;
            treeView1.ShowLines = false;
            treeView1.ShowRootLines = false;
            treeView1.ShowPlusMinus = false;
            treeView1.BackColor = QueuePanelBackColor;
            treeView1.ForeColor = QueueTextColor;
            treeView1.ItemHeight = 48;
            treeView1.Indent = 20;
            treeView1.LineColor = QueuePanelBackColor;
            treeView1.DrawNode -= TreeView1_DrawNode;
            treeView1.DrawNode += TreeView1_DrawNode;
            treeView1.MouseMove -= TreeView1_MouseMove;
            treeView1.MouseMove += TreeView1_MouseMove;
            treeView1.MouseLeave -= TreeView1_MouseLeave;
            treeView1.MouseLeave += TreeView1_MouseLeave;

            cbQueue.DrawMode = DrawMode.Normal;
        }

        private void EnsureQueuePanelLayoutOrder()
        {
            treeView1.Dock = DockStyle.Fill;
            scMain.Panel1.PerformLayout();
        }

        private void InitializeQueueNavigation()
        {
            PopulateQueueTree();

            treeView1.AfterSelect += TreeView1_AfterSelect;
            cbQueue.SelectedIndexChanged += CbQueue_SelectedIndexChanged;
            dgvJobs.RowsAdded += (_, _) => HandleOrdersGridChanged();
            dgvJobs.RowsRemoved += (_, _) => HandleOrdersGridChanged();
            dgvJobs.DataBindingComplete += (_, _) => HandleOrdersGridChanged();
            dgvJobs.CellValueChanged += DgvJobs_CellValueChanged;
            dgvJobs.CellDoubleClick += DgvJobs_CellDoubleClick;

            if (treeView1.Nodes.Count == 0)
                return;

            _isSyncingQueueSelection = true;
            SyncQueueSelection(QueueStatuses[0]);
            _isSyncingQueueSelection = false;
        }

        private void InitializeOrdersDataFlow()
        {
            tbSearch.TextChanged += (_, _) => RebuildOrdersGrid();
            LoadHistory();
            RefreshArchivedStatuses(forceArchiveIndexRefresh: true, rebuildGridIfChanged: false);
            RebuildOrdersGrid();
            InitializeOrdersViewsWarmupCoordinator();
        }

        private void InitializeOrdersGridVisuals()
        {
            const int horizontalPadding = 10;
            var unifiedPadding = new Padding(horizontalPadding, 0, horizontalPadding, 0);
            var rightEdgeSafePadding = horizontalPadding + SystemInformation.VerticalScrollBarWidth + 4;

            dgvJobs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvJobs.MultiSelect = true;
            dgvJobs.AllowUserToResizeRows = false;
            dgvJobs.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            dgvJobs.RowTemplate.Resizable = DataGridViewTriState.False;
            dgvJobs.RowTemplate.Height = 42;
            dgvJobs.AllowDrop = true;
            dgvJobs.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            dgvJobs.GridColor = OrdersGridLineColor;
            dgvJobs.DefaultCellStyle.BackColor = OrdersRowBaseBackColor;
            dgvJobs.RowsDefaultCellStyle.BackColor = OrdersRowBaseBackColor;
            dgvJobs.AlternatingRowsDefaultCellStyle.BackColor = OrdersRowZebraBackColor;
            dgvJobs.DefaultCellStyle.SelectionBackColor = OrdersRowSelectedBackColor;
            dgvJobs.RowsDefaultCellStyle.SelectionBackColor = OrdersRowSelectedBackColor;
            dgvJobs.AlternatingRowsDefaultCellStyle.SelectionBackColor = OrdersRowSelectedBackColor;
            dgvJobs.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgvJobs.RowsDefaultCellStyle.SelectionForeColor = Color.Black;
            dgvJobs.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.Black;
            dgvJobs.DefaultCellStyle.Padding = unifiedPadding;
            dgvJobs.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgvJobs.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            dgvJobs.EnableHeadersVisualStyles = true;
            dgvJobs.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvJobs.ColumnHeadersHeight = dgvJobs.RowTemplate.Height;
            dgvJobs.ColumnHeadersDefaultCellStyle.BackColor = Color.White;
            dgvJobs.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
            dgvJobs.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.White;
            dgvJobs.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.Black;
            dgvJobs.ColumnHeadersDefaultCellStyle.Padding = unifiedPadding;
            dgvJobs.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgvJobs.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            ApplyLeftAlignedColumnStyle(colStatus, unifiedPadding.Left, unifiedPadding.Right);
            ApplyRightAlignedNumericColumnStyle(colOrderNumber, unifiedPadding.Left, unifiedPadding.Right);
            ApplyLeftAlignedColumnStyle(colPrep, unifiedPadding.Left, unifiedPadding.Right);
            ApplyLeftAlignedColumnStyle(colPitstop, unifiedPadding.Left, unifiedPadding.Right);
            ApplyLeftAlignedColumnStyle(colHotimposing, unifiedPadding.Left, unifiedPadding.Right);
            ApplyLeftAlignedColumnStyle(colPrint, unifiedPadding.Left, unifiedPadding.Right);
            ApplyRightAlignedNumericColumnStyle(colReceived, unifiedPadding.Left, unifiedPadding.Right);
            ApplyRightAlignedNumericColumnStyle(colCreated, unifiedPadding.Left, rightEdgeSafePadding);

            dgvJobs.CellPainting += DgvJobs_CellPainting;
            dgvJobs.CellFormatting += DgvJobs_CellFormatting;
            dgvJobs.CellClick += DgvJobs_CellClick;
            dgvJobs.CellToolTipTextNeeded += DgvJobs_CellToolTipTextNeeded;
            dgvJobs.CellMouseEnter += DgvJobs_CellMouseEnter;
            dgvJobs.CellMouseLeave += DgvJobs_CellMouseLeave;
            dgvJobs.MouseLeave += DgvJobs_MouseLeave;
            dgvJobs.MouseDown += DgvJobs_MouseDown;
            dgvJobs.MouseMove += DgvJobs_MouseMove;
            dgvJobs.MouseUp += DgvJobs_MouseUp;
            dgvJobs.DragEnter += DgvJobs_DragEnter;
            dgvJobs.DragOver += DgvJobs_DragOver;
            dgvJobs.DragDrop += DgvJobs_DragDrop;

        }

        private static void ApplyRightAlignedNumericColumnStyle(DataGridViewColumn? column, int leftPadding, int rightPadding)
        {
            if (column == null)
                return;

            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Padding = new Padding(leftPadding, 0, rightPadding, 0);
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.HeaderCell.Style.Padding = new Padding(leftPadding, 0, rightPadding, 0);
        }

        private static void ApplyLeftAlignedColumnStyle(DataGridViewColumn? column, int leftPadding, int rightPadding)
        {
            if (column == null)
                return;

            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            column.DefaultCellStyle.Padding = new Padding(leftPadding, 0, rightPadding, 0);
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            column.HeaderCell.Style.Padding = new Padding(leftPadding, 0, rightPadding, 0);
        }

        private void InitializeActionButtonsState()
        {
            dgvJobs.SelectionChanged += (_, _) =>
            {
                if (!_isSyncingGridSelection)
                    SyncTilesSelectionWithGrid();

                UpdateActionButtonsState();
                UpdateTrayStatsIndicator();
            };
            dgvJobs.CurrentCellChanged += (_, _) =>
            {
                if (!_isSyncingGridSelection)
                    SyncTilesSelectionWithGrid();

                UpdateActionButtonsState();
                UpdateTrayStatsIndicator();
            };
            UpdateActionButtonsState();
            UpdateTrayStatsIndicator();
        }

        private void InitializeOrdersKeyboardShortcuts()
        {
            KeyPreview = true;
            KeyDown += MainForm_KeyDown;
        }

        private async void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete && e.KeyCode != Keys.Back)
                return;

            if (!dgvJobs.ContainsFocus && !_lvPrintTiles.ContainsFocus)
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            if (!EnsureServerWriteAllowed("Удаление заказа"))
                return;
            await RemoveSelectedOrderAsync();
        }

        private void UpdateActionButtonsState()
        {
            if (_serverHardLockActive)
            {
                tsbNewJob.Enabled = false;
                tsbRun.Enabled = false;
                tsbStop.Enabled = false;
                tsbRemove.Enabled = false;
                tsbAddFile.Enabled = false;
                tsbBrowse.Enabled = false;
                tsbConsole.Enabled = true;
                tsbAddFile.ToolTipText = "Недоступно: нет соединения с сервером.";
                tsbBrowse.ToolTipText = "Недоступно: нет соединения с сервером.";
                return;
            }

            var order = GetSelectedOrder();
            var hasOrder = order != null;
            var hasSelectedOrderContainer = TryGetSelectedOrderContainer(out var selectedOrderContainer) && selectedOrderContainer != null;
            var hasFirstFileInSelectedOrder = hasSelectedOrderContainer && HasAtLeastOneOrderFile(selectedOrderContainer!);
            var canAddFileToSelectedOrder = hasSelectedOrderContainer && hasFirstFileInSelectedOrder;
            var canOpenOrderFolder = false;
            var browseTooltipText = string.Empty;

            if (hasOrder && order != null)
            {
                if (TryGetBrowseFolderPathForOrder(order, out _, out var browseReason))
                {
                    canOpenOrderFolder = true;
                    browseTooltipText = OrderTopologyService.IsMultiOrder(order)
                        ? "Открыть общую папку группы"
                        : "Открыть папку заказа";
                }
                else
                {
                    canOpenOrderFolder = false;
                    browseTooltipText = string.IsNullOrWhiteSpace(browseReason) ? "Папка недоступна" : browseReason;
                }
            }

            var canStopByLocalRunSession = hasOrder && _runTokensByOrder.ContainsKey(order!.InternalId);
            var canStopByLanStatus = hasOrder
                && ShouldUseLanRunApi()
                && string.Equals(NormalizeStatus(order!.Status), WorkflowStatusNames.Processing, StringComparison.OrdinalIgnoreCase);

            tsbRun.Enabled = hasOrder;
            tsbRemove.Enabled = hasOrder;
            tsbBrowse.Enabled = canOpenOrderFolder;
            tsbConsole.Enabled = hasOrder;
            tsbStop.Enabled = canStopByLocalRunSession || canStopByLanStatus;
            tsbAddFile.Enabled = canAddFileToSelectedOrder;

            tsbBrowse.ToolTipText = browseTooltipText;
            tsbAddFile.ToolTipText = canAddFileToSelectedOrder
                ? "Добавить следующий файл в группу заказа"
                : hasSelectedOrderContainer
                    ? "Сначала добавьте первый файл в строку заказа (ячейка Источник или drag-and-drop)"
                    : "Выберите строку заказа (single-order или group-order)";
        }

        private static bool HasAtLeastOneOrderFile(OrderData order)
        {
            if (order == null)
                return false;

            static bool HasPath(string? rawPath)
            {
                return !string.IsNullOrWhiteSpace(CleanPath(rawPath));
            }

            if (HasPath(order.SourcePath) || HasPath(order.PreparedPath) || HasPath(order.PrintPath))
                return true;

            if (order.Items == null || order.Items.Count == 0)
                return false;

            foreach (var item in order.Items.Where(x => x != null))
            {
                if (HasPath(item.SourcePath) || HasPath(item.PreparedPath) || HasPath(item.PrintPath))
                    return true;
            }

            return false;
        }

        private void ShowSettingsDialog()
        {
            var currentSettings = _settingsProvider.Load();
            using var settingsForm = new SettingsDialogForm(
                _ordersRootPath,
                _tempRootPath,
                _grandpaFolder,
                _archiveDoneSubfolder,
                _jsonHistoryFile,
                _managerLogFilePath,
                _orderLogsFolderPath,
                currentSettings.SharedThumbnailCachePath,
                currentSettings.FontsFolderPath,
                _ordersStorageBackend,
                _lanPostgreSqlConnectionString,
                _lanApiBaseUrl,
                currentSettings.MaxParallelism,
                useExtendedMode: currentSettings.UseExtendedMode);

            if (settingsForm.ShowDialog(this) != DialogResult.OK)
                return;

            _ordersRootPath = settingsForm.OrdersRootPath;
            _tempRootPath = settingsForm.TempRootPath;
            _grandpaFolder = settingsForm.GrandpaPath;
            _archiveDoneSubfolder = settingsForm.ArchiveDoneSubfolder;
            _jsonHistoryFile = StoragePaths.ResolveFilePath(settingsForm.HistoryFilePath, "history.json");
            _managerLogFilePath = StoragePaths.ResolveFilePath(settingsForm.ManagerLogFilePath, "manager.log");
            _orderLogsFolderPath = StoragePaths.ResolveFolderPath(settingsForm.OrderLogsFolderPath, "order-logs");
            var nextSharedCacheRootPath = ResolveOptionalSharedThumbnailCacheFolderPath(settingsForm.SharedThumbnailCachePath);
            var cacheRootChanged = !string.Equals(
                _sharedPrintTilesCacheFolderPath,
                nextSharedCacheRootPath,
                StringComparison.OrdinalIgnoreCase);
            _sharedPrintTilesCacheFolderPath = nextSharedCacheRootPath;
            _ordersStorageBackend = settingsForm.OrdersStorageBackend;
            _lanPostgreSqlConnectionString = settingsForm.LanPostgreSqlConnectionString;
            _lanApiBaseUrl = settingsForm.LanApiBaseUrl;

            var settings = _settingsProvider.Load();
            settings.OrdersRootPath = _ordersRootPath;
            settings.TempFolderPath = _tempRootPath;
            settings.GrandpaPath = _grandpaFolder;
            settings.ArchiveDoneSubfolder = _archiveDoneSubfolder;
            settings.HistoryFilePath = _jsonHistoryFile;
            settings.ManagerLogFilePath = _managerLogFilePath;
            settings.OrderLogsFolderPath = _orderLogsFolderPath;
            settings.SharedThumbnailCachePath = settingsForm.SharedThumbnailCachePath;
            settings.FontsFolderPath = settingsForm.FontsFolderPath;
            settings.MaxParallelism = settingsForm.MaxParallelism;
            settings.UseExtendedMode = settingsForm.UseExtendedMode;
            settings.OrdersStorageBackend = _ordersStorageBackend;
            settings.LanPostgreSqlConnectionString = _lanPostgreSqlConnectionString;
            settings.LanApiBaseUrl = _lanApiBaseUrl;
            _settingsProvider.Save(settings);
            _orderApplicationService.ConfigureHistoryRepository(_ordersStorageBackend, _lanPostgreSqlConnectionString, _jsonHistoryFile);

            Logger.LogFilePath = _managerLogFilePath;
            InitializeProcessor();
            RefreshUsersDirectory(forceRefresh: true, refreshGrid: true);
            RefreshCurrentUserProfile(forceRefresh: true);
            RefreshTrayIndicators();
            var settingsSavedMessage = cacheRootChanged
                ? "Настройки сохранены. Путь общего кэша превью изменен и будет полностью применен после перезапуска приложения."
                : "Настройки сохранены";
            RefreshArchivedStatuses(forceArchiveIndexRefresh: true, rebuildGridIfChanged: true);
            SetBottomStatus(settingsSavedMessage);
            MessageBox.Show(this, settingsSavedMessage, "OrdersWorkspaceForm", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string ResolveLocalThumbnailCacheFolderPath()
        {
            var fallbackPath = AppSettings.DefaultThumbnailCacheFolderPath;
            Directory.CreateDirectory(fallbackPath);
            return fallbackPath;
        }

        private static string ResolveOptionalSharedThumbnailCacheFolderPath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return string.Empty;

            var candidatePath = Path.IsPathRooted(configuredPath)
                ? configuredPath.Trim()
                : Path.Combine(StoragePaths.AppBaseDirectory, configuredPath.Trim());

            try
            {
                Directory.CreateDirectory(candidatePath);
                return candidatePath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void splitServer_Panel2_Paint(object? sender, PaintEventArgs e)
        {

        }
    }
}

