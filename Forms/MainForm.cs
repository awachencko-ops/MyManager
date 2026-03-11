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
        private readonly Dictionary<string, int> _printTileImageIndexesByExtension = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _printTileImageIndexesByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _printTilesCacheFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyManager",
            "ThumbnailCache");
        private readonly OrderGridContextMenu _gridMenu = new();
        private readonly ContextMenuStrip _printTilesContextMenu = new();
        private readonly ListView _lvPrintTiles = new();
        private readonly ImageList _printTilesImageList = new();
        private Font? _printTileOrderFont;
        private OrderProcessor? _processor;
        private System.Windows.Forms.Timer? _trayIndicatorsTimer;
        private CancellationTokenSource? _printTilesThumbnailsCts;
        private bool _isRebuildingGrid;
        private bool _isSyncingTileSelection;
        private int _hoveredRowIndex = -1;
        private int _ctxRow = -1;
        private int _ctxCol = -1;
        private Rectangle _dragBoxFromMouseDown = Rectangle.Empty;
        private int _dragSourceRowIndex = -1;
        private int _dragSourceColumnIndex = -1;
        private OrdersViewMode _ordersViewMode = OrdersViewMode.List;

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

        private enum OrdersViewMode
        {
            List,
            Tiles
        }

        private sealed class PrintTileTag
        {
            public PrintTileTag(string orderInternalId, string orderNumber, string printPath, string printFileName)
            {
                OrderInternalId = orderInternalId;
                OrderNumber = orderNumber;
                PrintPath = printPath;
                PrintFileName = printFileName;
            }

            public string OrderInternalId { get; }
            public string OrderNumber { get; }
            public string PrintPath { get; }
            public string PrintFileName { get; }
        }

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
            InitializeOrdersTilesView();
            InitializeViewModeSwitches();
            InitializeOrdersDataFlow();
            InitializeOrderRowContextMenu();
            InitializeActionButtonsState();
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
