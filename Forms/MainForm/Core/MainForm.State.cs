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
        private readonly Dictionary<string, int> _printTileImageIndexesByExtension = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _printTileImageIndexesByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _printTilesCacheFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyManager",
            "ThumbnailCache");
        private readonly OrderGridContextMenu _gridMenu = new();
        private readonly ContextMenuStrip _printTilesContextMenu = new();
        private readonly Manina.Windows.Forms.ImageListView _lvPrintTiles = new();
        private readonly ImageList _printTilesImageList = new();
        private Font? _printTileOrderFont;
        private OrderProcessor? _processor;
        private System.Windows.Forms.Timer? _trayIndicatorsTimer;
        private System.Windows.Forms.Timer? _tileHoverActivateTimer;
        private CancellationTokenSource? _printTilesThumbnailsCts;
        private bool _isRebuildingGrid;
        private bool _isSyncingTileSelection;
        private bool _isSyncingGridSelection;
        private int _tileHoverCandidateIndex = -1;
        private string _baseBottomStatusText = DefaultTrayStatusText;
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
        private const int TileHoverActivateDelayMs = 500;
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
