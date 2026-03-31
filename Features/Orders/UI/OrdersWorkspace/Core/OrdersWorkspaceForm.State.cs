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
using System.Net.Http;
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
        private string _ordersRootPath = AppSettings.DefaultOrdersRootPath;
        private string _tempRootPath = AppSettings.DefaultTempFolderPath;
        private string _grandpaFolder = AppSettings.DefaultGrandpaPath;
        private string _archiveDoneSubfolder = "Готово";
        private string _jsonHistoryFile = AppSettings.DefaultHistoryFilePath;
        private OrdersStorageMode _ordersStorageBackend = OrdersStorageMode.LanPostgreSql;
        private string _lanPostgreSqlConnectionString = AppSettings.DefaultLanPostgreSqlConnectionString;
        private string _lanApiBaseUrl = AppSettings.DefaultLanApiBaseUrl;
        private readonly IOrderApplicationService _orderApplicationService;
        private readonly ILanApiIdentityService _lanApiIdentityService;
        private readonly ILanOrderPushClient _lanOrderPushClient;
        private string _managerLogFilePath = AppSettings.DefaultManagerLogFilePath;
        private string _orderLogsFolderPath = AppSettings.DefaultOrderLogsFolderPath;
        private readonly List<OrderData> _orderHistory = [];
        private readonly HashSet<string> _expandedOrderIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CancellationTokenSource> _runTokensByOrder = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _runProgressByOrderInternalId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DependencyHealthLevel> _dependencyHealthByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly HttpClient _lanStatusHttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private readonly ConnectionStatusPopup _connectionStatusPopup = new();
        private readonly object _lanServerProbeSync = new();
        private readonly ISettingsProvider _settingsProvider;
        private readonly HashSet<string> _archivedFileNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _archivedFilePathsByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _archivedFilePathsByHash = new(StringComparer.OrdinalIgnoreCase);
        private int _archiveHashIndexBuildVersion;
        private bool _archiveHashIndexBuildInProgress;
        private int _archiveIndexRefreshInProgress;
        private bool _archiveIndexInitialized;
        private readonly Dictionary<string, int> _printTileImageIndexesByExtension = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _printTileImageIndexesByPath = new(StringComparer.OrdinalIgnoreCase);
        private string _printTilesCacheFolderPath = AppSettings.DefaultThumbnailCacheFolderPath;
        private string _sharedPrintTilesCacheFolderPath = string.Empty;
        private readonly OrderGridContextMenu _gridMenu = new();
        private readonly ContextMenuStrip _groupOrderContextMenu = new();
        private readonly ContextMenuStrip _printTilesContextMenu = new();
        private readonly Manina.Windows.Forms.ImageListView _lvPrintTiles = new();
        private readonly ImageList _printTilesImageList = new();
        private PdfAwareFileSystemAdaptor? _pdfThumbnailAdaptor;
        private OrdersViewWarmupCoordinator? _ordersViewWarmupCoordinator;
        private Font? _printTileOrderFont;
        private OrderProcessor? _processor;
        private System.Windows.Forms.Timer? _trayIndicatorsTimer;
        private System.Windows.Forms.Timer? _searchDebounceTimer;
        private System.Windows.Forms.Timer? _gridRefreshCoalesceTimer;
        private System.Windows.Forms.Timer? _gridDerivedRefreshCoalesceTimer;
        private System.Windows.Forms.Timer? _gridHoverActivateTimer;
        private System.Windows.Forms.Timer? _tileHoverActivateTimer;
        private bool _isRebuildingGrid;
        private bool _gridRefreshPending;
        private bool _gridRefreshPendingForceFullRebuild;
        private string? _gridRefreshPendingSelectedTag;
        private string? _gridRefreshPendingTargetOrderInternalId;
        private bool _gridDerivedRefreshPending;
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
        private DateTime _archiveStatusSyncLastAt = DateTime.MinValue;
        private bool _archiveSyncInProgress;
        private int _hoveredRowIndex = -1;
        private int _ctxRow = -1;
        private int _ctxCol = -1;
        private int _gridMouseDownRowIndex = -1;
        private bool _gridMouseDownRowWasSelected;
        private Rectangle _dragBoxFromMouseDown = Rectangle.Empty;
        private int _dragSourceRowIndex = -1;
        private int _dragSourceColumnIndex = -1;
        private bool _lanServerProbeInProgress;
        private int _lanServerProbeRequestCount;
        private DateTime _lanServerProbeLastRequestedUtc = DateTime.MinValue;
        private DateTime _lanServerProbeLastSuccessfulUtc = DateTime.MinValue;
        private CancellationTokenSource? _lanServerProbeCts;
        private LanServerProbeSnapshot _lanServerProbeSnapshot = LanServerProbeSnapshot.CreateInitial();
        private bool _lanApiRecoveryInProgress;
        private bool _lanConnectionRecoveryActionEnabled;
        private DateTime _lanApiAutoStartLastAttemptUtc = DateTime.MinValue;
        private DateTime _lanApiAutoStartLastSuccessUtc = DateTime.MinValue;
        private bool _lanPushPressureAckActionEnabled;
        private readonly object _lanPushRefreshSync = new();
        private readonly object _lanPushMetricsSync = new();
        private int _lanPushRefreshInProgress;
        private int _lanPushRefreshPending;
        private LanOrderPushEvent _lanPushPendingEvent = new(
            LanOrderPushEventNames.ForceRefresh,
            string.Empty,
            "startup",
            DateTime.UtcNow);
        private bool _lanPushConnected;
        private string _lanPushConnectionState = LanOrderPushConnectionStates.Stopped;
        private DateTime _lanPushConnectionStateAtUtc = DateTime.MinValue;
        private DateTime _lanPushLastEventAtUtc = DateTime.MinValue;
        private DateTime _lanPushLastRefreshAtUtc = DateTime.MinValue;
        private string _lanPushLastEventType = string.Empty;
        private string _lanPushLastForceRefreshReason = string.Empty;
        private double _lanPushLastEventLagMs = -1;
        private long _lanPushEventsReceivedCount;
        private long _lanPushRefreshAppliedCount;
        private long _lanPushCoalescedEventsCount;
        private long _lanPushThrottleDelayCount;
        private int _lanPushReconnectCount;
        private long _lanPushPressureAlertCount;
        private readonly Dictionary<string, long> _lanPushReasonCounters = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lanPushLastPressureAlertAtUtc = DateTime.MinValue;
        private bool _connectionStatusToolTipVisible;
        private bool _pendingConnectionIndicatorRefresh;
        private string _connectionStatusToolTipContent = string.Empty;
        private ToolStripItem? _trayStatusPopupTargetItem;
        private string _trayStatusPopupContent = string.Empty;
        private bool _serverHardLockActive;
        private bool _serverHardLockManagedByLan;
        private DateTime _serverHardLockLastDialogUtc = DateTime.MinValue;
        private Panel? _serverHardLockOverlayPanelMain;
        private Panel? _serverHardLockOverlayPanelQueue;
        private Label? _serverHardLockOverlayTitleMain;
        private Label? _serverHardLockOverlayDetailsMain;
        private string _lastServerOpsProbeLogFingerprint = string.Empty;
        private OrdersViewMode _ordersViewMode = OrdersViewMode.List;

        private readonly List<string> _users = [UserIdentityResolver.DefaultDisplayName];
        private readonly List<string> _filterUsers = [UserIdentityResolver.DefaultDisplayName];
        private readonly Dictionary<string, string> _serverUsersByDisplayName = new(StringComparer.OrdinalIgnoreCase)
        {
            [UserIdentityResolver.DefaultDisplayName] = UserIdentityResolver.DefaultServerName
        };
        private string _usersSourceFilePath = AppSettings.DefaultUsersFilePath;
        private string _usersCacheFilePath = AppSettings.DefaultUsersCacheFilePath;
        private bool _usersLoadedFromCache;
        private string _usersDirectoryStatusText = "Пользователи: не проверены";
        private DateTime _usersDirectoryLastRefreshAt = DateTime.MinValue;
        private int _usersDirectoryRefreshInProgress;
        private int _ordersDataBootstrapInProgress;
        private bool _ordersDataBootstrapCompleted;
        private long _ordersGridRebuildCount;
        private double _ordersGridRebuildLastMs;
        private double _ordersGridRebuildMaxMs;
        private Dictionary<string, int>? _queueStatusCountsCache;
        private int _queueTotalOrdersCountCache;
        private bool _queueStatusCountsCacheValid;
        private int _visibleOrdersCountCache;
        private bool _visibleOrdersCountCacheValid;
        private readonly Dictionary<string, (bool Exists, long CheckedAtUtcTicks)> _uiFileExistsCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _uiFileExistsCacheSync = new();

        private static readonly string[] QueueStatuses = QueueStatusNames.All;

        // Сопоставление рабочих статусов с группами очереди (treeView1/cbQueue):
        // Обработанные: Обработано
        // В архиве: В архиве
        // Обрабатывается: Сборка, Обрабатывается, Ожидание
        // Задержанные: Отменено, Ошибка
        // Завершено: Завершено
        private static readonly string[] FilterStatuses = WorkflowStatusNames.Filterable;
        private const string StatusFilterLabelText = "Состояние задания";
        private const string OrderNoSearchLabelText = "Номер заказа";
        private const string UserFilterLabelText = "Пользователь";
        private const string CreatedDateFilterLabelText = "В препрессе";
        private const string ReceivedDateFilterLabelText = "Заказ принят";
        private const string DefaultTrayStatusText = "Готово";
        private const int TrayIndicatorsRefreshIntervalMs = 15000;
        private const int LanServerProbeMinIntervalMs = 5000;
        private const int LanServerProbeFailureThreshold = 3;
        private static readonly TimeSpan LanApiAutoStartCooldown = TimeSpan.FromSeconds(20);
        private const double LanSloAvailabilityTarget = 0.995;
        private const double LanSloLatencyP95TargetMs = 500;
        private const double LanSloWriteSuccessTarget = 0.99;
        private int LanPushMinRefreshIntervalMs = AppSettings.DefaultLanPushMinRefreshIntervalMs;
        private const int LanPushReasonCountersMaxItems = 4;
        private int LanPushPressureAlertMinEvents = AppSettings.DefaultLanPushPressureAlertMinEvents;
        private double LanPushCoalescedRateAlertThreshold = AppSettings.DefaultLanPushCoalescedRateAlertThreshold;
        private double LanPushThrottledRateAlertThreshold = AppSettings.DefaultLanPushThrottledRateAlertThreshold;
        private TimeSpan LanPushPressureAlertCooldown = TimeSpan.FromSeconds(AppSettings.DefaultLanPushPressureAlertCooldownSeconds);
        private TimeSpan LanPushPressureHintActiveWindow = TimeSpan.FromSeconds(AppSettings.DefaultLanPushPressureHintActiveWindowSeconds);
        private TimeSpan LanPushPressureStateResetWindow = TimeSpan.FromSeconds(AppSettings.DefaultLanPushPressureStateResetWindowSeconds);
        private static readonly TimeSpan ArchiveIndexLifetime = TimeSpan.FromSeconds(60);
        private const int ArchiveStatusSyncIntervalMs = 60000;
        private const int OrdersGridWarmupIntervalMs = 3000;
        private const int GridHoverActivateDelayMs = 500;
        private const int TileHoverActivateDelayMs = 500;
        private const int UsersDirectoryRefreshIntervalMs = 60000;
        private const int SearchDebounceIntervalMs = 180;
        private const int GridRefreshCoalesceIntervalMs = 70;
        private const int GridDerivedRefreshCoalesceIntervalMs = 90;
        private const int UiFileExistsCacheTtlMs = 1200;
        private const double OrdersGridRebuildWarnThresholdMs = 140d;
        private const long DiskWarningThresholdBytes = 10L * 1024 * 1024 * 1024;
        private const long DiskCriticalThresholdBytes = 5L * 1024 * 1024 * 1024;

        private static readonly IReadOnlyDictionary<string, string[]> QueueStatusMappings = WorkflowStatusNames.QueueMappings;

        private bool _isSyncingQueueSelection;
        private bool _suppressNextQueuePresentationRefresh;
        private TreeNode? _hoveredQueueNode;
        private string _currentUserName = string.Empty;
        private string _currentUserRoleText = "Права не определены";
        private string _currentUserAuthStateText = "Сессия не установлена";
        private Label? _userProfileNameLabel;
        private Label? _userProfileRoleLabel;
        private Label? _userProfileAuthStateLabel;
        private LinkLabel? _userProfileSessionActionLabel;
        private bool _currentUserProfileRefreshInProgress;
        private bool _currentUserUsesBearerSession;
        private bool _currentUserSessionActionInProgress;
        private bool _suppressAutoSessionBootstrap;
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

        private const int QueueCounterPillHeight = 22;
        private const int QueueCounterPillMinWidth = 22;
        private const int QueueCounterPillRadius = 11;
        private const int QueueCounterPillHorizontalPadding = 7;

        private static readonly Color QueuePanelBackColor = Color.FromArgb(245, 245, 245); // #F5F5F5
        private static readonly Color QueuePanelDividerColor = Color.FromArgb(224, 227, 230); // #E0E3E6
        private static readonly Color QueueHeaderBackColor = Color.FromArgb(245, 245, 245); // #F5F5F5
        private static readonly Color QueueHeaderTextColor = Color.FromArgb(30, 30, 30); // #1E1E1E
        private static readonly Color QueueHeaderSecondaryTextColor = Color.FromArgb(113, 113, 113); // #717171
        private static readonly Color QueueHeaderOnlineIndicatorColor = Color.FromArgb(52, 211, 153); // #34D399
        private static readonly Color QueueHeaderOfflineIndicatorColor = Color.FromArgb(156, 156, 156); // #9C9C9C
        private static readonly Color QueueStatusHoverBackColor = Color.FromArgb(238, 238, 242); // #EEEEF2
        private static readonly Color QueueStatusSelectedBackColor = Color.FromArgb(230, 231, 233); // #E6E7E9
        private static readonly Color QueueStatusSelectedTextColor = Color.FromArgb(30, 30, 30); // #1E1E1E
        private static readonly Color QueueActiveMarkerColor = Color.FromArgb(101, 101, 101); // #656565
        private static readonly Color QueueTextColor = Color.FromArgb(30, 30, 30); // #1E1E1E
        private static readonly Color QueueCounterTextColor = Color.FromArgb(101, 101, 101); // #656565
        private static readonly Color QueueCounterZeroTextColor = Color.FromArgb(156, 156, 156); // #9C9C9C
        private static readonly Color QueueCounterSelectedTextColor = Color.FromArgb(255, 255, 255);
        private static readonly Color QueueCounterSelectedZeroTextColor = Color.FromArgb(231, 231, 231); // #E7E7E7
        private static readonly Color QueueCounterPillBackColor = Color.FromArgb(224, 227, 230); // #E0E3E6
        private static readonly Color QueueCounterPillZeroBackColor = Color.FromArgb(240, 240, 240); // #F0F0F0
        private static readonly Color QueueCounterPillSelectedBackColor = Color.FromArgb(101, 101, 101); // #656565
        private static readonly Color QueueCounterPillSelectedZeroBackColor = Color.FromArgb(143, 143, 143); // #8F8F8F
        private static readonly Color OrdersRowBaseBackColor = Color.FromArgb(255, 255, 255);   // #FFFFFF
        private static readonly Color OrdersRowZebraBackColor = Color.FromArgb(252, 253, 254);  // #FCFDFE
        private static readonly Color OrdersRowHoverBackColor = Color.FromArgb(248, 250, 252);  // #F8FAFC
        private static readonly Color OrdersRowSelectedBackColor = Color.FromArgb(243, 247, 251); // #F3F7FB
        private static readonly Color OrdersGridLineColor = Color.FromArgb(231, 235, 240); // #E7EBF0
        private static readonly Color OrdersActiveMarkerColor = Color.FromArgb(122, 167, 217); // #7AA7D9
        private static readonly Color OrdersLinkTextColor = Color.FromArgb(95, 126, 168);
        private static readonly Color GroupOrderIconBackColor = Color.FromArgb(247, 200, 119); // folder-like accent
        private static readonly Color GroupOrderRowBackColor = Color.FromArgb(255, 252, 244);
        private static readonly Color GroupOrderRowHoverBackColor = Color.FromArgb(255, 249, 238);
        private static readonly Color GroupOrderRowSelectedBackColor = Color.FromArgb(255, 246, 232);
        private static readonly Color GroupOrderItemRowBaseBackColor = Color.FromArgb(255, 255, 255);
        private static readonly Color GroupOrderItemRowZebraBackColor = Color.FromArgb(255, 253, 248);
        private static readonly Color GroupOrderItemRowHoverBackColor = Color.FromArgb(255, 251, 244);
        private static readonly Color GroupOrderItemRowSelectedBackColor = Color.FromArgb(255, 248, 238);

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

        private sealed class LanServerProbeSnapshot
        {
            public static LanServerProbeSnapshot CreateInitial()
            {
                return new LanServerProbeSnapshot
                {
                    ReadyStatus = "unknown",
                    SloStatus = "unknown",
                    LiveStatus = "unknown",
                    ProbeReason = "startup",
                    ConsecutiveFailureCount = 0
                };
            }

            public bool ApiReachable { get; init; }
            public bool IsReady { get; init; }
            public bool IsDegraded { get; init; }
            public bool SloHealthy { get; init; }
            public DateTime RequestedAtUtc { get; init; }
            public DateTime CompletedAtUtc { get; init; }
            public DateTime SuccessfulAtUtc { get; init; }
            public DateTime ServerNowAtUtc { get; init; }
            public string LiveStatus { get; init; } = "unknown";
            public string ReadyStatus { get; init; } = "unknown";
            public string SloStatus { get; init; } = "unknown";
            public string Error { get; init; } = string.Empty;
            public string ProbeReason { get; init; } = string.Empty;
            public double AvailabilityRatio { get; init; } = -1;
            public double LatencyP95Ms { get; init; } = -1;
            public double WriteSuccessRatio { get; init; } = -1;
            public int ConsecutiveFailureCount { get; init; }
            public long HttpRequests5xx { get; init; } = -1;
            public long WriteBadRequest { get; init; } = -1;
            public DateTime LastServerEventAtUtc { get; init; }
            public string LastServerEventType { get; init; } = string.Empty;
            public string LastServerEventOrderId { get; init; } = string.Empty;
            public long PushPublishedTotal { get; init; } = -1;
            public long PushPublishFailuresTotal { get; init; } = -1;
            public double PushPublishSuccessRatio { get; init; } = -1;
            public string ProcessAlert { get; init; } = string.Empty;
            public DateTime ProcessAlertAtUtc { get; init; }
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

