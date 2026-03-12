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

namespace MyManager
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
            InitializeStatusCellVisuals();
            LoadSettings();
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
            InitializeClassicGridScrollBar();
            InitializeViewModeSwitches();
            InitializeOrdersDataFlow();
            InitializeOrderRowContextMenu();
            InitializeActionButtonsState();
            InitializeOrdersKeyboardShortcuts();
            InitializeTrayIndicators();
            FormClosed += MainForm_FormClosed;
            SetOrdersViewMode(OrdersViewMode.List);
            SetBottomStatus(DefaultTrayStatusText);
        }

        // обработчик нажатия кнопок в ToolStrip
        private async void TsMainActions_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == tsbNewJob)
            {
                CreateNewOrder();
                return;
            }

            if (e.ClickedItem == tsbParameters)
            {
                ShowSettingsDialog();
                return;
            }

            if (e.ClickedItem == tsbRun)
            {
                await RunSelectedOrderAsync();
                return;
            }

            if (e.ClickedItem == tsbStop)
            {
                StopSelectedOrder();
                return;
            }

            if (e.ClickedItem == tsbRemove)
            {
                RemoveSelectedOrder();
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

        private void CreateNewOrder()
        {
            var settings = AppSettings.Load();
            if (settings.UseExtendedMode)
                CreateNewExtendedOrder();
            else
                CreateNewSimpleOrder();
        }

        private void CreateNewSimpleOrder()
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
                Status = "Ожидание",
                PitStopAction = "-",
                ImposingAction = "-"
            };

            AddCreatedOrder(order);
        }

        private void CreateNewExtendedOrder()
        {
            using var form = new OrderForm(_ordersRootPath);
            if (form.ShowDialog(this) != DialogResult.OK || form.ResultOrder == null)
                return;

            AddCreatedOrder(form.ResultOrder);
        }

        private void AddCreatedOrder(OrderData order)
        {
            if (order == null)
                return;

            if (string.IsNullOrWhiteSpace(order.InternalId))
                order.InternalId = Guid.NewGuid().ToString("N");
            if (order.OrderDate == default)
                order.OrderDate = OrderData.PlaceholderOrderDate;
            if (order.ArrivalDate == default)
                order.ArrivalDate = DateTime.Now;

            _orderHistory.Add(order);
            SaveHistory();
            RebuildOrdersGrid();
            TryRestoreSelectedRowByTag($"order|{order.InternalId}");
        }

        private void EditOrderFromGrid(int rowIndex)
        {
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

            var settings = AppSettings.Load();
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

            order.Id = form.OrderNumber.Trim();
            order.OrderDate = form.OrderDate;
            if (order.ArrivalDate == default)
                order.ArrivalDate = DateTime.Now;

            SaveHistory();
            RebuildOrdersGrid();
            TryRestoreSelectedRowByTag($"order|{order.InternalId}");
        }

        private void EditOrderExtended(OrderData order)
        {
            using var form = new OrderForm(_ordersRootPath, order);
            if (form.ShowDialog(this) != DialogResult.OK || form.ResultOrder == null)
                return;

            var updated = form.ResultOrder;
            order.Id = updated.Id;
            order.StartMode = updated.StartMode;
            order.Keyword = updated.Keyword;
            order.ArrivalDate = updated.ArrivalDate;
            order.OrderDate = updated.OrderDate;
            order.FolderName = updated.FolderName;
            order.SourcePath = updated.SourcePath;
            order.PreparedPath = updated.PreparedPath;
            order.PrintPath = updated.PrintPath;
            order.PitStopAction = updated.PitStopAction;
            order.ImposingAction = updated.ImposingAction;

            SaveHistory();
            RebuildOrdersGrid();
            TryRestoreSelectedRowByTag($"order|{order.InternalId}");
        }

        private void LoadSettings()
        {
            var settings = AppSettings.Load();
            _ordersRootPath = settings.OrdersRootPath;
            _tempRootPath = settings.TempFolderPath;
            _grandpaFolder = settings.GrandpaPath;
            _archiveDoneSubfolder = settings.ArchiveDoneSubfolder;
            _jsonHistoryFile = settings.HistoryFilePath;
            _managerLogFilePath = settings.ManagerLogFilePath;
            _orderLogsFolderPath = settings.OrderLogsFolderPath;
            _printTilesCacheFolderPath = ResolveLocalThumbnailCacheFolderPath();
            _sharedPrintTilesCacheFolderPath = ResolveOptionalSharedThumbnailCacheFolderPath(settings.SharedThumbnailCachePath);
            Logger.LogFilePath = _managerLogFilePath;
        }

        private void InitializeProcessor()
        {
            _processor = new OrderProcessor(_ordersRootPath);
            _processor.OnStatusChanged += (orderId, status, reason) =>
            {
                void Apply()
                {
                    var order = _orderHistory.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.Ordinal));
                    if (order == null)
                        return;

                    SetOrderStatus(order, status, "processor", reason, persistHistory: false, rebuildGrid: true);
                }

                if (InvokeRequired)
                    BeginInvoke((Action)Apply);
                else
                    Apply();
            };
            _processor.OnLog += message => SetBottomStatus(message);
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
        }

        private void ApplyQueueVisualStyle()
        {
            scMain.Panel1.BackColor = QueuePanelBackColor;

            treeView1.HideSelection = false;
            treeView1.DrawMode = TreeViewDrawMode.OwnerDrawText;
            treeView1.FullRowSelect = true;
            treeView1.BorderStyle = BorderStyle.None;
            treeView1.ShowLines = false;
            treeView1.ShowRootLines = false;
            treeView1.BackColor = QueuePanelBackColor;
            treeView1.ForeColor = QueueTextColor;
            treeView1.ItemHeight = 44;
            treeView1.Indent = 18;
            treeView1.LineColor = Color.FromArgb(134, 142, 166);
            treeView1.DrawNode += TreeView1_DrawNode;

            cbQueue.DrawMode = DrawMode.Normal;
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

            var firstUserNode = treeView1.Nodes[0];
            _isSyncingQueueSelection = true;
            SelectUser(firstUserNode, QueueStatuses[0]);
            var defaultStatusNode = FindStatusNode(firstUserNode, QueueStatuses[0]);
            if (defaultStatusNode != null)
            {
                treeView1.SelectedNode = defaultStatusNode;
                defaultStatusNode.EnsureVisible();
            }
            else
            {
                treeView1.SelectedNode = firstUserNode;
                firstUserNode.EnsureVisible();
            }
            _isSyncingQueueSelection = false;
        }

        private void InitializeOrdersDataFlow()
        {
            tbSearch.TextChanged += (_, _) => RebuildOrdersGrid();
            LoadHistory();
            RebuildOrdersGrid();
            InitializeOrdersViewsWarmupCoordinator();
        }

        private void InitializeOrdersGridVisuals()
        {
            var unifiedPadding = new Padding(8, 0, 0, 0);

            dgvJobs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvJobs.MultiSelect = true;
            dgvJobs.AllowUserToResizeRows = false;
            dgvJobs.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            dgvJobs.RowTemplate.Resizable = DataGridViewTriState.False;
            dgvJobs.RowTemplate.Height = 42;
            dgvJobs.AllowDrop = true;
            dgvJobs.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            dgvJobs.GridColor = Color.FromArgb(218, 218, 218);
            dgvJobs.DefaultCellStyle.SelectionBackColor = OrdersRowSelectedBackColor;
            dgvJobs.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgvJobs.DefaultCellStyle.Padding = unifiedPadding;
            dgvJobs.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgvJobs.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            dgvJobs.EnableHeadersVisualStyles = true;
            dgvJobs.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvJobs.ColumnHeadersHeight = 34;
            dgvJobs.ColumnHeadersDefaultCellStyle.BackColor = Color.White;
            dgvJobs.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
            dgvJobs.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.White;
            dgvJobs.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.Black;
            dgvJobs.ColumnHeadersDefaultCellStyle.Padding = unifiedPadding;
            dgvJobs.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgvJobs.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;

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

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete && e.KeyCode != Keys.Back)
                return;

            if (!dgvJobs.ContainsFocus && !_lvPrintTiles.ContainsFocus)
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            RemoveSelectedOrder();
        }

        private void UpdateActionButtonsState()
        {
            var order = GetSelectedOrder();
            var hasOrder = order != null;

            tsbRun.Enabled = hasOrder;
            tsbRemove.Enabled = hasOrder;
            tsbBrowse.Enabled = hasOrder;
            tsbConsole.Enabled = hasOrder;
            tsbStop.Enabled = hasOrder && _runTokensByOrder.ContainsKey(order!.InternalId);
        }

        private void ShowSettingsDialog()
        {
            var currentSettings = AppSettings.Load();
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

            var settings = AppSettings.Load();
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
            settings.Save();

            Logger.LogFilePath = _managerLogFilePath;
            InitializeProcessor();
            RefreshTrayIndicators();
            var settingsSavedMessage = cacheRootChanged
                ? "Настройки сохранены. Путь общего кэша превью изменен и будет полностью применен после перезапуска приложения."
                : "Настройки сохранены";
            SetBottomStatus(settingsSavedMessage);
            MessageBox.Show(this, settingsSavedMessage, "MainForm", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    }
}
