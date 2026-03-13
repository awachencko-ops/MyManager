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
        private string _ordersRootPath = AppSettings.DefaultOrdersRootPath;
        private string _tempRootPath = AppSettings.DefaultTempFolderPath;
        private string _grandpaFolder = AppSettings.DefaultGrandpaPath;
        private string _archiveDoneSubfolder = "Готово";
        private string _jsonHistoryFile = AppSettings.DefaultHistoryFilePath;
        private string _managerLogFilePath = AppSettings.DefaultManagerLogFilePath;
        private string _orderLogsFolderPath = AppSettings.DefaultOrderLogsFolderPath;
        private readonly List<OrderData> _orderHistory = [];
        private readonly HashSet<string> _expandedOrderIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CancellationTokenSource> _runTokensByOrder = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _runProgressByOrderInternalId = new(StringComparer.Ordinal);
        private readonly HashSet<string> _archivedFileNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _archivedFilePathsByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _printTileImageIndexesByExtension = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _printTileImageIndexesByPath = new(StringComparer.OrdinalIgnoreCase);
        private string _printTilesCacheFolderPath = AppSettings.DefaultThumbnailCacheFolderPath;
        private string _sharedPrintTilesCacheFolderPath = string.Empty;
        private readonly OrderGridContextMenu _gridMenu = new();
        private readonly ContextMenuStrip _printTilesContextMenu = new();
        private readonly Manina.Windows.Forms.ImageListView _lvPrintTiles = new();
        private readonly ImageList _printTilesImageList = new();
        private PdfAwareFileSystemAdaptor? _pdfThumbnailAdaptor;
        private OrdersViewWarmupCoordinator? _ordersViewWarmupCoordinator;
        private Font? _printTileOrderFont;
        private OrderProcessor? _processor;
        private System.Windows.Forms.Timer? _trayIndicatorsTimer;
        private System.Windows.Forms.Timer? _gridHoverActivateTimer;
        private System.Windows.Forms.Timer? _tileHoverActivateTimer;
        private bool _isRebuildingGrid;
        private bool _isSyncingTileSelection;
        private bool _isSyncingGridSelection;
        private string? _gridHoverCandidateOrderInternalId;
        private int _tileHoverCandidateIndex = -1;
        private string _baseBottomStatusText = DefaultTrayStatusText;
        private int _activeFileTransfers;
        private int _fileTransferProgressPercent = -1;
        private bool _fileTransferIsIndeterminate;
        private string _fileTransferStatusText = string.Empty;
        private DateTime _archiveIndexLoadedAt = DateTime.MinValue;
        private bool _archiveSyncInProgress;
        private int _hoveredRowIndex = -1;
        private int _ctxRow = -1;
        private int _ctxCol = -1;
        private Rectangle _dragBoxFromMouseDown = Rectangle.Empty;
        private int _dragSourceRowIndex = -1;
        private int _dragSourceColumnIndex = -1;
        private OrdersViewMode _ordersViewMode = OrdersViewMode.List;

        private readonly List<string> _users = ["Сервер \"Таудеми\""];
        private readonly List<string> _filterUsers = ["Сервер \"Таудеми\""];
        private string _usersSourceFilePath = AppSettings.DefaultUsersFilePath;
        private string _usersCacheFilePath = AppSettings.DefaultUsersCacheFilePath;
        private bool _usersLoadedFromCache;
        private string _usersDirectoryStatusText = "Пользователи: fallback";
        private DateTime _usersDirectoryLastRefreshAt = DateTime.MinValue;

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
        private const string StatusFilterLabelText = "Состояние задания";
        private const string OrderNoSearchLabelText = "Номер заказа";
        private const string UserFilterLabelText = "Пользователь";
        private const string CreatedDateFilterLabelText = "В препрессе";
        private const string ReceivedDateFilterLabelText = "Заказ принят";
        private const string DefaultTrayStatusText = "Готово";
        private const int TrayIndicatorsRefreshIntervalMs = 15000;
        private static readonly TimeSpan ArchiveIndexLifetime = TimeSpan.FromSeconds(15);
        private const int OrdersGridWarmupIntervalMs = 3000;
        private const int GridHoverActivateDelayMs = 500;
        private const int TileHoverActivateDelayMs = 500;
        private const int UsersDirectoryRefreshIntervalMs = 60000;
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
        private TreeNode? _hoveredQueueNode;
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

        private static readonly Color QueuePanelBackColor = Color.FromArgb(55, 65, 81); // #374151
        private static readonly Color QueuePanelDividerColor = Color.FromArgb(44, 55, 70); // subtle rail/menu split
        private static readonly Color QueueHeaderBackColor = Color.FromArgb(55, 65, 81); // #374151
        private static readonly Color QueueHeaderTextColor = Color.FromArgb(229, 231, 235); // #E5E7EB
        private static readonly Color QueueHeaderSecondaryTextColor = Color.FromArgb(156, 163, 175); // #9CA3AF
        private static readonly Color QueueHeaderOnlineIndicatorColor = Color.FromArgb(52, 211, 153); // #34D399
        private static readonly Color QueueHeaderOfflineIndicatorColor = Color.FromArgb(107, 114, 128); // #6B7280
        private static readonly Color QueueStatusHoverBackColor = Color.FromArgb(65, 75, 90); // rgba(255,255,255,0.05) over #374151
        private static readonly Color QueueStatusSelectedBackColor = Color.FromArgb(63, 77, 95); // calmer active bg
        private static readonly Color QueueStatusSelectedTextColor = Color.FromArgb(243, 246, 251);
        private static readonly Color QueueActiveMarkerColor = Color.FromArgb(96, 165, 250); // #60A5FA
        private static readonly Color QueueTextColor = Color.FromArgb(209, 213, 219); // #D1D5DB
        private static readonly Color QueueCounterTextColor = Color.FromArgb(156, 163, 175); // #9CA3AF
        private static readonly Color QueueCounterZeroTextColor = Color.FromArgb(107, 114, 128); // #6B7280
        private static readonly Color QueueCounterSelectedTextColor = Color.FromArgb(193, 201, 212);
        private static readonly Color QueueCounterSelectedZeroTextColor = Color.FromArgb(145, 154, 167);
        private static readonly Color OrdersRowBaseBackColor = Color.FromArgb(255, 255, 255);   // #FFFFFF
        private static readonly Color OrdersRowZebraBackColor = Color.FromArgb(252, 253, 254);  // #FCFDFE
        private static readonly Color OrdersRowHoverBackColor = Color.FromArgb(248, 250, 252);  // #F8FAFC
        private static readonly Color OrdersRowSelectedBackColor = Color.FromArgb(243, 247, 251); // #F3F7FB
        private static readonly Color OrdersGridLineColor = Color.FromArgb(231, 235, 240); // #E7EBF0
        private static readonly Color OrdersActiveMarkerColor = Color.FromArgb(122, 167, 217); // #7AA7D9
        private static readonly Color OrdersLinkTextColor = Color.FromArgb(95, 126, 168);

        private enum OrdersViewMode
        {
            List,
            Tiles
        }

        internal sealed class PrintTileTag
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

    }
}
