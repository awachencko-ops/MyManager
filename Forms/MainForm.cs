using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Svg;

namespace MyManager
{
    public partial class MainForm : Form
    {
        private string _ordersRootPath = @"C:\MyManager\Orders";
        private string _tempRootPath = string.Empty;
        private string _grandpaFolder = @"C:\MyManager\Archive";
        private string _archiveDoneSubfolder = "Готово";
        private string _jsonHistoryFile = "history.json";
        private string _managerLogFilePath = "manager.log";
        private string _orderLogsFolderPath = string.Empty;
        private readonly List<OrderData> _orderHistory = [];
        private readonly HashSet<string> _expandedOrderIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CancellationTokenSource> _runTokensByOrder = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _runProgressByOrderInternalId = new(StringComparer.Ordinal);
        private readonly OrderGridContextMenu _gridMenu = new();
        private OrderProcessor? _processor;
        private System.Windows.Forms.Timer? _trayIndicatorsTimer;
        private bool _isRebuildingGrid;
        private int _hoveredRowIndex = -1;
        private int _ctxRow = -1;
        private int _ctxCol = -1;
        private Rectangle _dragBoxFromMouseDown = Rectangle.Empty;
        private int _dragSourceRowIndex = -1;
        private int _dragSourceColumnIndex = -1;

        // На будущее: список пользователей можно наполнять из настроек/БД.
        private readonly List<string> _users = ["Сервер \"Таудеми\""];

        private static readonly string[] QueueStatuses =
        {
            "Все задания",
            "Обработанные",
            "В архиве",
            "Обрабатывается",
            "Задержанные",
            "Завершено"
        };

        // Сопоставление рабочих статусов с группами очереди (treeView1/cbQueue):
        // Обработанные: Обработано
        // В архиве: В архиве
        // Обрабатывается: Выполняется сборка, Обрабатывается, Ожидание
        // Задержанные: Отменено, Ошибка
        // Завершено: Завершено
        private static readonly string[] FilterStatuses =
        {
            "Обработано",
            "В архиве",
            "Выполняется сборка",
            "Обрабатывается",
            "Ожидание",
            "Отменено",
            "Ошибка",
            "Завершено"
        };
        private static readonly string[] FilterUsers =
        {
            "Андрей",
            "Катя",
            "Вероника"
        };
        private const string StatusFilterLabelText = "Состояние задания";
        private const string OrderNoSearchLabelText = "Номер заказа";
        private const string UserFilterLabelText = "Пользователь";
        private const string CreatedDateFilterLabelText = "Начало обработки";
        private const string ReceivedDateFilterLabelText = "Дата поступления";
        private const string DefaultTrayStatusText = "Готово";
        private const int TrayIndicatorsRefreshIntervalMs = 15000;
        private const long DiskWarningThresholdBytes = 10L * 1024 * 1024 * 1024;
        private const long DiskCriticalThresholdBytes = 5L * 1024 * 1024 * 1024;

        private static readonly Dictionary<string, string[]> QueueStatusMappings = new(StringComparer.Ordinal)
        {
            ["Обработанные"] = ["Обработано"],
            ["В архиве"] = ["В архиве"],
            ["Обрабатывается"] = ["Выполняется сборка", "Обрабатывается", "Ожидание"],
            ["Задержанные"] = ["Отменено", "Ошибка"],
            ["Завершено"] = ["Завершено"]
        };

        private bool _isSyncingQueueSelection;
        private string _currentUserName = string.Empty;
        private readonly HashSet<string> _selectedFilterStatuses = new(StringComparer.Ordinal);
        private readonly HashSet<string> _selectedFilterUsers = new(StringComparer.Ordinal);
        private string _orderNumberFilterText = string.Empty;
        private CreatedDateFilterKind _createdDateFilterKind;
        private CreatedDateSingleMode _createdDateSingleMode = CreatedDateSingleMode.ExactDate;
        private DateTime _createdDateSingleValue = DateTime.Today;
        private DateTime _createdDateRangeFrom = DateTime.Today;
        private DateTime _createdDateRangeTo = DateTime.Today;
        private CreatedDateFilterKind _receivedDateFilterKind;
        private CreatedDateSingleMode _receivedDateSingleMode = CreatedDateSingleMode.ExactDate;
        private DateTime _receivedDateSingleValue = DateTime.Today;
        private DateTime _receivedDateRangeFrom = DateTime.Today;
        private DateTime _receivedDateRangeTo = DateTime.Today;
        private ToolStripDropDown? _statusFilterDropDown;
        private CheckedListBox? _statusFilterCheckedList;
        private bool _isUpdatingStatusFilterList;
        private bool _suppressNextStatusFilterLabelClick;
        private ToolStripDropDown? _orderNoFilterDropDown;
        private TextBox? _orderNoFilterTextBox;
        private Button? _orderNoFilterClearButton;
        private Button? _orderNoFilterApplyButton;
        private bool _suppressNextOrderNoLabelClick;
        private PictureBox? _userFilterGlyph;
        private Label? _userFilterLabel;
        private ToolStripDropDown? _userFilterDropDown;
        private CheckedListBox? _userFilterCheckedList;
        private Button? _userFilterClearButton;
        private Button? _userFilterApplyButton;
        private bool _isUpdatingUserFilterList;
        private bool _suppressNextUserFilterLabelClick;
        private PictureBox? _createdFilterGlyph;
        private Label? _createdFilterLabel;
        private ToolStripDropDown? _createdFilterDropDown;
        private RadioButton? _createdFilterTodayRadio;
        private RadioButton? _createdFilterSingleRadio;
        private ComboBox? _createdFilterSingleModeCombo;
        private DateTimePicker? _createdFilterSingleDatePicker;
        private RadioButton? _createdFilterRangeRadio;
        private DateTimePicker? _createdFilterRangeFromDatePicker;
        private DateTimePicker? _createdFilterRangeToDatePicker;
        private Button? _createdFilterClearButton;
        private Button? _createdFilterApplyButton;
        private bool _isSyncingCreatedFilterControls;
        private bool _suppressNextCreatedFilterLabelClick;
        private bool _isCreatedDateCalendarOpen;
        private ToolStripDropDown? _createdCalendarDropDown;
        private MonthCalendar? _createdCalendar;
        private Button? _createdCalendarOkButton;
        private DateTimePicker? _createdCalendarTargetPicker;
        private PictureBox? _receivedFilterGlyph;
        private Label? _receivedFilterLabel;
        private ToolStripDropDown? _receivedFilterDropDown;
        private RadioButton? _receivedFilterTodayRadio;
        private RadioButton? _receivedFilterSingleRadio;
        private ComboBox? _receivedFilterSingleModeCombo;
        private DateTimePicker? _receivedFilterSingleDatePicker;
        private RadioButton? _receivedFilterRangeRadio;
        private DateTimePicker? _receivedFilterRangeFromDatePicker;
        private DateTimePicker? _receivedFilterRangeToDatePicker;
        private Button? _receivedFilterClearButton;
        private Button? _receivedFilterApplyButton;
        private bool _isSyncingReceivedFilterControls;
        private bool _suppressNextReceivedFilterLabelClick;
        private bool _isReceivedDateCalendarOpen;
        private ToolStripDropDown? _receivedCalendarDropDown;
        private MonthCalendar? _receivedCalendar;
        private Button? _receivedCalendarOkButton;
        private DateTimePicker? _receivedCalendarTargetPicker;
        private int _acknowledgedErrorCount;

        private static readonly Color QueuePanelBackColor = Color.FromArgb(68, 74, 94);
        private static readonly Color QueueHeaderBackColor = Color.FromArgb(103, 163, 216);
        private static readonly Color QueueStatusSelectedBackColor = Color.FromArgb(57, 63, 81);
        private static readonly Color QueueTextColor = Color.FromArgb(244, 247, 252);
        private static readonly Color OrdersRowSelectedBackColor = Color.FromArgb(235, 240, 250);
        private static readonly Color OrdersRowHoverBackColor = Color.FromArgb(248, 250, 255);
        private static readonly Color OrdersItemRowBackColor = Color.FromArgb(248, 248, 248);

        private sealed class QueueStatusItem
        {
            public QueueStatusItem(string statusName, string text)
            {
                StatusName = statusName;
                Text = text;
            }

            public string StatusName { get; }
            public string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class StatusFilterOption
        {
            public StatusFilterOption(string statusName, int count)
            {
                StatusName = statusName;
                Count = count;
            }

            public string StatusName { get; }
            public int Count { get; }

            public override string ToString()
            {
                return $"{StatusName} ({Count})";
            }
        }

        private sealed class UserFilterOption
        {
            public UserFilterOption(string userName, int count)
            {
                UserName = userName;
                Count = count;
            }

            public string UserName { get; }
            public int Count { get; }

            public override string ToString()
            {
                return $"{UserName} ({Count})";
            }
        }

        private enum CreatedDateFilterKind
        {
            None,
            Today,
            Single,
            Range
        }

        private enum CreatedDateSingleMode
        {
            ExactDate,
            Before,
            After
        }

        public MainForm()
        {
            InitializeComponent();
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
            InitializeOrdersDataFlow();
            InitializeOrderRowContextMenu();
            InitializeActionButtonsState();
            InitializeTrayIndicators();
            FormClosed += MainForm_FormClosed;
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
                order.OrderDate = DateTime.Now;
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
        }

        private void InitializeOrdersGridVisuals()
        {
            var unifiedPadding = new Padding(8, 0, 0, 0);

            dgvJobs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvJobs.MultiSelect = false;
            dgvJobs.AllowUserToResizeRows = false;
            dgvJobs.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            dgvJobs.RowTemplate.Resizable = DataGridViewTriState.False;
            dgvJobs.RowTemplate.Height = 34;
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
            dgvJobs.MouseDown += DgvJobs_MouseDown;
            dgvJobs.MouseMove += DgvJobs_MouseMove;
            dgvJobs.DragEnter += DgvJobs_DragEnter;
            dgvJobs.DragOver += DgvJobs_DragOver;
            dgvJobs.DragDrop += DgvJobs_DragDrop;
        }

        private void InitializeActionButtonsState()
        {
            dgvJobs.SelectionChanged += (_, _) =>
            {
                UpdateActionButtonsState();
                UpdateTrayStatsIndicator();
            };
            dgvJobs.CurrentCellChanged += (_, _) =>
            {
                UpdateActionButtonsState();
                UpdateTrayStatsIndicator();
            };
            UpdateActionButtonsState();
            UpdateTrayStatsIndicator();
        }

        private void InitializeTrayIndicators()
        {
            toolStatus.Spring = true;
            toolStatus.TextAlign = ContentAlignment.MiddleLeft;

            toolProgress.Visible = false;
            toolProgress.Style = ProgressBarStyle.Continuous;
            toolProgress.Minimum = 0;
            toolProgress.Maximum = 100;
            toolProgress.Value = 0;

            toolAlerts.IsLink = true;
            toolAlerts.LinkBehavior = LinkBehavior.HoverUnderline;
            toolAlerts.Click += ToolAlerts_Click;

            _trayIndicatorsTimer ??= new System.Windows.Forms.Timer
            {
                Interval = TrayIndicatorsRefreshIntervalMs
            };
            _trayIndicatorsTimer.Tick -= TrayIndicatorsTimer_Tick;
            _trayIndicatorsTimer.Tick += TrayIndicatorsTimer_Tick;
            _trayIndicatorsTimer.Start();

            _acknowledgedErrorCount = CountOrdersWithErrors();
            RefreshTrayIndicators();
        }

        private void ToolAlerts_Click(object? sender, EventArgs e)
        {
            AcknowledgeErrorNotifications();
            OpenLogForSelectionOrManager();
        }

        private void TrayIndicatorsTimer_Tick(object? sender, EventArgs e)
        {
            UpdateTrayConnectionIndicator();
            UpdateTrayDiskIndicator();
        }

        private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
        {
            if (_trayIndicatorsTimer == null)
                return;

            _trayIndicatorsTimer.Stop();
            _trayIndicatorsTimer.Tick -= TrayIndicatorsTimer_Tick;
            _trayIndicatorsTimer.Dispose();
            _trayIndicatorsTimer = null;
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

        private void InitializeOrderRowContextMenu()
        {
            _gridMenu.OpenFolder = (stage) =>
            {
                var order = GetContextOrder();
                if (order != null)
                    OpenOrderStageFolder(order, stage);
            };
            _gridMenu.Delete = () =>
            {
                if (TrySelectContextRow())
                    RemoveSelectedOrder();
            };
            _gridMenu.Run = async () =>
            {
                if (TrySelectContextRow())
                    await RunSelectedOrderAsync();
            };
            _gridMenu.Stop = () =>
            {
                if (TrySelectContextRow())
                    StopSelectedOrder();
            };
            _gridMenu.PickFile = (stage, fileType) => _ = PickFileFromContextAsync(stage);
            _gridMenu.RemoveFile = (stage) => RemoveFileFromContext(stage);
            _gridMenu.RenameFile = (stage) => RenameFileFromContext(stage);
            _gridMenu.CopyPathToClipboard = (stage) => CopyPathFromContextToClipboard(stage);
            _gridMenu.PastePathFromClipboard = (stage) => _ = PastePathFromClipboardToContextAsync(stage);
            _gridMenu.ApplyWatermark = () => ApplyWatermarkFromContext(isVertical: false);
            _gridMenu.ApplyWatermarkLeft = () => ApplyWatermarkFromContext(isVertical: true);
            _gridMenu.CopyToGrandpa = CopyPrintFromContextToGrandpa;
            _gridMenu.OpenPitStopMan = OpenPitStopManager;
            _gridMenu.OpenImpMan = OpenImposingManager;
            _gridMenu.RemovePitStopAction = RemovePitStopActionFromContext;
            _gridMenu.RemoveImposingAction = RemoveImposingActionFromContext;
            _gridMenu.OpenOrderLog = () =>
            {
                if (TrySelectContextRow())
                    OpenLogForSelectionOrManager();
            };

            dgvJobs.CellMouseDown += DgvJobs_CellMouseDown;
        }

        private void DgvJobs_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var row = dgvJobs.Rows[e.RowIndex];
            var rowTag = row.Tag?.ToString();
            if (!IsOrderTag(rowTag))
                return;

            _ctxRow = e.RowIndex;
            _ctxCol = e.ColumnIndex;
            if (!TrySelectContextRow())
                return;

            var order = GetContextOrder();
            var allowCopyToGrandpa = order == null || UsesOrderFolderStorage(order);
            var columnName = dgvJobs.Columns[e.ColumnIndex].Name;
            var menu = _gridMenu.Build(columnName, allowCopyToGrandpa);
            if (menu.Items.Count == 0)
                return;

            menu.Show(Cursor.Position);
        }

        private bool TrySelectContextRow()
        {
            if (_ctxRow < 0 || _ctxRow >= dgvJobs.Rows.Count)
                return false;

            var row = dgvJobs.Rows[_ctxRow];
            var columnIndex = _ctxCol >= 0 && _ctxCol < dgvJobs.Columns.Count
                ? _ctxCol
                : colStatus.Index;
            if (columnIndex < 0 || columnIndex >= row.Cells.Count)
                columnIndex = colStatus.Index;

            dgvJobs.CurrentCell = row.Cells[columnIndex];
            row.Selected = true;
            return true;
        }

        private OrderData? GetContextOrder()
        {
            if (_ctxRow < 0)
                return null;

            return GetOrderByRowIndex(_ctxRow);
        }

        private async Task PickFileFromContextAsync(int stage)
        {
            if (stage is < 1 or > 3)
                return;

            try
            {
                var order = GetContextOrder();
                if (order == null)
                    return;

                await PickAndCopyFileForOrderAsync(order, stage);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Ошибка выбора файла: {ex.Message}");
                MessageBox.Show(this, $"Не удалось выбрать файл: {ex.Message}", "Файловая операция", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemoveFileFromContext(int stage)
        {
            if (stage is < 1 or > 3)
                return;

            var order = GetContextOrder();
            if (order == null)
                return;

            RemoveFileFromOrder(order, stage);
        }

        private void RenameFileFromContext(int stage)
        {
            if (stage is < 1 or > 3)
                return;

            var order = GetContextOrder();
            if (order == null)
                return;

            RenameFileForOrder(order, stage);
        }

        private void CopyPathFromContextToClipboard(int stage)
        {
            if (stage is < 1 or > 3)
                return;

            var order = GetContextOrder();
            if (order == null)
                return;

            CopyPathToClipboard(order, stage);
        }

        private async Task PastePathFromClipboardToContextAsync(int stage)
        {
            if (stage is < 1 or > 3)
                return;

            try
            {
                var order = GetContextOrder();
                if (order == null)
                    return;

                await PasteFileFromClipboardAsync(order, stage);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Ошибка вставки из буфера: {ex.Message}");
                MessageBox.Show(this, $"Не удалось вставить файл из буфера: {ex.Message}", "Файловая операция", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyWatermarkFromContext(bool isVertical)
        {
            var order = GetContextOrder();
            if (order == null)
                return;

            ProcessWatermark(order, isVertical);
        }

        private void CopyPrintFromContextToGrandpa()
        {
            var order = GetContextOrder();
            if (order == null)
                return;

            CopyToGrandpa(order);
        }

        private void RemovePitStopActionFromContext()
        {
            var order = GetContextOrder();
            if (order == null)
                return;

            RemovePitStopAction(order);
        }

        private void RemoveImposingActionFromContext()
        {
            var order = GetContextOrder();
            if (order == null)
                return;

            RemoveImposingAction(order);
        }

        private void OpenPitStopManager()
        {
            using var form = new ActionManagerForm();
            form.ShowDialog(this);
        }

        private void OpenImposingManager()
        {
            using var form = new ImposingManagerForm();
            form.ShowDialog(this);
        }

        private void ProcessWatermark(OrderData order, bool isVertical)
        {
            try
            {
                if (!HasExistingFile(order.PrintPath))
                {
                    SetBottomStatus("Файл печати не найден");
                    MessageBox.Show(this, "Файл печати не найден.", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                PdfWatermark.Apply(order, isVertical);
                var pos = isVertical ? "слева" : "сверху";
                SetBottomStatus($"Водяной знак ({pos}) нанесен на {GetOrderDisplayId(order)}");
            }
            catch (IOException)
            {
                SetBottomStatus("Файл занят другой программой. Закройте PDF и повторите");
                MessageBox.Show(this, "Файл занят другой программой. Закройте PDF и повторите.", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось применить водяной знак: {ex.Message}");
                MessageBox.Show(this, $"Не удалось применить водяной знак: {ex.Message}", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ProcessWatermark(OrderData order, OrderFileItem item, bool isVertical)
        {
            try
            {
                if (!HasExistingFile(item.PrintPath))
                {
                    SetBottomStatus("Файл печати item не найден");
                    MessageBox.Show(this, "Файл печати item не найден.", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var originalPrintPath = order.PrintPath;
                try
                {
                    order.PrintPath = item.PrintPath ?? string.Empty;
                    PdfWatermark.Apply(order, isVertical);
                }
                finally
                {
                    order.PrintPath = originalPrintPath;
                }

                var pos = isVertical ? "слева" : "сверху";
                var fileName = Path.GetFileName(item.PrintPath);
                SetBottomStatus($"Водяной знак ({pos}) нанесен на {fileName}");
            }
            catch (IOException)
            {
                SetBottomStatus("Файл занят другой программой. Закройте PDF и повторите");
                MessageBox.Show(this, "Файл занят другой программой. Закройте PDF и повторите.", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось применить водяной знак: {ex.Message}");
                MessageBox.Show(this, $"Не удалось применить водяной знак: {ex.Message}", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemovePitStopAction(OrderData order)
        {
            order.PitStopAction = "-";
            if (order.Items != null)
            {
                foreach (var item in order.Items.Where(x => x != null))
                    item.PitStopAction = "-";
            }

            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus($"PitStop очищен для {GetOrderDisplayId(order)}");
        }

        private void RemoveImposingAction(OrderData order)
        {
            order.ImposingAction = "-";
            if (order.Items != null)
            {
                foreach (var item in order.Items.Where(x => x != null))
                    item.ImposingAction = "-";
            }

            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus($"Imposing очищен для {GetOrderDisplayId(order)}");
        }

        private void RemovePitStopAction(OrderData order, OrderFileItem item)
        {
            item.PitStopAction = "-";
            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
            SetBottomStatus($"PitStop очищен для item {item.ClientFileLabel}");
        }

        private void RemoveImposingAction(OrderData order, OrderFileItem item)
        {
            item.ImposingAction = "-";
            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
            SetBottomStatus($"Imposing очищен для item {item.ClientFileLabel}");
        }

        private string CopyToGrandpa(OrderData order)
        {
            if (!HasExistingFile(order.PrintPath))
            {
                SetBottomStatus("Файл печати не найден");
                MessageBox.Show(this, "Файл печати не найден.", "Копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return string.Empty;
            }

            var sourcePath = order.PrintPath ?? string.Empty;
            var targetName = Path.GetFileName(sourcePath);
            return CopyToGrandpaFromSource(sourcePath, targetName);
        }

        private string CopyToGrandpa(OrderData order, OrderFileItem item)
        {
            if (!HasExistingFile(item.PrintPath))
            {
                SetBottomStatus("Файл печати item не найден");
                MessageBox.Show(this, "Файл печати item не найден.", "Копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return string.Empty;
            }

            var sourcePath = item.PrintPath ?? string.Empty;
            var targetName = Path.GetFileName(sourcePath);
            return CopyToGrandpaFromSource(sourcePath, targetName);
        }

        private void DgvJobs_MouseDown(object? sender, MouseEventArgs e)
        {
            _dragBoxFromMouseDown = Rectangle.Empty;
            _dragSourceRowIndex = -1;
            _dragSourceColumnIndex = -1;

            if (e.Button != MouseButtons.Left)
                return;

            var hit = dgvJobs.HitTest(e.X, e.Y);
            if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
                return;

            var stage = GetStageByColumnIndex(hit.ColumnIndex);
            if (stage == 0)
                return;

            var rowTag = dgvJobs.Rows[hit.RowIndex].Tag?.ToString();
            if (string.IsNullOrWhiteSpace(rowTag))
                return;

            if (!IsOrderTag(rowTag))
                return;

            _dragSourceRowIndex = hit.RowIndex;
            _dragSourceColumnIndex = hit.ColumnIndex;

            var dragSize = SystemInformation.DragSize;
            _dragBoxFromMouseDown = new Rectangle(
                new Point(e.X - (dragSize.Width / 2), e.Y - (dragSize.Height / 2)),
                dragSize);
        }

        private void DgvJobs_MouseMove(object? sender, MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) != MouseButtons.Left)
                return;

            if (_dragBoxFromMouseDown == Rectangle.Empty || _dragBoxFromMouseDown.Contains(e.X, e.Y))
                return;

            if (_dragSourceRowIndex < 0 || _dragSourceColumnIndex < 0)
                return;

            var stage = GetStageByColumnIndex(_dragSourceColumnIndex);
            if (stage == 0)
                return;

            var sourcePath = GetDragSourceFilePath(_dragSourceRowIndex, stage);
            if (!HasExistingFile(sourcePath))
                return;

            var dragData = new DataObject();
            dragData.SetData(DataFormats.FileDrop, new[] { sourcePath });
            dragData.SetData("InternalSourceColumn", _dragSourceColumnIndex);
            dragData.SetData("InternalSourceRow", _dragSourceRowIndex);

            _dragBoxFromMouseDown = Rectangle.Empty;
            dgvJobs.DoDragDrop(dragData, DragDropEffects.Copy | DragDropEffects.Move);
        }

        private void InitializeStatusFilter()
        {
            lblFStatus.Click += LblFStatus_Click;
            picFStatusGlyph.Click += LblFStatus_Click;
            ApplyStatusFilterChevronIcon();
            UpdateStatusFilterCaption();
        }

        private void ApplyStatusFilterChevronIcon()
        {
            using var icon = CreateDropDownGlyphIcon(24);
            picFStatusGlyph.Image?.Dispose();
            picFStatusGlyph.Image = (Image)icon.Clone();
            lblFStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblFStatus.Padding = new Padding(0, 3, 0, 0);
        }

        private void InitializeOrderNoSearch()
        {
            lblFOrderNo.Click += LblFOrderNo_Click;
            picFOrderNoGlyph.Click += LblFOrderNo_Click;
            ApplyOrderNoSearchIcon();
            UpdateOrderNoSearchCaption();
        }

        private void InitializeUserFilter()
        {
            var insertIndex = flpFilters.Controls.IndexOf(cbUser);
            if (insertIndex >= 0)
                flpFilters.Controls.Remove(cbUser);

            cbUser.Visible = false;

            _userFilterGlyph = new PictureBox
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 0, 0, 0),
                Name = "picFUserGlyph",
                Size = new Size(24, 33),
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            _userFilterLabel = new Label
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 3, 0),
                Name = "lblFUser",
                Size = new Size(150, 33),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _userFilterGlyph.Click += LblFUser_Click;
            _userFilterLabel.Click += LblFUser_Click;

            flpFilters.Controls.Add(_userFilterGlyph);
            flpFilters.Controls.Add(_userFilterLabel);
            if (insertIndex >= 0)
            {
                flpFilters.Controls.SetChildIndex(_userFilterGlyph, insertIndex);
                flpFilters.Controls.SetChildIndex(_userFilterLabel, insertIndex + 1);
            }

            ApplyUserFilterChevronIcon();
            UpdateUserFilterCaption();
        }

        private void InitializeCreatedDateFilter()
        {
            var insertIndex = flpFilters.Controls.IndexOf(cbFCreated);
            if (insertIndex >= 0)
                flpFilters.Controls.Remove(cbFCreated);

            cbFCreated.Visible = false;

            _createdFilterGlyph = new PictureBox
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 0, 0, 0),
                Name = "picFCreatedGlyph",
                Size = new Size(24, 33),
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            _createdFilterLabel = new Label
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 3, 0),
                Name = "lblFCreated",
                Size = new Size(170, 33),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _createdFilterGlyph.Click += LblFCreated_Click;
            _createdFilterLabel.Click += LblFCreated_Click;

            flpFilters.Controls.Add(_createdFilterGlyph);
            flpFilters.Controls.Add(_createdFilterLabel);
            if (insertIndex >= 0)
            {
                flpFilters.Controls.SetChildIndex(_createdFilterGlyph, insertIndex);
                flpFilters.Controls.SetChildIndex(_createdFilterLabel, insertIndex + 1);
            }

            ApplyCreatedDateFilterChevronIcon();
            UpdateCreatedDateFilterCaption();
        }

        private void InitializeReceivedDateFilter()
        {
            var insertIndex = flpFilters.Controls.IndexOf(cbFReceived);
            if (insertIndex >= 0)
                flpFilters.Controls.Remove(cbFReceived);

            cbFReceived.Visible = false;

            _receivedFilterGlyph = new PictureBox
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 0, 0, 0),
                Name = "picFReceivedGlyph",
                Size = new Size(24, 33),
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            _receivedFilterLabel = new Label
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 3, 0),
                Name = "lblFReceived",
                Size = new Size(170, 33),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _receivedFilterGlyph.Click += LblFReceived_Click;
            _receivedFilterLabel.Click += LblFReceived_Click;

            flpFilters.Controls.Add(_receivedFilterGlyph);
            flpFilters.Controls.Add(_receivedFilterLabel);
            if (insertIndex >= 0)
            {
                flpFilters.Controls.SetChildIndex(_receivedFilterGlyph, insertIndex);
                flpFilters.Controls.SetChildIndex(_receivedFilterLabel, insertIndex + 1);
            }

            ApplyReceivedDateFilterChevronIcon();
            UpdateReceivedDateFilterCaption();
        }

        private void ApplyOrderNoSearchIcon()
        {
            using var icon = CreateDropDownGlyphIcon(24);
            picFOrderNoGlyph.Image?.Dispose();
            picFOrderNoGlyph.Image = (Image)icon.Clone();
            lblFOrderNo.TextAlign = ContentAlignment.MiddleLeft;
            lblFOrderNo.Padding = new Padding(0, 3, 0, 0);
        }

        private void ApplyUserFilterChevronIcon()
        {
            if (_userFilterGlyph == null || _userFilterLabel == null)
                return;

            using var icon = CreateDropDownGlyphIcon(24);
            _userFilterGlyph.Image?.Dispose();
            _userFilterGlyph.Image = (Image)icon.Clone();
            _userFilterLabel.TextAlign = ContentAlignment.MiddleLeft;
            _userFilterLabel.Padding = new Padding(0, 3, 0, 0);
        }

        private void ApplyCreatedDateFilterChevronIcon()
        {
            if (_createdFilterGlyph == null || _createdFilterLabel == null)
                return;

            using var icon = CreateDropDownGlyphIcon(24);
            _createdFilterGlyph.Image?.Dispose();
            _createdFilterGlyph.Image = (Image)icon.Clone();
            _createdFilterLabel.TextAlign = ContentAlignment.MiddleLeft;
            _createdFilterLabel.Padding = new Padding(0, 3, 0, 0);
        }

        private void ApplyReceivedDateFilterChevronIcon()
        {
            if (_receivedFilterGlyph == null || _receivedFilterLabel == null)
                return;

            using var icon = CreateDropDownGlyphIcon(24);
            _receivedFilterGlyph.Image?.Dispose();
            _receivedFilterGlyph.Image = (Image)icon.Clone();
            _receivedFilterLabel.TextAlign = ContentAlignment.MiddleLeft;
            _receivedFilterLabel.Padding = new Padding(0, 3, 0, 0);
        }

        private static Bitmap CreateDropDownGlyphIcon(int iconSize)
        {
            var svgPath = ResolveDropDownGlyphSvgPath();
            if (!string.IsNullOrWhiteSpace(svgPath))
            {
                try
                {
                    var svg = SvgDocument.Open<SvgDocument>(svgPath);
                    svg.Width = iconSize;
                    svg.Height = iconSize;
                    var rendered = svg.Draw(iconSize, iconSize);
                    if (rendered != null)
                        return rendered;
                }
                catch
                {
                    // Если SVG недоступен/поврежден, используем внутреннюю отрисовку.
                }
            }

            return CreateFallbackDropDownGlyphIcon(iconSize);
        }

        private static string? ResolveDropDownGlyphSvgPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Icons", "arrow_drop_down.svg"),
                Path.Combine(AppContext.BaseDirectory, "arrow_drop_down.svg")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static Bitmap CreateFallbackDropDownGlyphIcon(int iconSize)
        {
            var bitmap = new Bitmap(iconSize, iconSize);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var centerX = iconSize / 2f;
            var centerY = (iconSize / 2f) - 0.8f;
            var halfWidth = iconSize * 0.24f;
            var vertical = iconSize * 0.17f;

            using var pen = new Pen(Color.FromArgb(31, 31, 31), 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            graphics.DrawLines(
                pen,
                [
                    new PointF(centerX - halfWidth, centerY - vertical),
                    new PointF(centerX, centerY + vertical),
                    new PointF(centerX + halfWidth, centerY - vertical)
                ]);

            return bitmap;
        }

        private void PopulateQueueTree()
        {
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();

            foreach (var userName in _users)
            {
                var userNode = new TreeNode(userName);
                foreach (var statusName in QueueStatuses)
                    userNode.Nodes.Add(statusName);

                userNode.Expand();
                treeView1.Nodes.Add(userNode);
            }

            treeView1.EndUpdate();
        }

        // cbQueue всегда содержит статусы выбранного в дереве пользователя.
        private void SelectUser(TreeNode userNode, string? preferredStatus = null)
        {
            _currentUserName = userNode.Text;

            var targetStatus = string.IsNullOrWhiteSpace(preferredStatus)
                ? QueueStatuses[0]
                : preferredStatus;

            FillQueueCombo(targetStatus);
            treeView1.Invalidate();
        }

        private void TreeView1_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (_isSyncingQueueSelection || e.Node == null)
                return;

            var userNode = e.Node.Level == 0 ? e.Node : e.Node.Parent;
            if (userNode == null)
                return;

            var preferredStatus = e.Node.Level == 0
                ? GetSelectedQueueStatusName()
                : e.Node.Text;

            _isSyncingQueueSelection = true;
            SelectUser(userNode, preferredStatus);
            _isSyncingQueueSelection = false;
            HandleOrdersGridChanged();
        }

        private void CbQueue_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isSyncingQueueSelection || cbQueue.SelectedItem is not QueueStatusItem selectedItem)
                return;

            var selectedStatus = selectedItem.StatusName;
            var userNode = FindUserNode(_currentUserName);
            if (userNode == null && treeView1.SelectedNode != null)
                userNode = treeView1.SelectedNode.Level == 0 ? treeView1.SelectedNode : treeView1.SelectedNode.Parent;

            if (userNode == null)
                return;

            var statusNode = FindStatusNode(userNode, selectedStatus);
            if (statusNode == null)
                return;

            _isSyncingQueueSelection = true;
            userNode.Expand();
            treeView1.SelectedNode = statusNode;
            statusNode.EnsureVisible();
            _isSyncingQueueSelection = false;
            HandleOrdersGridChanged();
        }

        private TreeNode? FindUserNode(string userName)
        {
            foreach (TreeNode node in treeView1.Nodes)
            {
                if (string.Equals(node.Text, userName, StringComparison.Ordinal))
                    return node;
            }

            return null;
        }

        private static TreeNode? FindStatusNode(TreeNode userNode, string statusName)
        {
            foreach (TreeNode child in userNode.Nodes)
            {
                if (string.Equals(child.Text, statusName, StringComparison.Ordinal))
                    return child;
            }

            return null;
        }

        private void TreeView1_DrawNode(object? sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null)
                return;

            var isRoot = e.Node.Level == 0;
            var isSelected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
            var rowRect = new Rectangle(0, e.Bounds.Top, treeView1.ClientSize.Width, e.Bounds.Height);

            var backColor = QueuePanelBackColor;
            if (isRoot)
                backColor = QueueHeaderBackColor;
            if (isSelected && !isRoot)
                backColor = QueueStatusSelectedBackColor;

            using var backBrush = new SolidBrush(backColor);
            e.Graphics.FillRectangle(backBrush, rowRect);

            var textValue = isRoot ? e.Node.Text : FormatQueueLabel(e.Node.Text);
            using var textFont = new Font(
                "Segoe UI",
                isRoot ? 22f : 18f,
                isRoot || isSelected ? FontStyle.Bold : FontStyle.Regular,
                GraphicsUnit.Pixel);

            var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, treeView1.ClientSize.Width - e.Bounds.X - 16, e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                textValue,
                textFont,
                textRect,
                QueueTextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (!isRoot)
            {
                var countText = GetQueueStatusCountText(e.Node.Text);
                using var countFont = new Font("Segoe UI", 18f, isSelected ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                var countRect = new Rectangle(0, e.Bounds.Y, treeView1.ClientSize.Width - 16, e.Bounds.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    countText,
                    countFont,
                    countRect,
                    QueueTextColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            if ((e.State & TreeNodeStates.Focused) == TreeNodeStates.Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, rowRect, QueueTextColor, backColor);
        }

        private void DgvJobs_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (_isRebuildingGrid)
                return;

            if (e.ColumnIndex == colStatus.Index || e.ColumnIndex == colCreated.Index || e.ColumnIndex == colReceived.Index || e.ColumnIndex < 0)
                HandleOrdersGridChanged();
        }

        private void DgvJobs_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0)
                return;

            if (e.Graphics == null)
                return;

            var graphics = e.Graphics;
            var mouseClient = dgvJobs.PointToClient(Cursor.Position);
            var hit = dgvJobs.HitTest(mouseClient.X, mouseClient.Y);
            var isHoveredHeader = hit.RowIndex == -1 && hit.ColumnIndex == e.ColumnIndex;

            if (isHoveredHeader)
            {
                // Keep native hover look on the header under cursor.
                e.Paint(e.CellBounds, e.PaintParts);
            }

            if (!isHoveredHeader)
            {
                using var backBrush = new SolidBrush(Color.White);
                graphics.FillRectangle(backBrush, e.CellBounds);
                e.Paint(
                    e.CellBounds,
                    DataGridViewPaintParts.ContentForeground |
                    DataGridViewPaintParts.ErrorIcon |
                    DataGridViewPaintParts.Focus);
            }

            // Draw header separators in black.
            using var gridPen = new Pen(Color.Black);
            if (e.ColumnIndex == 0)
                graphics.DrawLine(gridPen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Left, e.CellBounds.Bottom - 1);
            graphics.DrawLine(gridPen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Top);
            graphics.DrawLine(gridPen, e.CellBounds.Right - 1, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
            graphics.DrawLine(gridPen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
            e.Handled = true;
        }

        private void DgvJobs_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var row = dgvJobs.Rows[e.RowIndex];
            var rowBackColor = row.Selected
                ? OrdersRowSelectedBackColor
                : (e.RowIndex == _hoveredRowIndex ? OrdersRowHoverBackColor : Color.White);

            if (e.CellStyle == null)
                return;

            e.CellStyle.BackColor = rowBackColor;
            e.CellStyle.SelectionBackColor = OrdersRowSelectedBackColor;
            e.CellStyle.SelectionForeColor = Color.Black;
        }

        private void DgvJobs_CellMouseEnter(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            var isFileColumn = e.ColumnIndex == colSource.Index
                || e.ColumnIndex == colPrep.Index
                || e.ColumnIndex == colPrint.Index;
            dgvJobs.Cursor = isFileColumn ? Cursors.Hand : Cursors.Default;

            var rowTag = dgvJobs.Rows[e.RowIndex].Tag?.ToString();
            if (!IsOrderTag(rowTag))
            {
                if (_hoveredRowIndex != -1)
                {
                    var oldIndex = _hoveredRowIndex;
                    _hoveredRowIndex = -1;
                    if (oldIndex >= 0 && oldIndex < dgvJobs.Rows.Count)
                        dgvJobs.InvalidateRow(oldIndex);
                }
                return;
            }

            if (e.RowIndex == _hoveredRowIndex)
                return;

            var prevIndex = _hoveredRowIndex;
            _hoveredRowIndex = e.RowIndex;
            if (prevIndex >= 0 && prevIndex < dgvJobs.Rows.Count)
                dgvJobs.InvalidateRow(prevIndex);
            dgvJobs.InvalidateRow(_hoveredRowIndex);
        }

        private void DgvJobs_CellMouseLeave(object? sender, EventArgs e)
        {
            dgvJobs.Cursor = Cursors.Default;

            if (_hoveredRowIndex == -1)
                return;

            var oldIndex = _hoveredRowIndex;
            _hoveredRowIndex = -1;
            if (oldIndex >= 0 && oldIndex < dgvJobs.Rows.Count)
                dgvJobs.InvalidateRow(oldIndex);
        }

        private void DgvJobs_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            var columnName = dgvJobs.Columns[e.ColumnIndex].Name;
            if (string.Equals(columnName, "colPitstop", StringComparison.Ordinal))
            {
                SelectPitStopActionFromGrid(e.RowIndex);
                return;
            }

            if (string.Equals(columnName, "colHotimposing", StringComparison.Ordinal))
            {
                SelectImposingActionFromGrid(e.RowIndex);
                return;
            }

            if (e.ColumnIndex == colOrderNumber.Index)
            {
                EditOrderFromGrid(e.RowIndex);
                return;
            }
        }

        private void SelectPitStopActionFromGrid(int rowIndex)
        {
            var order = GetOrderByRowIndex(rowIndex);
            if (order == null)
                return;

            using var form = new PitStopSelectForm(order.PitStopAction);
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            var selected = NormalizeAction(form.SelectedName);
            order.PitStopAction = selected;
            if (order.Items != null)
            {
                foreach (var entry in order.Items.Where(x => x != null))
                    entry.PitStopAction = selected;
            }

            PersistGridChanges($"order|{order.InternalId}");
        }

        private void SelectImposingActionFromGrid(int rowIndex)
        {
            var order = GetOrderByRowIndex(rowIndex);
            if (order == null)
                return;

            using var form = new ImposingSelectForm(order.ImposingAction);
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            var selected = NormalizeAction(form.SelectedName);
            order.ImposingAction = selected;
            if (order.Items != null)
            {
                foreach (var entry in order.Items.Where(x => x != null))
                    entry.ImposingAction = selected;
            }

            PersistGridChanges($"order|{order.InternalId}");
        }

        private void DgvJobs_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            if (!string.Equals(dgvJobs.Columns[e.ColumnIndex].Name, "colStatus", StringComparison.Ordinal))
                return;

            var rowTag = dgvJobs.Rows[e.RowIndex].Tag?.ToString();
            if (!IsOrderTag(rowTag))
                return;

            var order = GetOrderByRowIndex(e.RowIndex);
            if (order == null)
                return;

            var statusText = order.Status ?? string.Empty;
            if (!statusText.Contains("Ошибка", StringComparison.OrdinalIgnoreCase))
                return;

            var reason = string.IsNullOrWhiteSpace(order.LastStatusReason) ? "Причина не указана" : order.LastStatusReason;
            var source = string.IsNullOrWhiteSpace(order.LastStatusSource) ? "неизвестно" : order.LastStatusSource;
            var stamp = order.LastStatusAt == default
                ? "неизвестно"
                : order.LastStatusAt.ToString("dd.MM.yyyy HH:mm:ss");

            e.ToolTipText = $"{statusText}\nИсточник: {source}\nПричина: {reason}\nВремя: {stamp}";
        }

        private async void DgvJobs_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var stage = GetStageByColumnIndex(e.ColumnIndex);
            if (stage == 0)
                return;

            var rowTag = dgvJobs.Rows[e.RowIndex].Tag?.ToString();
            var order = GetOrderByRowIndex(e.RowIndex);
            if (order == null || string.IsNullOrWhiteSpace(rowTag))
                return;

            try
            {
                if (!IsOrderTag(rowTag))
                    return;

                var orderPath = GetOrderStagePath(order, stage);
                if (HasExistingFile(orderPath))
                {
                    OpenFileDefault(orderPath);
                    return;
                }

                await PickAndCopyFileForOrderAsync(order, stage);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось обработать действие по файлу: {ex.Message}");
                MessageBox.Show(this, $"Не удалось обработать действие по файлу: {ex.Message}", "Файловая операция", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DgvJobs_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void DgvJobs_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var clientPoint = dgvJobs.PointToClient(new Point(e.X, e.Y));
            var hit = dgvJobs.HitTest(clientPoint.X, clientPoint.Y);
            if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var stage = GetStageByColumnIndex(hit.ColumnIndex);
            if (stage == 0)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var rowTag = dgvJobs.Rows[hit.RowIndex].Tag?.ToString();
            var order = GetOrderByRowIndex(hit.RowIndex);
            if (order == null || string.IsNullOrWhiteSpace(rowTag))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if (!IsOrderTag(rowTag))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var draggingFile = (e.Data.GetData(DataFormats.FileDrop) as string[])?.FirstOrDefault();
            draggingFile = CleanPath(draggingFile);
            if (string.IsNullOrWhiteSpace(draggingFile))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var existingPath = GetOrderStagePath(order, stage);

            e.Effect = PathsEqual(existingPath, draggingFile)
                ? DragDropEffects.None
                : DragDropEffects.Copy;
        }

        private async void DgvJobs_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true)
                return;

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var sourceFile = CleanPath(files?.FirstOrDefault());
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
                return;

            var clientPoint = dgvJobs.PointToClient(new Point(e.X, e.Y));
            var hit = dgvJobs.HitTest(clientPoint.X, clientPoint.Y);
            if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
                return;

            var stage = GetStageByColumnIndex(hit.ColumnIndex);
            if (stage == 0)
                return;

            var row = dgvJobs.Rows[hit.RowIndex];
            var rowTag = row.Tag?.ToString();
            var order = GetOrderByRowIndex(hit.RowIndex);
            if (order == null || string.IsNullOrWhiteSpace(rowTag))
                return;

            try
            {
                if (!IsOrderTag(rowTag))
                    return;

                if (!await AddFileToOrderAsync(order, sourceFile, stage))
                    return;

                PersistGridChanges($"order|{order.InternalId}");
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось добавить файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось добавить файл: {ex.Message}", "Drag&Drop", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task PickAndCopyFileForOrderAsync(OrderData order, int stage)
        {
            var targetFolder = GetStageFolder(order, stage);
            Directory.CreateDirectory(targetFolder);

            using var ofd = new OpenFileDialog
            {
                Filter = "PDF|*.pdf|Все файлы|*.*",
                InitialDirectory = targetFolder,
                RestoreDirectory = false
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            if (!await AddFileToOrderAsync(order, ofd.FileName, stage))
                return;

            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus("Файл добавлен в заказ");
        }

        private async Task PickAndCopyFileForItemAsync(OrderData order, OrderFileItem item, int stage)
        {
            var targetFolder = GetStageFolder(order, stage);
            Directory.CreateDirectory(targetFolder);

            using var ofd = new OpenFileDialog
            {
                Filter = "PDF|*.pdf|Все файлы|*.*",
                InitialDirectory = targetFolder,
                RestoreDirectory = false
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            if (!await AddFileToItemAsync(order, item, ofd.FileName, stage))
                return;

            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
            SetBottomStatus("Файл добавлен в item");
        }

        private async Task<bool> AddFileToOrderAsync(OrderData order, string sourceFile, int stage)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                return false;

            if (stage == 3 && !await EnsureSimpleOrderInfoForPrintAsync(order))
                return false;

            string targetName;
            if (stage == 3 && !string.IsNullOrWhiteSpace(order.Id))
                targetName = $"{order.Id}{Path.GetExtension(cleanSource)}";
            else
                targetName = EnsureUniqueStageFileName(order, stage, Path.GetFileName(cleanSource));

            var newPath = stage == 3
                ? CopyPrintFile(order, cleanSource, targetName)
                : CopyIntoStage(order, stage, cleanSource, targetName);

            if (stage == 2)
                EnsureSourceCopy(order, cleanSource);

            UpdateOrderFilePath(order, stage, newPath);
            return true;
        }

        private async Task<bool> AddFileToItemAsync(OrderData order, OrderFileItem item, string sourceFile, int stage)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                return false;

            if (stage == 3 && !await EnsureSimpleOrderInfoForPrintAsync(order))
                return false;

            if (string.IsNullOrWhiteSpace(item.ClientFileLabel))
                item.ClientFileLabel = Path.GetFileNameWithoutExtension(cleanSource);

            string newPath;
            if (stage == 3)
            {
                var printName = EnsureUniqueStageFileName(order, 3, BuildItemPrintFileName(order, item, cleanSource));
                newPath = CopyPrintFile(order, cleanSource, printName);
            }
            else
            {
                var targetName = EnsureUniqueStageFileName(order, stage, Path.GetFileName(cleanSource));
                newPath = CopyIntoStage(order, stage, cleanSource, targetName);
            }

            UpdateItemFilePath(order, item, stage, newPath);
            return true;
        }

        private void PersistGridChanges(string selectedTag)
        {
            SaveHistory();
            RebuildOrdersGrid();
            if (!string.IsNullOrWhiteSpace(selectedTag))
                TryRestoreSelectedRowByTag(selectedTag);
        }

        private int GetStageByColumnIndex(int columnIndex)
        {
            var colName = dgvJobs.Columns[columnIndex].Name;
            return colName switch
            {
                "colSource" => 1,
                "colPrep" => 2,
                "colPrint" => 3,
                _ => 0
            };
        }

        private OrderData? GetOrderByRowIndex(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= dgvJobs.Rows.Count)
                return null;

            var rowTag = dgvJobs.Rows[rowIndex].Tag?.ToString();
            if (!IsOrderTag(rowTag))
                return null;

            var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
            return FindOrderByInternalId(orderInternalId);
        }

        private static string GetOrderStagePath(OrderData order, int stage)
        {
            return stage switch
            {
                1 => order.SourcePath ?? string.Empty,
                2 => order.PreparedPath ?? string.Empty,
                3 => order.PrintPath ?? string.Empty,
                _ => string.Empty
            };
        }

        private static string GetItemStagePath(OrderFileItem item, int stage)
        {
            return stage switch
            {
                1 => item.SourcePath ?? string.Empty,
                2 => item.PreparedPath ?? string.Empty,
                3 => item.PrintPath ?? string.Empty,
                _ => string.Empty
            };
        }

        private void RemoveFileFromOrder(OrderData order, int stage)
        {
            var currentPath = GetOrderStagePath(order, stage);
            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            var decision = MessageBox.Show(
                this,
                $"Удалить файл {Path.GetFileName(currentPath)}?",
                "Удаление файла",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (decision != DialogResult.Yes)
                return;

            try
            {
                if (File.Exists(currentPath))
                    File.Delete(currentPath);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось удалить файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось удалить файл: {ex.Message}", "Удаление файла", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateOrderFilePath(order, stage, string.Empty);
            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus("Файл удален");
        }

        private void RemoveFileFromItem(OrderData order, OrderFileItem item, int stage)
        {
            var currentPath = GetItemStagePath(item, stage);
            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            var decision = MessageBox.Show(
                this,
                $"Удалить файл {Path.GetFileName(currentPath)}?",
                "Удаление файла",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (decision != DialogResult.Yes)
                return;

            try
            {
                if (File.Exists(currentPath))
                    File.Delete(currentPath);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось удалить файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось удалить файл: {ex.Message}", "Удаление файла", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateItemFilePath(order, item, stage, string.Empty);
            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
            SetBottomStatus("Файл item удален");
        }

        private void RenameFileForOrder(OrderData order, int stage)
        {
            var currentPath = GetOrderStagePath(order, stage);
            if (!HasExistingFile(currentPath))
                return;

            if (!TryBuildRenamedPath(currentPath, out var renamedPath))
                return;

            try
            {
                File.Move(currentPath, renamedPath);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось переименовать файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось переименовать файл: {ex.Message}", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateOrderFilePath(order, stage, renamedPath);
            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus("Файл переименован");
        }

        private void RenameFileForItem(OrderData order, OrderFileItem item, int stage)
        {
            var currentPath = GetItemStagePath(item, stage);
            if (!HasExistingFile(currentPath))
                return;

            if (!TryBuildRenamedPath(currentPath, out var renamedPath))
                return;

            try
            {
                File.Move(currentPath, renamedPath);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось переименовать файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось переименовать файл: {ex.Message}", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateItemFilePath(order, item, stage, renamedPath);
            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
            SetBottomStatus("Файл item переименован");
        }

        private bool TryBuildRenamedPath(string currentPath, out string renamedPath)
        {
            renamedPath = string.Empty;
            if (!HasExistingFile(currentPath))
                return false;

            var oldName = Path.GetFileNameWithoutExtension(currentPath);
            var extension = Path.GetExtension(currentPath);
            var nextName = ShowInputDialog("Переименование", "Введите новое имя файла:", oldName);
            if (string.IsNullOrWhiteSpace(nextName))
                return false;

            nextName = nextName.Trim();
            if (string.Equals(nextName, oldName, StringComparison.Ordinal))
                return false;

            foreach (var invalid in Path.GetInvalidFileNameChars())
                nextName = nextName.Replace(invalid, '_');

            var directory = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            var targetPath = Path.Combine(directory, nextName + extension);
            if (PathsEqual(currentPath, targetPath))
                return false;

            if (File.Exists(targetPath))
            {
                SetBottomStatus("Файл с таким именем уже существует");
                MessageBox.Show(this, "Файл с таким именем уже существует.", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            renamedPath = targetPath;
            return true;
        }

        private string ShowInputDialog(string title, string promptText, string initialValue)
        {
            using var form = new Form();
            using var promptLabel = new Label();
            using var inputTextBox = new TextBox();
            using var okButton = new Button();
            using var cancelButton = new Button();

            form.Text = title;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = new Size(430, 150);
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.ShowInTaskbar = false;

            promptLabel.Text = promptText;
            promptLabel.SetBounds(16, 16, 398, 22);

            inputTextBox.Text = initialValue;
            inputTextBox.SetBounds(16, 46, 398, 26);

            okButton.Text = "ОК";
            okButton.DialogResult = DialogResult.OK;
            okButton.SetBounds(238, 96, 82, 32);

            cancelButton.Text = "Отмена";
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.SetBounds(332, 96, 82, 32);

            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;
            form.Controls.AddRange([promptLabel, inputTextBox, okButton, cancelButton]);

            return form.ShowDialog(this) == DialogResult.OK
                ? inputTextBox.Text
                : initialValue;
        }

        private void CopyPathToClipboard(OrderData order, int stage)
        {
            CopyExistingPathToClipboard(GetOrderStagePath(order, stage));
        }

        private void CopyPathToClipboard(OrderFileItem item, int stage)
        {
            CopyExistingPathToClipboard(GetItemStagePath(item, stage));
        }

        private void CopyExistingPathToClipboard(string? path)
        {
            if (!HasExistingFile(path))
            {
                SetBottomStatus("Путь к файлу не найден");
                MessageBox.Show(this, "Путь к файлу не найден.", "Буфер обмена", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            TrySetClipboardText(path);
            SetBottomStatus("Путь скопирован в буфер");
        }

        private async Task PasteFileFromClipboardAsync(OrderData order, int stage)
        {
            var clipboardFilePath = TryGetClipboardFilePath();
            if (string.IsNullOrWhiteSpace(clipboardFilePath))
                return;

            if (!await AddFileToOrderAsync(order, clipboardFilePath, stage))
                return;

            PersistGridChanges($"order|{order.InternalId}");
        }

        private async Task PasteFileFromClipboardAsync(OrderData order, OrderFileItem item, int stage)
        {
            var clipboardFilePath = TryGetClipboardFilePath();
            if (string.IsNullOrWhiteSpace(clipboardFilePath))
                return;

            if (!await AddFileToItemAsync(order, item, clipboardFilePath, stage))
                return;

            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
        }

        private string? TryGetClipboardFilePath()
        {
            string clipboardText;
            try
            {
                clipboardText = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось прочитать буфер обмена: {ex.Message}");
                MessageBox.Show(this, $"Не удалось прочитать буфер обмена: {ex.Message}", "Буфер обмена", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            var cleanPath = CleanPath(clipboardText?.Replace("\"", string.Empty));
            if (string.IsNullOrWhiteSpace(cleanPath))
            {
                SetBottomStatus("Буфер обмена пуст");
                MessageBox.Show(this, "Буфер обмена пуст.", "Буфер обмена", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            if (!File.Exists(cleanPath))
            {
                SetBottomStatus("Файл не найден по указанному пути");
                MessageBox.Show(this, $"Файл не найден по указанному пути:\n{cleanPath}", "Буфер обмена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            return cleanPath;
        }

        private string GetDragSourceFilePath(int rowIndex, int stage)
        {
            if (rowIndex < 0 || rowIndex >= dgvJobs.Rows.Count)
                return string.Empty;

            var rowTag = dgvJobs.Rows[rowIndex].Tag?.ToString();
            if (string.IsNullOrWhiteSpace(rowTag))
                return string.Empty;

            var order = GetOrderByRowIndex(rowIndex);
            if (order == null)
                return string.Empty;

            if (!IsOrderTag(rowTag))
                return string.Empty;

            return GetOrderStagePath(order, stage);
        }

        private static void SetItemStagePath(OrderFileItem item, int stage, string path)
        {
            if (stage == 1)
                item.SourcePath = path;
            else if (stage == 2)
                item.PreparedPath = path;
            else if (stage == 3)
                item.PrintPath = path;
        }

        private void UpdateOrderFilePath(OrderData order, int stage, string path)
        {
            if (stage == 1)
                order.SourcePath = path;
            else if (stage == 2)
                order.PreparedPath = path;
            else if (stage == 3)
                order.PrintPath = path;

            var status = ResolveWorkflowStatus(order.SourcePath, order.PreparedPath, order.PrintPath);
            SetOrderStatus(order, status, "file-sync", $"stage-{stage}", persistHistory: false, rebuildGrid: false);

            if (order.Items != null && order.Items.Count == 1)
            {
                var singleItem = order.Items[0];
                SetItemStagePath(singleItem, stage, path);
                singleItem.FileStatus = status;
                singleItem.UpdatedAt = DateTime.Now;
            }
        }

        private void UpdateItemFilePath(OrderData order, OrderFileItem item, int stage, string path)
        {
            SetItemStagePath(item, stage, path);
            item.FileStatus = ResolveWorkflowStatus(item.SourcePath, item.PreparedPath, item.PrintPath);
            item.UpdatedAt = DateTime.Now;

            if (order.Items != null && order.Items.Count == 1 && string.Equals(order.Items[0].ItemId, item.ItemId, StringComparison.Ordinal))
            {
                if (stage == 1)
                    order.SourcePath = item.SourcePath;
                else if (stage == 2)
                    order.PreparedPath = item.PreparedPath;
                else if (stage == 3)
                    order.PrintPath = item.PrintPath;

                SetOrderStatus(order, item.FileStatus, "file-sync", $"item-stage-{stage}", persistHistory: false, rebuildGrid: false);
                return;
            }

            RefreshOrderStatusFromItems(order);
        }

        private void RefreshOrderStatusFromItems(OrderData order)
        {
            if (order.Items == null || order.Items.Count == 0)
            {
                var status = ResolveWorkflowStatus(order.SourcePath, order.PreparedPath, order.PrintPath);
                SetOrderStatus(order, status, "file-sync", "no-items", persistHistory: false, rebuildGrid: false);
                return;
            }

            var items = order.Items.Where(x => x != null).ToList();
            if (items.Count == 0)
            {
                SetOrderStatus(order, "Ожидание", "file-sync", "empty-items", persistHistory: false, rebuildGrid: false);
                return;
            }

            var total = items.Count;
            var done = items.Count(x => HasExistingFile(x.PrintPath));
            var active = items.Count(x => HasExistingFile(x.SourcePath) || HasExistingFile(x.PreparedPath) || HasExistingFile(x.PrintPath));

            var statusValue = done == total
                ? "Завершено"
                : active > 0
                    ? "Обрабатывается"
                    : "Ожидание";

            SetOrderStatus(order, statusValue, "file-sync", "aggregate", persistHistory: false, rebuildGrid: false);
        }

        private static string ResolveWorkflowStatus(string? sourcePath, string? preparedPath, string? printPath)
        {
            if (HasExistingFile(printPath))
                return "Завершено";

            if (HasExistingFile(preparedPath) || HasExistingFile(sourcePath))
                return "Обрабатывается";

            return "Ожидание";
        }

        private string GetStageFolder(OrderData order, int stage)
        {
            if (stage == 3 && HasExistingFile(order.PrintPath))
                return Path.GetDirectoryName(order.PrintPath) ?? GetTempStageFolder(stage);

            if (string.IsNullOrWhiteSpace(order.FolderName))
                return GetTempStageFolder(stage);

            var sub = stage switch
            {
                1 => "1. исходные",
                2 => "2. подготовка",
                3 => "3. печать",
                _ => string.Empty
            };

            var path = Path.Combine(_ordersRootPath, order.FolderName, sub);
            Directory.CreateDirectory(path);
            return path;
        }

        private string GetTempStageFolder(int stage)
        {
            var sub = stage switch
            {
                1 => "in",
                2 => "prepress",
                3 => "print",
                _ => string.Empty
            };

            var root = string.IsNullOrWhiteSpace(_tempRootPath)
                ? Path.Combine(_ordersRootPath, "_temp")
                : _tempRootPath;
            var path = Path.Combine(root, sub);
            Directory.CreateDirectory(path);
            return path;
        }

        private string CopyIntoStage(OrderData order, int stage, string sourceFile, string? targetName = null)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                throw new FileNotFoundException("Файл для копирования не найден.", cleanSource);

            var stageFolder = GetStageFolder(order, stage);
            Directory.CreateDirectory(stageFolder);

            var fileName = string.IsNullOrWhiteSpace(targetName) ? Path.GetFileName(cleanSource) : targetName;
            var destination = Path.Combine(stageFolder, fileName);

            if (PathsEqual(cleanSource, destination))
                return destination;

            if (File.Exists(destination))
                return destination;

            File.Copy(cleanSource, destination, true);
            return destination;
        }

        private string CopyPrintFile(OrderData order, string sourceFile, string targetName)
        {
            if (!UsesOrderFolderStorage(order))
                return CopyToGrandpaFromSource(sourceFile, targetName);

            return CopyIntoStage(order, 3, sourceFile, targetName);
        }

        private static bool UsesOrderFolderStorage(OrderData order)
        {
            return !string.IsNullOrWhiteSpace(order.FolderName);
        }

        private string CopyToGrandpaFromSource(string sourceFile, string targetName)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                throw new FileNotFoundException("Файл для копирования не найден.", cleanSource);

            var destinationRoot = string.IsNullOrWhiteSpace(_grandpaFolder)
                ? GetTempStageFolder(3)
                : _grandpaFolder;
            Directory.CreateDirectory(destinationRoot);

            var destination = Path.Combine(destinationRoot, targetName);

            if (PathsEqual(cleanSource, destination))
            {
                TrySetClipboardText(destination);
                SetBottomStatus("Скопировано в Дедушку");
                return destination;
            }

            if (File.Exists(destination))
            {
                TrySetClipboardText(destination);
                SetBottomStatus("Скопировано в Дедушку");
                return destination;
            }

            File.Copy(cleanSource, destination, true);
            TrySetClipboardText(destination);
            SetBottomStatus("Скопировано в Дедушку");
            return destination;
        }

        private static void TrySetClipboardText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                // Clipboard access should not fail file workflow.
            }
        }

        private string EnsureUniqueStageFileName(OrderData order, int stage, string fileName)
        {
            var folder = GetStageFolder(order, stage);
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(fileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var candidate = fileName;
            var index = 1;

            while (File.Exists(Path.Combine(folder, candidate)))
            {
                candidate = $"{baseName}_{index}{ext}";
                index++;
            }

            return candidate;
        }

        private string BuildItemPrintFileName(OrderData order, OrderFileItem item, string sourceFile)
        {
            var ext = Path.GetExtension(sourceFile);
            var orderNo = string.IsNullOrWhiteSpace(order.Id) ? "order" : order.Id;
            var orderedItems = (order.Items ?? []).OrderBy(x => x.SequenceNo).ToList();
            var idx = orderedItems.FindIndex(x => string.Equals(x.ItemId, item.ItemId, StringComparison.Ordinal));
            var itemIndex = idx >= 0 ? idx + 1 : 1;
            return $"{orderNo}_{itemIndex}{ext}";
        }

        private void EnsureSourceCopy(OrderData order, string sourceFile)
        {
            if (!string.IsNullOrWhiteSpace(order.SourcePath) && HasExistingFile(order.SourcePath))
                return;

            var newPath = CopyIntoStage(order, 1, sourceFile);
            UpdateOrderFilePath(order, 1, newPath);
        }

        private async Task<bool> EnsureSimpleOrderInfoForPrintAsync(OrderData order)
        {
            if (UsesOrderFolderStorage(order))
                return true;

            if (!string.IsNullOrWhiteSpace(order.Id))
                return true;

            using var form = new SimpleOrderForm(order);
            if (form.ShowDialog(this) != DialogResult.OK)
                return false;

            order.Id = form.OrderNumber.Trim();
            order.OrderDate = form.OrderDate;
            if (order.ArrivalDate == default)
                order.ArrivalDate = DateTime.Now;

            return !string.IsNullOrWhiteSpace(order.Id);
        }

        private static void OpenFileDefault(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }

        private static bool HasExistingFile(string? path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        private static string CleanPath(string? path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Trim('"');
        }

        private static bool PathsEqual(string? leftPath, string? rightPath)
        {
            if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
                return false;

            var left = NormalizePath(leftPath);
            var right = NormalizePath(rightPath);
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim();
            }
        }

        private void RefreshQueuePresentation()
        {
            treeView1.Invalidate();

            var userNode = FindUserNode(_currentUserName);
            if (userNode == null)
                return;

            var preferredStatus = GetSelectedQueueStatusName();
            FillQueueCombo(preferredStatus);
        }

        private void HandleOrdersGridChanged()
        {
            if (_isRebuildingGrid)
                return;

            ApplyStatusFilterToGrid();
            UpdateStatusFilterCaption();
            UpdateOrderNoSearchCaption();
            UpdateUserFilterCaption();
            UpdateCreatedDateFilterCaption();
            UpdateReceivedDateFilterCaption();
            RefreshStatusFilterChecklist();
            RefreshUserFilterChecklist();
            RefreshQueuePresentation();
            UpdateActionButtonsState();
            RefreshTrayIndicators();
        }

        private void LoadHistory()
        {
            _orderHistory.Clear();
            _jsonHistoryFile = StoragePaths.ResolveExistingFilePath(_jsonHistoryFile, "history.json");

            if (File.Exists(_jsonHistoryFile))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<OrderData>>(File.ReadAllText(_jsonHistoryFile));
                    if (parsed != null)
                        _orderHistory.AddRange(parsed);
                }
                catch
                {
                    _orderHistory.Clear();
                }
            }

            foreach (var order in _orderHistory)
            {
                if (string.IsNullOrWhiteSpace(order.InternalId))
                    order.InternalId = Guid.NewGuid().ToString("N");
                if (order.ArrivalDate == default)
                    order.ArrivalDate = order.OrderDate != default ? order.OrderDate : DateTime.Now;
            }

            if (NormalizeOrderTopologyInHistory(logIssues: true))
                SaveHistory();
        }

        private void SaveHistory()
        {
            NormalizeOrderTopologyInHistory(logIssues: false);

            var targetPath = StoragePaths.ResolveFilePath(_jsonHistoryFile, "history.json");
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(
                targetPath,
                JsonSerializer.Serialize(_orderHistory, new JsonSerializerOptions { WriteIndented = true }));
            _jsonHistoryFile = targetPath;
        }

        private bool NormalizeOrderTopologyInHistory(bool logIssues)
        {
            if (_orderHistory.Count == 0)
                return false;

            var changed = false;
            foreach (var order in _orderHistory)
            {
                var result = OrderTopologyService.Normalize(order);
                if (result.Changed)
                    changed = true;

                if (!logIssues || result.Issues.Count == 0)
                    continue;

                foreach (var issue in result.Issues)
                    Logger.Warn($"TOPOLOGY | order={GetOrderDisplayId(order)} | {issue}");
            }

            return changed;
        }

        private void RebuildOrdersGrid()
        {
            if (_isRebuildingGrid)
                return;

            if (NormalizeOrderTopologyInHistory(logIssues: false))
                SaveHistory();

            _isRebuildingGrid = true;
            dgvJobs.SuspendLayout();

            try
            {
                var selectedTag = dgvJobs.CurrentRow?.Tag?.ToString();
                dgvJobs.Rows.Clear();

                var sortedOrders = _orderHistory
                    .OrderByDescending(x => x.ArrivalDate)
                    .ToList();

                var searchText = (tbSearch.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    sortedOrders = sortedOrders
                        .Where(x => OrderMatchesSearch(x, searchText))
                        .ToList();
                }

                foreach (var order in sortedOrders)
                    AddOrderRowsToGrid(order);

                if (!string.IsNullOrWhiteSpace(selectedTag))
                    TryRestoreSelectedRowByTag(selectedTag);
            }
            finally
            {
                dgvJobs.ResumeLayout();
                _isRebuildingGrid = false;
            }

            HandleOrdersGridChanged();
        }

        private void AddOrderRowsToGrid(OrderData order)
        {
            if (order == null)
                return;

            var normalizedStatus = NormalizeStatus(order.Status) ?? (order.Status ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedStatus))
                normalizedStatus = "Обрабатывается";

            var sourcePath = ResolveSingleOrderDisplayPath(order, 1);
            var preparedPath = ResolveSingleOrderDisplayPath(order, 2);
            var printPath = ResolveSingleOrderDisplayPath(order, 3);
            var pitStopAction = ResolveSingleOrderDisplayAction(order, x => x.PitStopAction, order.PitStopAction);
            var imposingAction = ResolveSingleOrderDisplayAction(order, x => x.ImposingAction, order.ImposingAction);

            var orderRowIndex = dgvJobs.Rows.Add(
                normalizedStatus,
                GetOrderDisplayId(order),
                GetFileName(sourcePath),
                GetFileName(preparedPath),
                pitStopAction,
                imposingAction,
                GetFileName(printPath),
                FormatDate(order.OrderDate),
                FormatDate(order.ArrivalDate));

            dgvJobs.Rows[orderRowIndex].Tag = $"order|{order.InternalId}";
        }

        private static string ResolveSingleOrderDisplayPath(OrderData order, int stage)
        {
            var orderPath = GetOrderStagePath(order, stage);
            var primaryItem = GetPrimaryItem(order);
            var itemPath = primaryItem == null ? string.Empty : GetItemStagePath(primaryItem, stage);

            if (HasExistingFile(itemPath))
                return itemPath;

            if (HasExistingFile(orderPath))
                return orderPath;

            if (!string.IsNullOrWhiteSpace(orderPath))
                return orderPath;

            return itemPath;
        }

        private static string ResolveSingleOrderDisplayAction(OrderData order, Func<OrderFileItem, string> selector, string? orderAction)
        {
            var normalizedOrderAction = NormalizeAction(orderAction);
            if (!string.Equals(normalizedOrderAction, "-", StringComparison.Ordinal))
                return normalizedOrderAction;

            var primaryItem = GetPrimaryItem(order);
            if (primaryItem == null)
                return normalizedOrderAction;

            return NormalizeAction(selector(primaryItem));
        }

        private static OrderFileItem? GetPrimaryItem(OrderData order)
        {
            if (order?.Items == null || order.Items.Count == 0)
                return null;

            return order.Items
                .Where(x => x != null)
                .OrderBy(x => x.SequenceNo)
                .FirstOrDefault();
        }

        private bool OrderMatchesSearch(OrderData order, string searchText)
        {
            if (order == null)
                return false;

            var query = searchText.Trim();
            if (string.IsNullOrWhiteSpace(query))
                return true;

            static bool Contains(string source, string queryValue)
                => !string.IsNullOrWhiteSpace(source) &&
                   source.IndexOf(queryValue, StringComparison.OrdinalIgnoreCase) >= 0;

            if (Contains(order.Id, query)
                || Contains(Path.GetFileName(order.SourcePath), query)
                || Contains(Path.GetFileName(order.PreparedPath), query)
                || Contains(Path.GetFileName(order.PrintPath), query))
            {
                return true;
            }

            if (order.Items == null || order.Items.Count == 0)
                return false;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                if (Contains(item.ClientFileLabel, query)
                    || Contains(Path.GetFileName(item.SourcePath), query)
                    || Contains(Path.GetFileName(item.PreparedPath), query)
                    || Contains(Path.GetFileName(item.PrintPath), query))
                {
                    return true;
                }
            }

            return false;
        }

        private void TryRestoreSelectedRowByTag(string selectedTag)
        {
            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (string.Equals(row.Tag?.ToString(), selectedTag, StringComparison.Ordinal))
                {
                    dgvJobs.CurrentCell = row.Cells[colStatus.Index];
                    return;
                }
            }
        }

        private OrderData? FindOrderByInternalId(string? internalId)
        {
            if (string.IsNullOrWhiteSpace(internalId))
                return null;

            return _orderHistory.FirstOrDefault(x => string.Equals(x.InternalId, internalId, StringComparison.Ordinal));
        }

        private static bool IsOrderTag(string? tag)
        {
            return !string.IsNullOrWhiteSpace(tag) && tag.StartsWith("order|", StringComparison.Ordinal);
        }

        private static bool IsItemTag(string? tag)
        {
            return !string.IsNullOrWhiteSpace(tag) && tag.StartsWith("item|", StringComparison.Ordinal);
        }

        private static string? ExtractOrderInternalIdFromTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var parts = tag.Split('|');
            if (parts.Length < 2)
                return null;

            return parts[1];
        }

        private static string GetOrderDisplayId(OrderData order)
        {
            return string.IsNullOrWhiteSpace(order.Id) ? "—" : order.Id.Trim();
        }

        private static string GetFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "...";

            var normalizedPath = path.Trim();
            if (Directory.Exists(normalizedPath))
                return "...";

            var fileName = Path.GetFileName(normalizedPath);
            return string.IsNullOrWhiteSpace(fileName) ? "..." : fileName;
        }

        private static string FormatDate(DateTime value)
        {
            if (value == default)
                return string.Empty;

            return value.ToString("dd.MM.yyyy");
        }

        private static string NormalizeAction(string? action)
        {
            return string.IsNullOrWhiteSpace(action) ? "-" : action.Trim();
        }

        private OrderData? GetSelectedOrder()
        {
            if (dgvJobs.CurrentRow == null)
                return null;

            var rowTag = dgvJobs.CurrentRow.Tag?.ToString();
            var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return null;

            return FindOrderByInternalId(orderInternalId);
        }

        private async Task RunSelectedOrderAsync()
        {
            var order = GetSelectedOrder();
            if (order == null)
            {
                SetBottomStatus("Выберите строку заказа для запуска");
                MessageBox.Show(this, "Выберите строку заказа для запуска.", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(order.Id))
            {
                SetBottomStatus("У заказа не указан номер");
                MessageBox.Show(this, "У заказа не указан № заказа. Перед запуском заполните карточку заказа.", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_runTokensByOrder.ContainsKey(order.InternalId))
            {
                SetBottomStatus($"Заказ {GetOrderDisplayId(order)} уже запущен");
                MessageBox.Show(this, $"Заказ {GetOrderDisplayId(order)} уже запущен.", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_processor == null)
                InitializeProcessor();

            var cts = new CancellationTokenSource();
            _runTokensByOrder[order.InternalId] = cts;
            _runProgressByOrderInternalId[order.InternalId] = 0;
            UpdateTrayProgressIndicator();

            SetOrderStatus(order, "Обрабатывается", "ui", "Запуск из MainForm", persistHistory: true, rebuildGrid: true);
            SetBottomStatus($"Запущен заказ {GetOrderDisplayId(order)}");

            try
            {
                await _processor!.RunAsync(order, cts.Token, selectedItemIds: null);
            }
            catch (OperationCanceledException)
            {
                SetOrderStatus(order, "Отменено", "ui", "Остановлено пользователем", persistHistory: true, rebuildGrid: true);
                SetBottomStatus($"Заказ {GetOrderDisplayId(order)} остановлен");
            }
            catch (Exception ex)
            {
                SetOrderStatus(order, "Ошибка", "ui", ex.Message, persistHistory: true, rebuildGrid: true);
                SetBottomStatus($"Ошибка запуска заказа {GetOrderDisplayId(order)}: {ex.Message}");
                MessageBox.Show(this, $"Не удалось запустить заказ: {ex.Message}", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _runTokensByOrder.Remove(order.InternalId);
                _runProgressByOrderInternalId.Remove(order.InternalId);
                UpdateTrayProgressIndicator();
                SaveHistory();
                RebuildOrdersGrid();
                UpdateActionButtonsState();
            }
        }

        private void StopSelectedOrder()
        {
            var order = GetSelectedOrder();
            if (order == null)
            {
                SetBottomStatus("Выберите заказ для остановки");
                MessageBox.Show(this, "Выберите заказ для остановки.", "Остановка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_runTokensByOrder.TryGetValue(order.InternalId, out var cts))
            {
                SetBottomStatus($"Заказ {GetOrderDisplayId(order)} сейчас не выполняется");
                MessageBox.Show(this, $"Заказ {GetOrderDisplayId(order)} сейчас не выполняется.", "Остановка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            cts.Cancel();
            _runTokensByOrder.Remove(order.InternalId);
            _runProgressByOrderInternalId.Remove(order.InternalId);
            UpdateTrayProgressIndicator();
            SetOrderStatus(order, "Отменено", "ui", "Остановлено пользователем", persistHistory: true, rebuildGrid: true);
            UpdateActionButtonsState();
            SetBottomStatus($"Остановлен заказ {GetOrderDisplayId(order)}");
        }

        private void RemoveSelectedOrder()
        {
            var order = GetSelectedOrder();
            if (order == null)
            {
                SetBottomStatus("Выберите заказ для удаления");
                MessageBox.Show(this, "Выберите заказ для удаления.", "Удаление", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var orderFolder = string.IsNullOrWhiteSpace(order.FolderName)
                ? string.Empty
                : Path.Combine(_ordersRootPath, order.FolderName);

            var decision = MessageBox.Show(
                this,
                $"Заказ №{GetOrderDisplayId(order)}\n\n" +
                "Удалить папку заказа с диска?\n\n" +
                "[Да] — удалить с диска и из списка.\n" +
                "[Нет] — только удалить из списка.\n" +
                "[Отмена] — ничего не менять.",
                "Удаление заказа",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (decision == DialogResult.Cancel)
            {
                SetBottomStatus("Удаление отменено");
                return;
            }

            if (decision == DialogResult.Yes)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(orderFolder) && Directory.Exists(orderFolder))
                        Directory.Delete(orderFolder, true);
                    else
                        DeleteOrderFiles(order);
                }
                catch (Exception ex)
                {
                    SetBottomStatus($"Не удалось удалить файлы заказа: {ex.Message}");
                    MessageBox.Show(this, $"Не удалось удалить файлы заказа: {ex.Message}", "Удаление заказа", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (_runTokensByOrder.TryGetValue(order.InternalId, out var cts))
            {
                cts.Cancel();
                _runTokensByOrder.Remove(order.InternalId);
                _runProgressByOrderInternalId.Remove(order.InternalId);
                UpdateTrayProgressIndicator();
            }

            _expandedOrderIds.Remove(order.InternalId);
            _orderHistory.Remove(order);
            SaveHistory();
            RebuildOrdersGrid();
            UpdateActionButtonsState();
            SetBottomStatus($"Заказ {GetOrderDisplayId(order)} удален");
        }

        private void DeleteOrderFiles(OrderData order)
        {
            foreach (var path in GetOrderAllKnownPaths(order))
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Не удалось удалить файл: {path}", ex);
                }
            }
        }

        private static IEnumerable<string?> GetOrderAllKnownPaths(OrderData order)
        {
            if (order == null)
                yield break;

            yield return order.SourcePath;
            yield return order.PreparedPath;
            yield return order.PrintPath;

            if (order.Items == null)
                yield break;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                yield return item.SourcePath;
                yield return item.PreparedPath;
                yield return item.PrintPath;
            }
        }

        private void OpenLogForSelectionOrManager()
        {
            AcknowledgeErrorNotifications();

            var order = GetSelectedOrder();
            if (order != null)
            {
                var orderLogPath = GetOrderLogFilePath(order);
                if (File.Exists(orderLogPath))
                {
                    using var viewer = new OrderLogViewerForm(orderLogPath, GetOrderDisplayId(order));
                    viewer.ShowDialog(this);
                    return;
                }
            }

            if (!File.Exists(_managerLogFilePath))
            {
                SetBottomStatus("Лог пока не создан");
                MessageBox.Show(this, "Лог пока не создан.", "Лог", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _managerLogFilePath,
                UseShellExecute = true
            });
            SetBottomStatus("Открыт лог менеджера");
        }

        private void OpenFolderForSelectedOrder()
        {
            var order = GetSelectedOrder();
            string targetPath;

            if (order == null)
            {
                targetPath = !string.IsNullOrWhiteSpace(_ordersRootPath)
                    ? _ordersRootPath
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            else
            {
                targetPath = GetPreferredOrderFolder(order);
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                SetBottomStatus("Папка не определена");
                MessageBox.Show(this, "Папка не определена.", "Папка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Directory.CreateDirectory(targetPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });
            SetBottomStatus($"Открыта папка: {targetPath}");
        }

        private void OpenOrderStageFolder(OrderData order, int stage)
        {
            if (order == null)
                return;

            string targetPath;
            if (stage is >= 1 and <= 3)
                targetPath = GetStageFolder(order, stage);
            else
                targetPath = GetPreferredOrderFolder(order);

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                SetBottomStatus("Папка не определена");
                MessageBox.Show(this, "Папка не определена.", "Папка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Directory.CreateDirectory(targetPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });
            SetBottomStatus($"Открыта папка этапа: {targetPath}");
        }

        private string GetPreferredOrderFolder(OrderData order)
        {
            if (!string.IsNullOrWhiteSpace(order.FolderName))
                return Path.Combine(_ordersRootPath, order.FolderName);

            var knownPath = FirstNotEmpty(
                order.PrintPath,
                order.PreparedPath,
                order.SourcePath,
                order.Items?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.PrintPath))?.PrintPath,
                order.Items?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.PreparedPath))?.PreparedPath,
                order.Items?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.SourcePath))?.SourcePath);

            if (!string.IsNullOrWhiteSpace(knownPath))
                return Path.GetDirectoryName(knownPath) ?? _ordersRootPath;

            return !string.IsNullOrWhiteSpace(_tempRootPath) ? _tempRootPath : _ordersRootPath;
        }

        private static string? FirstNotEmpty(params string?[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return null;
        }

        private bool SetOrderStatus(
            OrderData order,
            string status,
            string source,
            string reason,
            bool persistHistory,
            bool rebuildGrid)
        {
            var oldStatus = order.Status ?? string.Empty;
            if (string.Equals(oldStatus, status, StringComparison.Ordinal)
                && string.Equals(order.LastStatusSource ?? string.Empty, source ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(order.LastStatusReason ?? string.Empty, reason ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            order.Status = status;
            order.LastStatusSource = source ?? string.Empty;
            order.LastStatusReason = reason ?? string.Empty;
            order.LastStatusAt = DateTime.Now;
            AppendOrderStatusLog(order, oldStatus, status, source ?? string.Empty, reason ?? string.Empty);

            if (persistHistory)
                SaveHistory();
            if (rebuildGrid)
                RebuildOrdersGrid();

            UpdateTrayErrorIndicator();

            return true;
        }

        private void RefreshTrayIndicators()
        {
            UpdateTrayStatsIndicator();
            UpdateTrayConnectionIndicator();
            UpdateTrayDiskIndicator();
            UpdateTrayErrorIndicator();
            UpdateTrayProgressIndicator();
        }

        private void UpdateTrayStatsIndicator()
        {
            if (toolStats.IsDisposed || dgvJobs.IsDisposed)
                return;

            var visibleOrders = 0;
            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow || !row.Visible)
                    continue;

                visibleOrders++;
            }

            var selectedOrderIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataGridViewRow row in dgvJobs.SelectedRows)
            {
                if (row.IsNewRow)
                    continue;

                var orderInternalId = ExtractOrderInternalIdFromTag(row.Tag?.ToString());
                if (!string.IsNullOrWhiteSpace(orderInternalId))
                    selectedOrderIds.Add(orderInternalId);
            }

            toolStats.Text = $"Строк: {visibleOrders} | Выделено: {selectedOrderIds.Count}";
        }

        private void UpdateTrayConnectionIndicator()
        {
            if (toolConnection.IsDisposed)
                return;

            var isConnected = CanAccessPath(_ordersRootPath);
            toolConnection.Text = isConnected ? "● Сервер: подключен" : "● Сервер: автономно";
            toolConnection.ForeColor = isConnected ? Color.SeaGreen : Color.Firebrick;
            toolConnection.ToolTipText = isConnected
                ? "Рабочая папка заказов доступна."
                : "Рабочая папка заказов недоступна.";
        }

        private void UpdateTrayDiskIndicator()
        {
            if (toolDiskFree.IsDisposed)
                return;

            if (!TryGetProbeDrive(out var drive))
            {
                toolDiskFree.Text = "Свободно: н/д";
                toolDiskFree.ForeColor = Color.Gray;
                toolDiskFree.ToolTipText = "Не удалось определить диск.";
                return;
            }

            long freeBytes;
            try
            {
                freeBytes = drive.AvailableFreeSpace;
            }
            catch
            {
                toolDiskFree.Text = $"Свободно {drive.Name} н/д";
                toolDiskFree.ForeColor = Color.Gray;
                toolDiskFree.ToolTipText = "Нет доступа к данным о диске.";
                return;
            }

            toolDiskFree.Text = $"Свободно {drive.Name} {FormatStorageSize(freeBytes)}";
            toolDiskFree.ToolTipText = $"Свободное место на диске {drive.Name}";

            if (freeBytes <= DiskCriticalThresholdBytes)
                toolDiskFree.ForeColor = Color.Firebrick;
            else if (freeBytes <= DiskWarningThresholdBytes)
                toolDiskFree.ForeColor = Color.DarkOrange;
            else
                toolDiskFree.ForeColor = Color.SeaGreen;
        }

        private void UpdateTrayErrorIndicator()
        {
            if (toolAlerts.IsDisposed)
                return;

            var errorCount = CountOrdersWithErrors();
            if (errorCount <= 0)
                _acknowledgedErrorCount = 0;
            else if (_acknowledgedErrorCount > errorCount)
                _acknowledgedErrorCount = errorCount;

            var hasUnseenErrors = errorCount > _acknowledgedErrorCount;
            toolAlerts.Text = $"⚠ {errorCount}";
            toolAlerts.ToolTipText = errorCount == 0
                ? "Ошибок нет. Нажмите, чтобы открыть лог."
                : hasUnseenErrors
                    ? $"Есть новые ошибки: {errorCount}. Нажмите, чтобы открыть лог."
                    : $"Ошибок: {errorCount}. Нажмите, чтобы открыть лог.";

            if (errorCount == 0)
                toolAlerts.ForeColor = Color.Gray;
            else if (hasUnseenErrors)
                toolAlerts.ForeColor = Color.Firebrick;
            else
                toolAlerts.ForeColor = Color.Goldenrod;
        }

        private void ApplyProcessorProgress(string orderId, int progressValue)
        {
            var boundedValue = Math.Clamp(progressValue, 0, 100);
            var matchedAny = false;

            foreach (var internalId in _runTokensByOrder.Keys.ToList())
            {
                var runningOrder = FindOrderByInternalId(internalId);
                if (runningOrder == null)
                    continue;

                if (!string.Equals(runningOrder.Id ?? string.Empty, orderId ?? string.Empty, StringComparison.Ordinal))
                    continue;

                _runProgressByOrderInternalId[internalId] = boundedValue;
                matchedAny = true;
            }

            if (!matchedAny && _runTokensByOrder.Count == 1)
            {
                var onlyInternalId = _runTokensByOrder.Keys.First();
                _runProgressByOrderInternalId[onlyInternalId] = boundedValue;
            }

            UpdateTrayProgressIndicator();
        }

        private void UpdateTrayProgressIndicator()
        {
            if (toolProgress.IsDisposed)
                return;

            if (_runProgressByOrderInternalId.Count == 0)
            {
                toolProgress.Visible = false;
                toolProgress.Value = 0;
                toolProgress.ToolTipText = "Нет активной обработки.";
                return;
            }

            var averageProgress = (int)Math.Round(_runProgressByOrderInternalId.Values.Average());
            var boundedProgress = Math.Clamp(averageProgress, 0, 100);
            toolProgress.Value = boundedProgress;
            toolProgress.Visible = true;
            toolProgress.ToolTipText = _runProgressByOrderInternalId.Count == 1
                ? $"Прогресс обработки: {boundedProgress}%."
                : $"Средний прогресс по {_runProgressByOrderInternalId.Count} заказам: {boundedProgress}%.";
        }

        private void AcknowledgeErrorNotifications()
        {
            _acknowledgedErrorCount = CountOrdersWithErrors();
            UpdateTrayErrorIndicator();
        }

        private int CountOrdersWithErrors()
        {
            var count = 0;
            foreach (var order in _orderHistory)
            {
                if (order == null)
                    continue;

                var normalizedStatus = NormalizeStatus(order.Status);
                if (string.Equals(normalizedStatus, "Ошибка", StringComparison.Ordinal))
                    count++;
            }

            return count;
        }

        private bool TryGetProbeDrive(out DriveInfo drive)
        {
            drive = null!;

            var probePath = FirstNotEmpty(_ordersRootPath, _tempRootPath, _grandpaFolder);
            if (string.IsNullOrWhiteSpace(probePath))
                return false;

            string root;
            try
            {
                root = Path.GetPathRoot(probePath) ?? string.Empty;
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(root))
                return false;

            try
            {
                drive = new DriveInfo(root);
                return drive.IsReady;
            }
            catch
            {
                return false;
            }
        }

        private static bool CanAccessPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                    return true;

                var root = Path.GetPathRoot(fullPath);
                return !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
            }
            catch
            {
                return false;
            }
        }

        private static string FormatStorageSize(long bytes)
        {
            if (bytes < 0)
                bytes = 0;

            string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? $"{value:0} {units[unit]}"
                : $"{value:0.0} {units[unit]}";
        }

        private void SetBottomStatus(string text)
        {
            if (Disposing || IsDisposed)
                return;

            var nextText = string.IsNullOrWhiteSpace(text)
                ? DefaultTrayStatusText
                : text.Trim();

            void Apply()
            {
                if (toolStatus.IsDisposed)
                    return;

                toolStatus.Text = nextText;
            }

            if (InvokeRequired)
                BeginInvoke((Action)Apply);
            else
                Apply();
        }

        private string GetOrderLogFilePath(OrderData order)
        {
            var safeId = string.IsNullOrWhiteSpace(order.InternalId) ? order.Id : order.InternalId;
            if (string.IsNullOrWhiteSpace(safeId))
                safeId = "unknown-order";

            foreach (var c in Path.GetInvalidFileNameChars())
                safeId = safeId.Replace(c, '_');

            var logFolder = StoragePaths.ResolveFolderPath(_orderLogsFolderPath, "order-logs");
            Directory.CreateDirectory(logFolder);
            return Path.Combine(logFolder, $"{safeId}.log");
        }

        private void AppendOrderStatusLog(OrderData order, string oldStatus, string newStatus, string source, string reason)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | status: {oldStatus} -> {newStatus} | source: {source} | reason: {reason}";
                File.AppendAllText(GetOrderLogFilePath(order), line + Environment.NewLine);
                Logger.Info($"ORDER-STATUS | order={GetOrderDisplayId(order)} | {line}");
            }
            catch
            {
                // Лог не должен ломать основной поток.
            }
        }

        private void LblFStatus_Click(object? sender, EventArgs e)
        {
            if (_suppressNextStatusFilterLabelClick)
            {
                _suppressNextStatusFilterLabelClick = false;
                return;
            }

            if (_statusFilterDropDown?.Visible == true)
            {
                _statusFilterDropDown.Close(ToolStripDropDownCloseReason.AppClicked);
                return;
            }

            ShowStatusFilterDropDown();
        }

        private void LblFOrderNo_Click(object? sender, EventArgs e)
        {
            if (_suppressNextOrderNoLabelClick)
            {
                _suppressNextOrderNoLabelClick = false;
                return;
            }

            if (_orderNoFilterDropDown?.Visible == true)
            {
                _orderNoFilterDropDown.Close(ToolStripDropDownCloseReason.AppClicked);
                return;
            }

            ShowOrderNoFilterDropDown();
        }

        private void LblFUser_Click(object? sender, EventArgs e)
        {
            if (_suppressNextUserFilterLabelClick)
            {
                _suppressNextUserFilterLabelClick = false;
                return;
            }

            if (_userFilterDropDown?.Visible == true)
            {
                _userFilterDropDown.Close(ToolStripDropDownCloseReason.AppClicked);
                return;
            }

            ShowUserFilterDropDown();
        }

        private void LblFCreated_Click(object? sender, EventArgs e)
        {
            if (_suppressNextCreatedFilterLabelClick)
            {
                _suppressNextCreatedFilterLabelClick = false;
                return;
            }

            if (_createdFilterDropDown?.Visible == true)
            {
                _createdFilterDropDown.Close(ToolStripDropDownCloseReason.AppClicked);
                return;
            }

            ShowCreatedDateFilterDropDown();
        }

        private void LblFReceived_Click(object? sender, EventArgs e)
        {
            if (_suppressNextReceivedFilterLabelClick)
            {
                _suppressNextReceivedFilterLabelClick = false;
                return;
            }

            if (_receivedFilterDropDown?.Visible == true)
            {
                _receivedFilterDropDown.Close(ToolStripDropDownCloseReason.AppClicked);
                return;
            }

            ShowReceivedDateFilterDropDown();
        }

        private void ShowOrderNoFilterDropDown()
        {
            EnsureOrderNoFilterDropDown();
            SyncOrderNoFilterPopupState();

            if (_orderNoFilterDropDown == null)
                return;

            _orderNoFilterDropDown.Show(picFOrderNoGlyph, new Point(0, picFOrderNoGlyph.Height));
            _orderNoFilterTextBox?.Focus();
            _orderNoFilterTextBox?.SelectAll();
        }

        private void EnsureOrderNoFilterDropDown()
        {
            if (_orderNoFilterDropDown != null &&
                _orderNoFilterTextBox != null)
                return;

            var popupWidth = Math.Max(lblFOrderNo.Width + 100, 280);
            var popupHeight = 96;

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(popupWidth, popupHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _orderNoFilterTextBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Location = new Point(16, 16),
                Size = new Size(popupWidth - 32, 24),
                Font = lblFOrderNo.Font
            };
            _orderNoFilterTextBox.TextChanged += (_, _) => UpdateOrderNoFilterActionButtonsState();
            _orderNoFilterTextBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    if (!HasOrderNoFilterInputText())
                        return;

                    ApplyOrderNoFilterFromPopup(clearFilter: false);
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    _orderNoFilterDropDown?.Close(ToolStripDropDownCloseReason.AppClicked);
                }
            };

            var underline = new Panel
            {
                BackColor = Color.FromArgb(33, 127, 203),
                Location = new Point(16, 42),
                Size = new Size(popupWidth - 32, 2)
            };

            _orderNoFilterClearButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(16, 56),
                Size = new Size(104, 32),
                Text = "Очистить",
                BackColor = Color.White,
                ForeColor = Color.FromArgb(168, 197, 225)
            };
            _orderNoFilterClearButton.FlatAppearance.BorderSize = 0;
            _orderNoFilterClearButton.Click += (_, _) => ApplyOrderNoFilterFromPopup(clearFilter: true);

            _orderNoFilterApplyButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(126, 56),
                Size = new Size(120, 32),
                Text = "Применить",
                BackColor = Color.FromArgb(176, 212, 242),
                ForeColor = Color.White
            };
            _orderNoFilterApplyButton.FlatAppearance.BorderSize = 0;
            _orderNoFilterApplyButton.Click += (_, _) => ApplyOrderNoFilterFromPopup(clearFilter: false);

            panel.Controls.Add(_orderNoFilterTextBox);
            panel.Controls.Add(underline);
            panel.Controls.Add(_orderNoFilterClearButton);
            panel.Controls.Add(_orderNoFilterApplyButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _orderNoFilterDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(4)
            };
            _orderNoFilterDropDown.Closing += OrderNoFilterDropDown_Closing;
            _orderNoFilterDropDown.Items.Add(host);
            UpdateOrderNoFilterActionButtonsState();
        }

        private void SyncOrderNoFilterPopupState()
        {
            if (_orderNoFilterTextBox == null)
                return;

            _orderNoFilterTextBox.Text = _orderNumberFilterText;
            UpdateOrderNoFilterActionButtonsState();
        }

        private bool HasOrderNoFilterInputText()
        {
            return !string.IsNullOrWhiteSpace(_orderNoFilterTextBox?.Text);
        }

        private void UpdateOrderNoFilterActionButtonsState()
        {
            if (_orderNoFilterClearButton == null || _orderNoFilterApplyButton == null)
                return;

            var hasInput = HasOrderNoFilterInputText();
            _orderNoFilterClearButton.Enabled = hasInput;
            _orderNoFilterApplyButton.Enabled = hasInput;
            _orderNoFilterClearButton.ForeColor = hasInput
                ? Color.FromArgb(77, 147, 222)
                : Color.FromArgb(168, 197, 225);
            _orderNoFilterApplyButton.BackColor = hasInput
                ? Color.FromArgb(33, 127, 203)
                : Color.FromArgb(176, 212, 242);
            _orderNoFilterApplyButton.ForeColor = Color.White;
        }

        private void ApplyOrderNoFilterFromPopup(bool clearFilter)
        {
            if (_orderNoFilterTextBox == null)
                return;

            if (!clearFilter && !HasOrderNoFilterInputText())
                return;

            var nextText = clearFilter ? string.Empty : (_orderNoFilterTextBox.Text ?? string.Empty).Trim();
            var changed = !string.Equals(_orderNumberFilterText, nextText, StringComparison.Ordinal);

            _orderNumberFilterText = nextText;

            if (changed)
                HandleOrdersGridChanged();

            _orderNoFilterDropDown?.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void OrderNoFilterDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason != ToolStripDropDownCloseReason.AppClicked)
                return;

            var labelRect = lblFOrderNo.RectangleToScreen(lblFOrderNo.ClientRectangle);
            var glyphRect = picFOrderNoGlyph.RectangleToScreen(picFOrderNoGlyph.ClientRectangle);
            if (labelRect.Contains(Cursor.Position) || glyphRect.Contains(Cursor.Position))
                _suppressNextOrderNoLabelClick = true;
        }

        private void ShowUserFilterDropDown()
        {
            EnsureUserFilterDropDown();
            RefreshUserFilterChecklist();

            if (_userFilterDropDown == null || _userFilterGlyph == null)
                return;

            _userFilterDropDown.Show(_userFilterGlyph, new Point(0, _userFilterGlyph.Height));
        }

        private void EnsureUserFilterDropDown()
        {
            if (_userFilterDropDown != null &&
                _userFilterCheckedList != null &&
                _userFilterClearButton != null &&
                _userFilterApplyButton != null)
                return;

            var labelWidth = _userFilterLabel?.Width ?? 150;
            var popupWidth = Math.Max(labelWidth + 110, 280);
            var listHeight = Math.Max(FilterUsers.Length * 33, 96);
            var buttonTop = 16 + listHeight + 10;
            var popupHeight = buttonTop + 32 + 10;

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(popupWidth, popupHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _userFilterCheckedList = new CheckedListBox
            {
                CheckOnClick = true,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                Font = _userFilterLabel?.Font ?? Font,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(47, 53, 72),
                Location = new Point(16, 16),
                Size = new Size(popupWidth - 32, listHeight)
            };
            _userFilterCheckedList.ItemCheck += UserFilterCheckedList_ItemCheck;

            _userFilterClearButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(16, buttonTop),
                Size = new Size(104, 32),
                Text = "Очистить",
                BackColor = Color.White,
                ForeColor = Color.FromArgb(168, 197, 225)
            };
            _userFilterClearButton.FlatAppearance.BorderSize = 0;
            _userFilterClearButton.Click += (_, _) => ApplyUserFilterFromPopup(clearFilter: true);

            _userFilterApplyButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(126, buttonTop),
                Size = new Size(120, 32),
                Text = "Применить",
                BackColor = Color.FromArgb(176, 212, 242),
                ForeColor = Color.White
            };
            _userFilterApplyButton.FlatAppearance.BorderSize = 0;
            _userFilterApplyButton.Click += (_, _) => ApplyUserFilterFromPopup(clearFilter: false);

            panel.Controls.Add(_userFilterCheckedList);
            panel.Controls.Add(_userFilterClearButton);
            panel.Controls.Add(_userFilterApplyButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _userFilterDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(4)
            };
            _userFilterDropDown.Closing += UserFilterDropDown_Closing;
            _userFilterDropDown.Items.Add(host);
            UpdateUserFilterActionButtonsState();
        }

        private void UserFilterDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason != ToolStripDropDownCloseReason.AppClicked)
                return;

            if (_userFilterLabel == null || _userFilterGlyph == null)
                return;

            var labelRect = _userFilterLabel.RectangleToScreen(_userFilterLabel.ClientRectangle);
            var glyphRect = _userFilterGlyph.RectangleToScreen(_userFilterGlyph.ClientRectangle);
            if (labelRect.Contains(Cursor.Position) || glyphRect.Contains(Cursor.Position))
                _suppressNextUserFilterLabelClick = true;
        }

        private void RefreshUserFilterChecklist()
        {
            if (_userFilterCheckedList == null)
                return;

            var countsByFilterUser = GetCountsByFilterUsers();

            _isUpdatingUserFilterList = true;
            _userFilterCheckedList.BeginUpdate();
            _userFilterCheckedList.Items.Clear();

            foreach (var userName in FilterUsers)
            {
                countsByFilterUser.TryGetValue(userName, out var count);
                var item = new UserFilterOption(userName, count);
                _userFilterCheckedList.Items.Add(item, _selectedFilterUsers.Contains(userName));
            }

            _userFilterCheckedList.EndUpdate();
            _isUpdatingUserFilterList = false;
            UpdateUserFilterActionButtonsState();
        }

        private void UserFilterCheckedList_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_isUpdatingUserFilterList)
                return;

            BeginInvoke(new Action(UpdateUserFilterActionButtonsState));
        }

        private void UpdateUserFilterActionButtonsState()
        {
            if (_userFilterClearButton == null || _userFilterApplyButton == null || _userFilterCheckedList == null)
                return;

            var hasSelection = _userFilterCheckedList.CheckedItems.Count > 0;
            _userFilterClearButton.Enabled = hasSelection;
            _userFilterApplyButton.Enabled = hasSelection;
            _userFilterClearButton.ForeColor = hasSelection
                ? Color.FromArgb(77, 147, 222)
                : Color.FromArgb(168, 197, 225);
            _userFilterApplyButton.BackColor = hasSelection
                ? Color.FromArgb(33, 127, 203)
                : Color.FromArgb(176, 212, 242);
            _userFilterApplyButton.ForeColor = Color.White;
        }

        private void ApplyUserFilterFromPopup(bool clearFilter)
        {
            if (_userFilterCheckedList == null)
                return;

            var nextSelectedUsers = new HashSet<string>(StringComparer.Ordinal);
            if (!clearFilter)
            {
                foreach (var item in _userFilterCheckedList.CheckedItems)
                {
                    if (item is UserFilterOption userItem)
                        nextSelectedUsers.Add(userItem.UserName);
                }

                if (nextSelectedUsers.Count == 0)
                    return;
            }

            var changed = !nextSelectedUsers.SetEquals(_selectedFilterUsers);
            _selectedFilterUsers.Clear();
            foreach (var userName in nextSelectedUsers)
                _selectedFilterUsers.Add(userName);

            if (changed)
                HandleOrdersGridChanged();

            _userFilterDropDown?.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void ShowCreatedDateFilterDropDown()
        {
            EnsureCreatedDateFilterDropDown();
            SyncCreatedDateFilterPopupState();

            if (_createdFilterDropDown == null || _createdFilterGlyph == null)
                return;

            _createdFilterDropDown.Show(_createdFilterGlyph, new Point(0, _createdFilterGlyph.Height));
        }

        private void EnsureCreatedDateFilterDropDown()
        {
            if (_createdFilterDropDown != null &&
                _createdFilterTodayRadio != null &&
                _createdFilterSingleRadio != null &&
                _createdFilterSingleModeCombo != null &&
                _createdFilterSingleDatePicker != null &&
                _createdFilterRangeRadio != null &&
                _createdFilterRangeFromDatePicker != null &&
                _createdFilterRangeToDatePicker != null &&
                _createdFilterClearButton != null &&
                _createdFilterApplyButton != null)
                return;

            var popupWidth = Math.Max((_createdFilterLabel?.Width ?? 170) + 260, 440);
            var popupHeight = 206;
            var font = _createdFilterLabel?.Font ?? Font;
            var row1Y = 16;
            var row2Y = 60;
            var row3Y = 104;
            var bulletX = 16;
            var contentX = 48;
            var singleModeWidth = 172;
            var singleDateX = contentX + singleModeWidth + 10;
            var singleDateWidth = popupWidth - singleDateX - 16;
            var rangeFromX = 88;
            var rangeDateWidth = 146;
            var toLabelX = rangeFromX + rangeDateWidth + 10;
            var toLabelWidth = 38;
            var rangeToX = toLabelX + toLabelWidth;
            var rangeToWidth = popupWidth - rangeToX - 16;
            var buttonsY = 158;

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(popupWidth, popupHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _createdFilterTodayRadio = new RadioButton
            {
                Location = new Point(bulletX, row1Y),
                Size = new Size(130, 30),
                Text = "Сегодня",
                Font = font,
                AutoCheck = true
            };
            _createdFilterTodayRadio.CheckedChanged += CreatedFilterModeControlChanged;

            _createdFilterSingleRadio = new RadioButton
            {
                Location = new Point(bulletX, row2Y),
                Size = new Size(24, 30),
                Font = font,
                AutoCheck = true
            };
            _createdFilterSingleRadio.CheckedChanged += CreatedFilterModeControlChanged;

            _createdFilterSingleModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = true,
                IntegralHeight = false,
                Location = new Point(contentX, row2Y - 1),
                Size = new Size(singleModeWidth, 32),
                Font = font
            };
            _createdFilterSingleModeCombo.Items.AddRange(new object[] { "Точная дата", "До", "После" });
            _createdFilterSingleModeCombo.SelectedIndexChanged += CreatedFilterModeControlChanged;

            _createdFilterSingleDatePicker = CreateCreatedFilterDatePicker(new Point(singleDateX, row2Y - 1), new Size(singleDateWidth, 32), font);
            _createdFilterSingleDatePicker.ValueChanged += CreatedFilterModeControlChanged;
            AttachCreatedDatePickerCalendarEvents(_createdFilterSingleDatePicker);

            _createdFilterRangeRadio = new RadioButton
            {
                Location = new Point(bulletX, row3Y),
                Size = new Size(58, 30),
                Text = "От",
                Font = font,
                AutoCheck = true
            };
            _createdFilterRangeRadio.CheckedChanged += CreatedFilterModeControlChanged;

            _createdFilterRangeFromDatePicker = CreateCreatedFilterDatePicker(new Point(rangeFromX, row3Y - 1), new Size(rangeDateWidth, 32), font);
            _createdFilterRangeFromDatePicker.ValueChanged += CreatedFilterModeControlChanged;
            AttachCreatedDatePickerCalendarEvents(_createdFilterRangeFromDatePicker);

            var toLabel = new Label
            {
                Location = new Point(toLabelX, row3Y),
                Size = new Size(toLabelWidth, 28),
                Text = "До",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = font
            };

            _createdFilterRangeToDatePicker = CreateCreatedFilterDatePicker(new Point(rangeToX, row3Y - 1), new Size(rangeToWidth, 32), font);
            _createdFilterRangeToDatePicker.ValueChanged += CreatedFilterModeControlChanged;
            AttachCreatedDatePickerCalendarEvents(_createdFilterRangeToDatePicker);

            _createdFilterClearButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(popupWidth - 232, buttonsY),
                Size = new Size(104, 32),
                Text = "Очистить",
                BackColor = Color.White,
                ForeColor = Color.FromArgb(168, 197, 225)
            };
            _createdFilterClearButton.FlatAppearance.BorderSize = 0;
            _createdFilterClearButton.Click += (_, _) => ApplyCreatedDateFilterFromPopup(clearFilter: true);

            _createdFilterApplyButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(popupWidth - 122, buttonsY),
                Size = new Size(120, 32),
                Text = "Применить",
                BackColor = Color.FromArgb(176, 212, 242),
                ForeColor = Color.White
            };
            _createdFilterApplyButton.FlatAppearance.BorderSize = 0;
            _createdFilterApplyButton.Click += (_, _) => ApplyCreatedDateFilterFromPopup(clearFilter: false);

            panel.Controls.Add(_createdFilterTodayRadio);
            panel.Controls.Add(_createdFilterSingleRadio);
            panel.Controls.Add(_createdFilterSingleModeCombo);
            panel.Controls.Add(_createdFilterSingleDatePicker);
            panel.Controls.Add(_createdFilterRangeRadio);
            panel.Controls.Add(_createdFilterRangeFromDatePicker);
            panel.Controls.Add(toLabel);
            panel.Controls.Add(_createdFilterRangeToDatePicker);
            panel.Controls.Add(_createdFilterClearButton);
            panel.Controls.Add(_createdFilterApplyButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _createdFilterDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(4)
            };
            _createdFilterDropDown.Closing += CreatedDateFilterDropDown_Closing;
            _createdFilterDropDown.Items.Add(host);
            UpdateCreatedDateFilterControlsState();
        }

        private static DateTimePicker CreateCreatedFilterDatePicker(Point location, Size size, Font font)
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd.MM.yyyy",
                Location = location,
                Size = size,
                Font = font
            };
        }

        private void AttachCreatedDatePickerCalendarEvents(DateTimePicker picker)
        {
            picker.DropDown += CreatedDatePicker_DropDown;
        }

        private void CreatedDatePicker_DropDown(object? sender, EventArgs e)
        {
            if (sender is not DateTimePicker picker)
                return;

            BeginInvoke(new Action(() =>
            {
                try
                {
                    SendKeys.Send("{ESC}");
                }
                catch
                {
                    // Игнорируем сбои нативного сворачивания, ниже откроем свой календарь.
                }

                ShowCreatedCalendarDropDown(picker);
            }));
        }

        private void EnsureCreatedCalendarDropDown()
        {
            if (_createdCalendarDropDown != null && _createdCalendar != null && _createdCalendarOkButton != null)
                return;

            _createdCalendar = new MonthCalendar
            {
                MaxSelectionCount = 1,
                ShowToday = false,
                ShowTodayCircle = false
            };

            _createdCalendarOkButton = new Button
            {
                Text = "OK",
                FlatStyle = FlatStyle.Standard
            };
            _createdCalendarOkButton.Click += CreatedCalendarOkButton_Click;

            var calendarWidth = _createdCalendar.Width;
            var buttonHeight = 32;
            var buttonTop = _createdCalendar.Height + 6;
            _createdCalendarOkButton.Location = new Point(0, buttonTop);
            _createdCalendarOkButton.Size = new Size(calendarWidth, buttonHeight);

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(calendarWidth, buttonTop + buttonHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            panel.Controls.Add(_createdCalendar);
            panel.Controls.Add(_createdCalendarOkButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _createdCalendarDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(2)
            };
            _createdCalendarDropDown.Closed += CreatedCalendarDropDown_Closed;
            _createdCalendarDropDown.Items.Add(host);
        }

        private void ShowCreatedCalendarDropDown(DateTimePicker picker)
        {
            EnsureCreatedCalendarDropDown();
            if (_createdCalendarDropDown == null || _createdCalendar == null)
                return;

            _createdCalendarTargetPicker = picker;
            var selectedDate = picker.Value.Date;
            _createdCalendar.SetDate(selectedDate);
            _createdCalendar.SelectionStart = selectedDate;
            _createdCalendar.SelectionEnd = selectedDate;

            if (_createdCalendarDropDown.Visible)
                _createdCalendarDropDown.Close(ToolStripDropDownCloseReason.CloseCalled);

            _isCreatedDateCalendarOpen = true;
            _createdCalendarDropDown.Show(picker, new Point(0, picker.Height));
        }

        private void CreatedCalendarOkButton_Click(object? sender, EventArgs e)
        {
            if (_createdCalendarTargetPicker != null && _createdCalendar != null)
            {
                var selectedDate = _createdCalendar.SelectionStart.Date;
                if (_createdCalendarTargetPicker.Value.Date != selectedDate)
                    _createdCalendarTargetPicker.Value = selectedDate;
            }

            CloseCreatedCalendarDropDown();
        }

        private void CloseCreatedCalendarDropDown()
        {
            if (_createdCalendarDropDown?.Visible == true)
                _createdCalendarDropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
            else
            {
                _isCreatedDateCalendarOpen = false;
                _createdCalendarTargetPicker = null;
            }
        }

        private void CreatedCalendarDropDown_Closed(object? sender, ToolStripDropDownClosedEventArgs e)
        {
            _isCreatedDateCalendarOpen = false;
            _createdCalendarTargetPicker = null;
        }

        private void SyncCreatedDateFilterPopupState()
        {
            if (_createdFilterTodayRadio == null ||
                _createdFilterSingleRadio == null ||
                _createdFilterSingleModeCombo == null ||
                _createdFilterSingleDatePicker == null ||
                _createdFilterRangeRadio == null ||
                _createdFilterRangeFromDatePicker == null ||
                _createdFilterRangeToDatePicker == null)
                return;

            _isSyncingCreatedFilterControls = true;
            try
            {
                _createdFilterSingleModeCombo.SelectedIndex = _createdDateSingleMode switch
                {
                    CreatedDateSingleMode.Before => 1,
                    CreatedDateSingleMode.After => 2,
                    _ => 0
                };
                _createdFilterSingleDatePicker.Value = _createdDateSingleValue.Date;
                _createdFilterRangeFromDatePicker.Value = _createdDateRangeFrom.Date;
                _createdFilterRangeToDatePicker.Value = _createdDateRangeTo.Date;

                _createdFilterTodayRadio.Checked = _createdDateFilterKind == CreatedDateFilterKind.Today;
                _createdFilterSingleRadio.Checked = _createdDateFilterKind == CreatedDateFilterKind.Single;
                _createdFilterRangeRadio.Checked = _createdDateFilterKind == CreatedDateFilterKind.Range;

                if (_createdDateFilterKind == CreatedDateFilterKind.None)
                {
                    _createdFilterTodayRadio.Checked = false;
                    _createdFilterSingleRadio.Checked = false;
                    _createdFilterRangeRadio.Checked = false;
                }
            }
            finally
            {
                _isSyncingCreatedFilterControls = false;
            }

            UpdateCreatedDateFilterControlsState();
        }

        private void CreatedFilterModeControlChanged(object? sender, EventArgs e)
        {
            if (_isSyncingCreatedFilterControls)
                return;

            if (_createdFilterSingleModeCombo != null &&
                _createdFilterSingleRadio != null &&
                ReferenceEquals(sender, _createdFilterSingleModeCombo) &&
                !_createdFilterSingleRadio.Checked)
                _createdFilterSingleRadio.Checked = true;

            if (_createdFilterSingleDatePicker != null &&
                _createdFilterSingleRadio != null &&
                ReferenceEquals(sender, _createdFilterSingleDatePicker) &&
                !_createdFilterSingleRadio.Checked)
                _createdFilterSingleRadio.Checked = true;

            if (_createdFilterRangeFromDatePicker != null &&
                _createdFilterRangeRadio != null &&
                ReferenceEquals(sender, _createdFilterRangeFromDatePicker) &&
                !_createdFilterRangeRadio.Checked)
                _createdFilterRangeRadio.Checked = true;

            if (_createdFilterRangeToDatePicker != null &&
                _createdFilterRangeRadio != null &&
                ReferenceEquals(sender, _createdFilterRangeToDatePicker) &&
                !_createdFilterRangeRadio.Checked)
                _createdFilterRangeRadio.Checked = true;

            UpdateCreatedDateFilterControlsState();
        }

        private void UpdateCreatedDateFilterControlsState()
        {
            if (_createdFilterTodayRadio == null ||
                _createdFilterSingleRadio == null ||
                _createdFilterSingleModeCombo == null ||
                _createdFilterSingleDatePicker == null ||
                _createdFilterRangeRadio == null ||
                _createdFilterRangeFromDatePicker == null ||
                _createdFilterRangeToDatePicker == null ||
                _createdFilterClearButton == null ||
                _createdFilterApplyButton == null)
                return;

            var isSingle = _createdFilterSingleRadio.Checked;
            var isRange = _createdFilterRangeRadio.Checked;

            _createdFilterSingleModeCombo.Enabled = isSingle;
            _createdFilterSingleDatePicker.Enabled = isSingle;
            _createdFilterRangeFromDatePicker.Enabled = isRange;
            _createdFilterRangeToDatePicker.Enabled = isRange;

            var hasSelection = _createdFilterTodayRadio.Checked || isSingle || isRange;
            var isRangeValid = !isRange || _createdFilterRangeFromDatePicker.Value.Date <= _createdFilterRangeToDatePicker.Value.Date;
            var canApply = hasSelection && isRangeValid;
            var canClear = _createdDateFilterKind != CreatedDateFilterKind.None || hasSelection;

            _createdFilterClearButton.Enabled = canClear;
            _createdFilterApplyButton.Enabled = canApply;
            _createdFilterClearButton.ForeColor = canClear
                ? Color.FromArgb(77, 147, 222)
                : Color.FromArgb(168, 197, 225);
            _createdFilterApplyButton.BackColor = canApply
                ? Color.FromArgb(33, 127, 203)
                : Color.FromArgb(176, 212, 242);
            _createdFilterApplyButton.ForeColor = Color.White;
        }

        private void ApplyCreatedDateFilterFromPopup(bool clearFilter)
        {
            if (_createdFilterTodayRadio == null ||
                _createdFilterSingleRadio == null ||
                _createdFilterSingleModeCombo == null ||
                _createdFilterSingleDatePicker == null ||
                _createdFilterRangeRadio == null ||
                _createdFilterRangeFromDatePicker == null ||
                _createdFilterRangeToDatePicker == null)
                return;

            var nextKind = CreatedDateFilterKind.None;
            var nextSingleMode = _createdDateSingleMode;
            var nextSingleDate = _createdDateSingleValue.Date;
            var nextRangeFrom = _createdDateRangeFrom.Date;
            var nextRangeTo = _createdDateRangeTo.Date;

            if (!clearFilter)
            {
                if (_createdFilterTodayRadio.Checked)
                {
                    nextKind = CreatedDateFilterKind.Today;
                }
                else if (_createdFilterSingleRadio.Checked)
                {
                    nextKind = CreatedDateFilterKind.Single;
                    nextSingleMode = _createdFilterSingleModeCombo.SelectedIndex switch
                    {
                        1 => CreatedDateSingleMode.Before,
                        2 => CreatedDateSingleMode.After,
                        _ => CreatedDateSingleMode.ExactDate
                    };
                    nextSingleDate = _createdFilterSingleDatePicker.Value.Date;
                }
                else if (_createdFilterRangeRadio.Checked)
                {
                    var fromDate = _createdFilterRangeFromDatePicker.Value.Date;
                    var toDate = _createdFilterRangeToDatePicker.Value.Date;
                    if (fromDate > toDate)
                        return;

                    nextKind = CreatedDateFilterKind.Range;
                    nextRangeFrom = fromDate;
                    nextRangeTo = toDate;
                }
                else
                {
                    return;
                }
            }

            var changed = _createdDateFilterKind != nextKind ||
                          _createdDateSingleMode != nextSingleMode ||
                          _createdDateSingleValue.Date != nextSingleDate ||
                          _createdDateRangeFrom.Date != nextRangeFrom ||
                          _createdDateRangeTo.Date != nextRangeTo;

            _createdDateFilterKind = nextKind;
            _createdDateSingleMode = nextSingleMode;
            _createdDateSingleValue = nextSingleDate;
            _createdDateRangeFrom = nextRangeFrom;
            _createdDateRangeTo = nextRangeTo;

            if (changed)
                HandleOrdersGridChanged();

            CloseCreatedCalendarDropDown();
            _createdFilterDropDown?.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void CreatedDateFilterDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (_createdFilterLabel == null || _createdFilterGlyph == null)
                return;

            var labelRect = _createdFilterLabel.RectangleToScreen(_createdFilterLabel.ClientRectangle);
            var glyphRect = _createdFilterGlyph.RectangleToScreen(_createdFilterGlyph.ClientRectangle);
            var clickedTrigger = labelRect.Contains(Cursor.Position) || glyphRect.Contains(Cursor.Position);

            if (_isCreatedDateCalendarOpen && !clickedTrigger)
            {
                e.Cancel = true;
                return;
            }

            if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && clickedTrigger)
                _suppressNextCreatedFilterLabelClick = true;

            if (!e.Cancel)
            {
                CloseCreatedCalendarDropDown();
                _isCreatedDateCalendarOpen = false;
            }
        }

        private void ShowReceivedDateFilterDropDown()
        {
            EnsureReceivedDateFilterDropDown();
            SyncReceivedDateFilterPopupState();

            if (_receivedFilterDropDown == null || _receivedFilterGlyph == null)
                return;

            _receivedFilterDropDown.Show(_receivedFilterGlyph, new Point(0, _receivedFilterGlyph.Height));
        }

        private void EnsureReceivedDateFilterDropDown()
        {
            if (_receivedFilterDropDown != null &&
                _receivedFilterTodayRadio != null &&
                _receivedFilterSingleRadio != null &&
                _receivedFilterSingleModeCombo != null &&
                _receivedFilterSingleDatePicker != null &&
                _receivedFilterRangeRadio != null &&
                _receivedFilterRangeFromDatePicker != null &&
                _receivedFilterRangeToDatePicker != null &&
                _receivedFilterClearButton != null &&
                _receivedFilterApplyButton != null)
                return;

            var popupWidth = Math.Max((_receivedFilterLabel?.Width ?? 170) + 260, 440);
            var popupHeight = 206;
            var font = _receivedFilterLabel?.Font ?? Font;
            var row1Y = 16;
            var row2Y = 60;
            var row3Y = 104;
            var bulletX = 16;
            var contentX = 48;
            var singleModeWidth = 172;
            var singleDateX = contentX + singleModeWidth + 10;
            var singleDateWidth = popupWidth - singleDateX - 16;
            var rangeFromX = 88;
            var rangeDateWidth = 146;
            var toLabelX = rangeFromX + rangeDateWidth + 10;
            var toLabelWidth = 38;
            var rangeToX = toLabelX + toLabelWidth;
            var rangeToWidth = popupWidth - rangeToX - 16;
            var buttonsY = 158;

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(popupWidth, popupHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _receivedFilterTodayRadio = new RadioButton
            {
                Location = new Point(bulletX, row1Y),
                Size = new Size(130, 30),
                Text = "Сегодня",
                Font = font,
                AutoCheck = true
            };
            _receivedFilterTodayRadio.CheckedChanged += ReceivedFilterModeControlChanged;

            _receivedFilterSingleRadio = new RadioButton
            {
                Location = new Point(bulletX, row2Y),
                Size = new Size(24, 30),
                Font = font,
                AutoCheck = true
            };
            _receivedFilterSingleRadio.CheckedChanged += ReceivedFilterModeControlChanged;

            _receivedFilterSingleModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = true,
                IntegralHeight = false,
                Location = new Point(contentX, row2Y - 1),
                Size = new Size(singleModeWidth, 32),
                Font = font
            };
            _receivedFilterSingleModeCombo.Items.AddRange(new object[] { "Точная дата", "До", "После" });
            _receivedFilterSingleModeCombo.SelectedIndexChanged += ReceivedFilterModeControlChanged;

            _receivedFilterSingleDatePicker = CreateCreatedFilterDatePicker(new Point(singleDateX, row2Y - 1), new Size(singleDateWidth, 32), font);
            _receivedFilterSingleDatePicker.ValueChanged += ReceivedFilterModeControlChanged;
            AttachReceivedDatePickerCalendarEvents(_receivedFilterSingleDatePicker);

            _receivedFilterRangeRadio = new RadioButton
            {
                Location = new Point(bulletX, row3Y),
                Size = new Size(58, 30),
                Text = "От",
                Font = font,
                AutoCheck = true
            };
            _receivedFilterRangeRadio.CheckedChanged += ReceivedFilterModeControlChanged;

            _receivedFilterRangeFromDatePicker = CreateCreatedFilterDatePicker(new Point(rangeFromX, row3Y - 1), new Size(rangeDateWidth, 32), font);
            _receivedFilterRangeFromDatePicker.ValueChanged += ReceivedFilterModeControlChanged;
            AttachReceivedDatePickerCalendarEvents(_receivedFilterRangeFromDatePicker);

            var toLabel = new Label
            {
                Location = new Point(toLabelX, row3Y),
                Size = new Size(toLabelWidth, 28),
                Text = "До",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = font
            };

            _receivedFilterRangeToDatePicker = CreateCreatedFilterDatePicker(new Point(rangeToX, row3Y - 1), new Size(rangeToWidth, 32), font);
            _receivedFilterRangeToDatePicker.ValueChanged += ReceivedFilterModeControlChanged;
            AttachReceivedDatePickerCalendarEvents(_receivedFilterRangeToDatePicker);

            _receivedFilterClearButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(popupWidth - 232, buttonsY),
                Size = new Size(104, 32),
                Text = "Очистить",
                BackColor = Color.White,
                ForeColor = Color.FromArgb(168, 197, 225)
            };
            _receivedFilterClearButton.FlatAppearance.BorderSize = 0;
            _receivedFilterClearButton.Click += (_, _) => ApplyReceivedDateFilterFromPopup(clearFilter: true);

            _receivedFilterApplyButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(popupWidth - 122, buttonsY),
                Size = new Size(120, 32),
                Text = "Применить",
                BackColor = Color.FromArgb(176, 212, 242),
                ForeColor = Color.White
            };
            _receivedFilterApplyButton.FlatAppearance.BorderSize = 0;
            _receivedFilterApplyButton.Click += (_, _) => ApplyReceivedDateFilterFromPopup(clearFilter: false);

            panel.Controls.Add(_receivedFilterTodayRadio);
            panel.Controls.Add(_receivedFilterSingleRadio);
            panel.Controls.Add(_receivedFilterSingleModeCombo);
            panel.Controls.Add(_receivedFilterSingleDatePicker);
            panel.Controls.Add(_receivedFilterRangeRadio);
            panel.Controls.Add(_receivedFilterRangeFromDatePicker);
            panel.Controls.Add(toLabel);
            panel.Controls.Add(_receivedFilterRangeToDatePicker);
            panel.Controls.Add(_receivedFilterClearButton);
            panel.Controls.Add(_receivedFilterApplyButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _receivedFilterDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(4)
            };
            _receivedFilterDropDown.Closing += ReceivedDateFilterDropDown_Closing;
            _receivedFilterDropDown.Items.Add(host);
            UpdateReceivedDateFilterControlsState();
        }

        private void AttachReceivedDatePickerCalendarEvents(DateTimePicker picker)
        {
            picker.DropDown += ReceivedDatePicker_DropDown;
        }

        private void ReceivedDatePicker_DropDown(object? sender, EventArgs e)
        {
            if (sender is not DateTimePicker picker)
                return;

            BeginInvoke(new Action(() =>
            {
                try
                {
                    SendKeys.Send("{ESC}");
                }
                catch
                {
                    // Игнорируем сбои нативного сворачивания, ниже откроем свой календарь.
                }

                ShowReceivedCalendarDropDown(picker);
            }));
        }

        private void EnsureReceivedCalendarDropDown()
        {
            if (_receivedCalendarDropDown != null && _receivedCalendar != null && _receivedCalendarOkButton != null)
                return;

            _receivedCalendar = new MonthCalendar
            {
                MaxSelectionCount = 1,
                ShowToday = false,
                ShowTodayCircle = false
            };

            _receivedCalendarOkButton = new Button
            {
                Text = "OK",
                FlatStyle = FlatStyle.Standard
            };
            _receivedCalendarOkButton.Click += ReceivedCalendarOkButton_Click;

            var calendarWidth = _receivedCalendar.Width;
            var buttonHeight = 32;
            var buttonTop = _receivedCalendar.Height + 6;
            _receivedCalendarOkButton.Location = new Point(0, buttonTop);
            _receivedCalendarOkButton.Size = new Size(calendarWidth, buttonHeight);

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(calendarWidth, buttonTop + buttonHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            panel.Controls.Add(_receivedCalendar);
            panel.Controls.Add(_receivedCalendarOkButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _receivedCalendarDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(2)
            };
            _receivedCalendarDropDown.Closed += ReceivedCalendarDropDown_Closed;
            _receivedCalendarDropDown.Items.Add(host);
        }

        private void ShowReceivedCalendarDropDown(DateTimePicker picker)
        {
            EnsureReceivedCalendarDropDown();
            if (_receivedCalendarDropDown == null || _receivedCalendar == null)
                return;

            _receivedCalendarTargetPicker = picker;
            var selectedDate = picker.Value.Date;
            _receivedCalendar.SetDate(selectedDate);
            _receivedCalendar.SelectionStart = selectedDate;
            _receivedCalendar.SelectionEnd = selectedDate;

            if (_receivedCalendarDropDown.Visible)
                _receivedCalendarDropDown.Close(ToolStripDropDownCloseReason.CloseCalled);

            _isReceivedDateCalendarOpen = true;
            _receivedCalendarDropDown.Show(picker, new Point(0, picker.Height));
        }

        private void ReceivedCalendarOkButton_Click(object? sender, EventArgs e)
        {
            if (_receivedCalendarTargetPicker != null && _receivedCalendar != null)
            {
                var selectedDate = _receivedCalendar.SelectionStart.Date;
                if (_receivedCalendarTargetPicker.Value.Date != selectedDate)
                    _receivedCalendarTargetPicker.Value = selectedDate;
            }

            CloseReceivedCalendarDropDown();
        }

        private void CloseReceivedCalendarDropDown()
        {
            if (_receivedCalendarDropDown?.Visible == true)
                _receivedCalendarDropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
            else
            {
                _isReceivedDateCalendarOpen = false;
                _receivedCalendarTargetPicker = null;
            }
        }

        private void ReceivedCalendarDropDown_Closed(object? sender, ToolStripDropDownClosedEventArgs e)
        {
            _isReceivedDateCalendarOpen = false;
            _receivedCalendarTargetPicker = null;
        }

        private void SyncReceivedDateFilterPopupState()
        {
            if (_receivedFilterTodayRadio == null ||
                _receivedFilterSingleRadio == null ||
                _receivedFilterSingleModeCombo == null ||
                _receivedFilterSingleDatePicker == null ||
                _receivedFilterRangeRadio == null ||
                _receivedFilterRangeFromDatePicker == null ||
                _receivedFilterRangeToDatePicker == null)
                return;

            _isSyncingReceivedFilterControls = true;
            try
            {
                _receivedFilterSingleModeCombo.SelectedIndex = _receivedDateSingleMode switch
                {
                    CreatedDateSingleMode.Before => 1,
                    CreatedDateSingleMode.After => 2,
                    _ => 0
                };
                _receivedFilterSingleDatePicker.Value = _receivedDateSingleValue.Date;
                _receivedFilterRangeFromDatePicker.Value = _receivedDateRangeFrom.Date;
                _receivedFilterRangeToDatePicker.Value = _receivedDateRangeTo.Date;

                _receivedFilterTodayRadio.Checked = _receivedDateFilterKind == CreatedDateFilterKind.Today;
                _receivedFilterSingleRadio.Checked = _receivedDateFilterKind == CreatedDateFilterKind.Single;
                _receivedFilterRangeRadio.Checked = _receivedDateFilterKind == CreatedDateFilterKind.Range;

                if (_receivedDateFilterKind == CreatedDateFilterKind.None)
                {
                    _receivedFilterTodayRadio.Checked = false;
                    _receivedFilterSingleRadio.Checked = false;
                    _receivedFilterRangeRadio.Checked = false;
                }
            }
            finally
            {
                _isSyncingReceivedFilterControls = false;
            }

            UpdateReceivedDateFilterControlsState();
        }

        private void ReceivedFilterModeControlChanged(object? sender, EventArgs e)
        {
            if (_isSyncingReceivedFilterControls)
                return;

            if (_receivedFilterSingleModeCombo != null &&
                _receivedFilterSingleRadio != null &&
                ReferenceEquals(sender, _receivedFilterSingleModeCombo) &&
                !_receivedFilterSingleRadio.Checked)
                _receivedFilterSingleRadio.Checked = true;

            if (_receivedFilterSingleDatePicker != null &&
                _receivedFilterSingleRadio != null &&
                ReferenceEquals(sender, _receivedFilterSingleDatePicker) &&
                !_receivedFilterSingleRadio.Checked)
                _receivedFilterSingleRadio.Checked = true;

            if (_receivedFilterRangeFromDatePicker != null &&
                _receivedFilterRangeRadio != null &&
                ReferenceEquals(sender, _receivedFilterRangeFromDatePicker) &&
                !_receivedFilterRangeRadio.Checked)
                _receivedFilterRangeRadio.Checked = true;

            if (_receivedFilterRangeToDatePicker != null &&
                _receivedFilterRangeRadio != null &&
                ReferenceEquals(sender, _receivedFilterRangeToDatePicker) &&
                !_receivedFilterRangeRadio.Checked)
                _receivedFilterRangeRadio.Checked = true;

            UpdateReceivedDateFilterControlsState();
        }

        private void UpdateReceivedDateFilterControlsState()
        {
            if (_receivedFilterTodayRadio == null ||
                _receivedFilterSingleRadio == null ||
                _receivedFilterSingleModeCombo == null ||
                _receivedFilterSingleDatePicker == null ||
                _receivedFilterRangeRadio == null ||
                _receivedFilterRangeFromDatePicker == null ||
                _receivedFilterRangeToDatePicker == null ||
                _receivedFilterClearButton == null ||
                _receivedFilterApplyButton == null)
                return;

            var isSingle = _receivedFilterSingleRadio.Checked;
            var isRange = _receivedFilterRangeRadio.Checked;

            _receivedFilterSingleModeCombo.Enabled = isSingle;
            _receivedFilterSingleDatePicker.Enabled = isSingle;
            _receivedFilterRangeFromDatePicker.Enabled = isRange;
            _receivedFilterRangeToDatePicker.Enabled = isRange;

            var hasSelection = _receivedFilterTodayRadio.Checked || isSingle || isRange;
            var isRangeValid = !isRange || _receivedFilterRangeFromDatePicker.Value.Date <= _receivedFilterRangeToDatePicker.Value.Date;
            var canApply = hasSelection && isRangeValid;
            var canClear = _receivedDateFilterKind != CreatedDateFilterKind.None || hasSelection;

            _receivedFilterClearButton.Enabled = canClear;
            _receivedFilterApplyButton.Enabled = canApply;
            _receivedFilterClearButton.ForeColor = canClear
                ? Color.FromArgb(77, 147, 222)
                : Color.FromArgb(168, 197, 225);
            _receivedFilterApplyButton.BackColor = canApply
                ? Color.FromArgb(33, 127, 203)
                : Color.FromArgb(176, 212, 242);
            _receivedFilterApplyButton.ForeColor = Color.White;
        }

        private void ApplyReceivedDateFilterFromPopup(bool clearFilter)
        {
            if (_receivedFilterTodayRadio == null ||
                _receivedFilterSingleRadio == null ||
                _receivedFilterSingleModeCombo == null ||
                _receivedFilterSingleDatePicker == null ||
                _receivedFilterRangeRadio == null ||
                _receivedFilterRangeFromDatePicker == null ||
                _receivedFilterRangeToDatePicker == null)
                return;

            var nextKind = CreatedDateFilterKind.None;
            var nextSingleMode = _receivedDateSingleMode;
            var nextSingleDate = _receivedDateSingleValue.Date;
            var nextRangeFrom = _receivedDateRangeFrom.Date;
            var nextRangeTo = _receivedDateRangeTo.Date;

            if (!clearFilter)
            {
                if (_receivedFilterTodayRadio.Checked)
                {
                    nextKind = CreatedDateFilterKind.Today;
                }
                else if (_receivedFilterSingleRadio.Checked)
                {
                    nextKind = CreatedDateFilterKind.Single;
                    nextSingleMode = _receivedFilterSingleModeCombo.SelectedIndex switch
                    {
                        1 => CreatedDateSingleMode.Before,
                        2 => CreatedDateSingleMode.After,
                        _ => CreatedDateSingleMode.ExactDate
                    };
                    nextSingleDate = _receivedFilterSingleDatePicker.Value.Date;
                }
                else if (_receivedFilterRangeRadio.Checked)
                {
                    var fromDate = _receivedFilterRangeFromDatePicker.Value.Date;
                    var toDate = _receivedFilterRangeToDatePicker.Value.Date;
                    if (fromDate > toDate)
                        return;

                    nextKind = CreatedDateFilterKind.Range;
                    nextRangeFrom = fromDate;
                    nextRangeTo = toDate;
                }
                else
                {
                    return;
                }
            }

            var changed = _receivedDateFilterKind != nextKind ||
                          _receivedDateSingleMode != nextSingleMode ||
                          _receivedDateSingleValue.Date != nextSingleDate ||
                          _receivedDateRangeFrom.Date != nextRangeFrom ||
                          _receivedDateRangeTo.Date != nextRangeTo;

            _receivedDateFilterKind = nextKind;
            _receivedDateSingleMode = nextSingleMode;
            _receivedDateSingleValue = nextSingleDate;
            _receivedDateRangeFrom = nextRangeFrom;
            _receivedDateRangeTo = nextRangeTo;

            if (changed)
                HandleOrdersGridChanged();

            CloseReceivedCalendarDropDown();
            _receivedFilterDropDown?.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void ReceivedDateFilterDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (_receivedFilterLabel == null || _receivedFilterGlyph == null)
                return;

            var labelRect = _receivedFilterLabel.RectangleToScreen(_receivedFilterLabel.ClientRectangle);
            var glyphRect = _receivedFilterGlyph.RectangleToScreen(_receivedFilterGlyph.ClientRectangle);
            var clickedTrigger = labelRect.Contains(Cursor.Position) || glyphRect.Contains(Cursor.Position);

            if (_isReceivedDateCalendarOpen && !clickedTrigger)
            {
                e.Cancel = true;
                return;
            }

            if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && clickedTrigger)
                _suppressNextReceivedFilterLabelClick = true;

            if (!e.Cancel)
            {
                CloseReceivedCalendarDropDown();
                _isReceivedDateCalendarOpen = false;
            }
        }

        private void ShowStatusFilterDropDown()
        {
            EnsureStatusFilterDropDown();
            RefreshStatusFilterChecklist();

            if (_statusFilterDropDown == null)
                return;

            _statusFilterDropDown.Show(picFStatusGlyph, new Point(0, picFStatusGlyph.Height));
        }

        private void EnsureStatusFilterDropDown()
        {
            if (_statusFilterDropDown != null && _statusFilterCheckedList != null)
                return;

            _statusFilterCheckedList = new CheckedListBox
            {
                CheckOnClick = true,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                Font = lblFStatus.Font,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(47, 53, 72),
                Width = Math.Max(lblFStatus.Width + 140, 280),
                Height = 240
            };
            _statusFilterCheckedList.ItemCheck += StatusFilterCheckedList_ItemCheck;

            var host = new ToolStripControlHost(_statusFilterCheckedList)
            {
                AutoSize = false,
                Size = _statusFilterCheckedList.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _statusFilterDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(4)
            };
            _statusFilterDropDown.Closing += StatusFilterDropDown_Closing;
            _statusFilterDropDown.Items.Add(host);
        }

        private void StatusFilterDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason != ToolStripDropDownCloseReason.AppClicked)
                return;

            var labelRect = lblFStatus.RectangleToScreen(lblFStatus.ClientRectangle);
            var glyphRect = picFStatusGlyph.RectangleToScreen(picFStatusGlyph.ClientRectangle);
            if (labelRect.Contains(Cursor.Position) || glyphRect.Contains(Cursor.Position))
                _suppressNextStatusFilterLabelClick = true;
        }

        private void RefreshStatusFilterChecklist()
        {
            if (_statusFilterCheckedList == null)
                return;

            var countsByFilterStatus = GetCountsByFilterStatus();

            _isUpdatingStatusFilterList = true;
            _statusFilterCheckedList.BeginUpdate();
            _statusFilterCheckedList.Items.Clear();

            foreach (var statusName in FilterStatuses)
            {
                countsByFilterStatus.TryGetValue(statusName, out var count);
                var item = new StatusFilterOption(statusName, count);
                _statusFilterCheckedList.Items.Add(item, _selectedFilterStatuses.Contains(statusName));
            }

            _statusFilterCheckedList.EndUpdate();
            _isUpdatingStatusFilterList = false;
        }

        private void StatusFilterCheckedList_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_isUpdatingStatusFilterList)
                return;

            BeginInvoke(new Action(() =>
            {
                UpdateSelectedStatusesFromChecklist();
                ApplyStatusFilterToGrid();
                UpdateStatusFilterCaption();
                RefreshQueuePresentation();
            }));
        }

        private void UpdateSelectedStatusesFromChecklist()
        {
            if (_statusFilterCheckedList == null)
                return;

            _selectedFilterStatuses.Clear();
            foreach (var item in _statusFilterCheckedList.CheckedItems)
            {
                if (item is StatusFilterOption statusItem)
                    _selectedFilterStatuses.Add(statusItem.StatusName);
            }
        }

        private void UpdateStatusFilterCaption()
        {
            lblFStatus.Text = StatusFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void UpdateOrderNoSearchCaption()
        {
            lblFOrderNo.Text = OrderNoSearchLabelText;
            AdjustFilterLabelWidths();
        }

        private void UpdateUserFilterCaption()
        {
            if (_userFilterLabel == null)
                return;

            _userFilterLabel.Text = UserFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void UpdateCreatedDateFilterCaption()
        {
            if (_createdFilterLabel == null)
                return;

            _createdFilterLabel.Text = CreatedDateFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void UpdateReceivedDateFilterCaption()
        {
            if (_receivedFilterLabel == null)
                return;

            _receivedFilterLabel.Text = ReceivedDateFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void AdjustFilterLabelWidths()
        {
            SetFilterLabelWidth(lblFStatus, StatusFilterLabelText, 200);
            SetFilterLabelWidth(lblFOrderNo, OrderNoSearchLabelText, 180);
            SetFilterLabelWidth(_userFilterLabel, UserFilterLabelText, 150);
            SetFilterLabelWidth(_createdFilterLabel, CreatedDateFilterLabelText, 190);
            SetFilterLabelWidth(_receivedFilterLabel, ReceivedDateFilterLabelText, 190);
        }

        private static void SetFilterLabelWidth(Label? label, string text, int minWidth)
        {
            if (label == null)
                return;

            var measuredWidth = TextRenderer.MeasureText(
                text,
                label.Font,
                new Size(int.MaxValue, Math.Max(label.Height, 1)),
                TextFormatFlags.NoPadding).Width;

            label.Width = Math.Max(minWidth, measuredWidth + 16);
        }

        private void ApplyStatusFilterToGrid()
        {
            var hasSelectedStatuses = _selectedFilterStatuses.Count > 0;
            var hasOrderNoFilter = !string.IsNullOrWhiteSpace(_orderNumberFilterText);
            var selectedQueueStatus = GetSelectedQueueStatusName();
            var orderVisibilityByInternalId = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (IsItemTag(rowTag))
                    continue;

                var statusValue = row.Cells[colStatus.Index].Value?.ToString();
                var normalizedStatus = NormalizeStatus(statusValue);
                var queueMatches = MatchesQueueStatus(selectedQueueStatus, normalizedStatus);
                var statusMatches = !hasSelectedStatuses || (normalizedStatus != null && _selectedFilterStatuses.Contains(normalizedStatus));
                var orderNoValue = row.Cells[colOrderNumber.Index].Value?.ToString();
                var orderNoMatches = !hasOrderNoFilter ||
                                     (!string.IsNullOrWhiteSpace(orderNoValue) &&
                                      orderNoValue.IndexOf(_orderNumberFilterText, StringComparison.OrdinalIgnoreCase) >= 0);
                var createdDateValue = row.Cells[colCreated.Index].Value?.ToString();
                var createdDateMatches = MatchesCreatedDateFilter(createdDateValue);
                var receivedDateValue = row.Cells[colReceived.Index].Value?.ToString();
                var receivedDateMatches = MatchesReceivedDateFilter(receivedDateValue);
                var shouldShow = queueMatches && statusMatches && orderNoMatches && createdDateMatches && receivedDateMatches;

                var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
                if (!string.IsNullOrWhiteSpace(orderInternalId))
                    orderVisibilityByInternalId[orderInternalId] = shouldShow;

                try
                {
                    row.Visible = shouldShow;
                }
                catch (InvalidOperationException)
                {
                    // Если строка управляется внешним DataSource, пропускаем скрытие без падения формы.
                }
            }

            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (!IsItemTag(rowTag))
                    continue;

                var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
                var shouldShow = !string.IsNullOrWhiteSpace(orderInternalId) &&
                                 orderVisibilityByInternalId.TryGetValue(orderInternalId, out var visible) &&
                                 visible;

                try
                {
                    row.Visible = shouldShow;
                }
                catch (InvalidOperationException)
                {
                    // Если строка управляется внешним DataSource, пропускаем скрытие без падения формы.
                }
            }
        }

        private void FillQueueCombo(string? preferredStatus)
        {
            var targetStatus = string.IsNullOrWhiteSpace(preferredStatus)
                ? QueueStatuses[0]
                : preferredStatus;

            var previousSync = _isSyncingQueueSelection;
            _isSyncingQueueSelection = true;

            cbQueue.BeginUpdate();
            cbQueue.Items.Clear();
            foreach (var statusName in QueueStatuses)
            {
                cbQueue.Items.Add(new QueueStatusItem(statusName, statusName));
            }
            cbQueue.EndUpdate();

            var targetItem = FindQueueItem(targetStatus);
            if (targetItem != null)
                cbQueue.SelectedItem = targetItem;
            else if (cbQueue.Items.Count > 0)
                cbQueue.SelectedIndex = 0;

            _isSyncingQueueSelection = previousSync;
        }

        private QueueStatusItem? FindQueueItem(string statusName)
        {
            foreach (var item in cbQueue.Items)
            {
                if (item is QueueStatusItem queueItem &&
                    string.Equals(queueItem.StatusName, statusName, StringComparison.Ordinal))
                    return queueItem;
            }

            return null;
        }

        private string? GetSelectedQueueStatusName()
        {
            if (cbQueue.SelectedItem is QueueStatusItem selectedItem)
                return selectedItem.StatusName;

            return null;
        }

        private static string FormatQueueLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var culture = CultureInfo.CurrentCulture;
            var normalized = value.Trim().ToLower(culture);
            if (normalized.Length == 1)
                return normalized.ToUpper(culture);

            return char.ToUpper(normalized[0], culture) + normalized[1..];
        }

        private string GetQueueStatusCountText(string statusName)
        {
            return $"({GetQueueStatusCount(statusName)})";
        }

        private int GetQueueStatusCount(string queueStatusName)
        {
            if (string.Equals(queueStatusName, "Все задания", StringComparison.Ordinal))
                return GetOrdersTotalCount();

            if (!QueueStatusMappings.TryGetValue(queueStatusName, out var mappedStatuses))
                return 0;

            var countsByFilterStatus = GetCountsByFilterStatus();
            var total = 0;
            foreach (var status in mappedStatuses)
            {
                if (countsByFilterStatus.TryGetValue(status, out var count))
                    total += count;
            }

            return total;
        }

        private int GetOrdersTotalCount()
        {
            var total = 0;
            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (IsItemTag(rowTag))
                    continue;

                total++;
            }

            return total;
        }

        private bool MatchesCreatedDateFilter(string? rawCreatedDate)
        {
            if (_createdDateFilterKind == CreatedDateFilterKind.None)
                return true;

            if (!TryParseCreatedDate(rawCreatedDate, out var createdDate))
                return false;

            var date = createdDate.Date;
            return _createdDateFilterKind switch
            {
                CreatedDateFilterKind.Today => date == DateTime.Today,
                CreatedDateFilterKind.Single => _createdDateSingleMode switch
                {
                    CreatedDateSingleMode.Before => date <= _createdDateSingleValue.Date,
                    CreatedDateSingleMode.After => date >= _createdDateSingleValue.Date,
                    _ => date == _createdDateSingleValue.Date
                },
                CreatedDateFilterKind.Range => date >= _createdDateRangeFrom.Date && date <= _createdDateRangeTo.Date,
                _ => true
            };
        }

        private bool MatchesReceivedDateFilter(string? rawReceivedDate)
        {
            if (_receivedDateFilterKind == CreatedDateFilterKind.None)
                return true;

            if (!TryParseCreatedDate(rawReceivedDate, out var receivedDate))
                return false;

            var date = receivedDate.Date;
            return _receivedDateFilterKind switch
            {
                CreatedDateFilterKind.Today => date == DateTime.Today,
                CreatedDateFilterKind.Single => _receivedDateSingleMode switch
                {
                    CreatedDateSingleMode.Before => date <= _receivedDateSingleValue.Date,
                    CreatedDateSingleMode.After => date >= _receivedDateSingleValue.Date,
                    _ => date == _receivedDateSingleValue.Date
                },
                CreatedDateFilterKind.Range => date >= _receivedDateRangeFrom.Date && date <= _receivedDateRangeTo.Date,
                _ => true
            };
        }

        private static bool TryParseCreatedDate(string? rawCreatedDate, out DateTime parsedDate)
        {
            parsedDate = default;
            if (string.IsNullOrWhiteSpace(rawCreatedDate))
                return false;

            var value = rawCreatedDate.Trim();
            var formats = new[]
            {
                "dd.MM.yyyy",
                "d.M.yyyy",
                "dd.MM.yy",
                "d.M.yy",
                "yyyy-MM-dd",
                "yyyy/MM/dd",
                "dd/MM/yyyy",
                "d/M/yyyy",
                "MM/dd/yyyy",
                "M/d/yyyy"
            };

            return DateTime.TryParseExact(value, formats, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDate) ||
                   DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDate) ||
                   DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDate);
        }

        private static Dictionary<string, int> GetCountsByFilterUsers()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var userName in FilterUsers)
                counts[userName] = 0;

            return counts;
        }

        private Dictionary<string, int> GetCountsByFilterStatus()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var status in FilterStatuses)
                counts[status] = 0;

            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (IsItemTag(rowTag))
                    continue;

                var statusValue = row.Cells[colStatus.Index].Value?.ToString();
                var normalizedStatus = NormalizeStatus(statusValue);
                if (normalizedStatus == null)
                    continue;

                counts[normalizedStatus]++;
            }

            return counts;
        }

        private static string? NormalizeStatus(string? rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus))
                return null;

            var value = rawStatus.Trim();
            foreach (var status in FilterStatuses)
            {
                if (string.Equals(value, status, StringComparison.OrdinalIgnoreCase))
                    return status;

                if (value.Contains(status, StringComparison.OrdinalIgnoreCase))
                    return status;
            }

            if (value.Contains("архив", StringComparison.OrdinalIgnoreCase))
                return "В архиве";

            if (value.Contains("отмен", StringComparison.OrdinalIgnoreCase))
                return "Отменено";

            if (value.Contains("ошиб", StringComparison.OrdinalIgnoreCase))
                return "Ошибка";

            if (value.Contains("сборк", StringComparison.OrdinalIgnoreCase)
                || value.Contains("imposing", StringComparison.OrdinalIgnoreCase)
                || value.Contains("pitstop", StringComparison.OrdinalIgnoreCase))
                return "Выполняется сборка";

            if (value.Contains("обрабатыва", StringComparison.OrdinalIgnoreCase)
                || value.Contains("в работе", StringComparison.OrdinalIgnoreCase)
                || value.Contains("запуск", StringComparison.OrdinalIgnoreCase))
                return "Обрабатывается";

            if (value.Contains("ожид", StringComparison.OrdinalIgnoreCase))
                return "Ожидание";

            if (value.Contains("обработано", StringComparison.OrdinalIgnoreCase))
                return "Обработано";

            if (value.Contains("Готово", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Заверш", StringComparison.OrdinalIgnoreCase))
                return "Завершено";

            return null;
        }

        private static bool MatchesQueueStatus(string? queueStatusName, string? normalizedWorkflowStatus)
        {
            if (string.IsNullOrWhiteSpace(queueStatusName)
                || string.Equals(queueStatusName, "Все задания", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(normalizedWorkflowStatus))
                return false;

            if (!QueueStatusMappings.TryGetValue(queueStatusName, out var mappedStatuses))
                return false;

            foreach (var mappedStatus in mappedStatuses)
            {
                if (string.Equals(mappedStatus, normalizedWorkflowStatus, StringComparison.Ordinal))
                    return true;
            }

            return false;
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

            var settings = AppSettings.Load();
            settings.OrdersRootPath = _ordersRootPath;
            settings.TempFolderPath = _tempRootPath;
            settings.GrandpaPath = _grandpaFolder;
            settings.ArchiveDoneSubfolder = _archiveDoneSubfolder;
            settings.HistoryFilePath = _jsonHistoryFile;
            settings.ManagerLogFilePath = _managerLogFilePath;
            settings.OrderLogsFolderPath = _orderLogsFolderPath;
            settings.MaxParallelism = settingsForm.MaxParallelism;
            settings.UseExtendedMode = settingsForm.UseExtendedMode;
            settings.Save();

            Logger.LogFilePath = _managerLogFilePath;
            InitializeProcessor();
            RefreshTrayIndicators();
            SetBottomStatus("Настройки сохранены");
            MessageBox.Show(this, "Настройки сохранены", "MainForm", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

    }
}
