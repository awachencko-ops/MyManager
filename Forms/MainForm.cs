using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

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
        // Обрабатывается: Выполняется сборка, Обрабатывается
        // Задержанные: Отменено, Ошибка
        // Завершено: Завершено
        private static readonly string[] FilterStatuses =
        {
            "Обработано",
            "В архиве",
            "Выполняется сборка",
            "Обрабатывается",
            "Отменено",
            "Ошибка",
            "Завершено"
        };
        private const string StatusFilterLabelText = "✓ Состояние задания";

        private static readonly Dictionary<string, string[]> QueueStatusMappings = new(StringComparer.Ordinal)
        {
            ["Обработанные"] = ["Обработано"],
            ["В архиве"] = ["В архиве"],
            ["Обрабатывается"] = ["Выполняется сборка", "Обрабатывается"],
            ["Задержанные"] = ["Отменено", "Ошибка"],
            ["Завершено"] = ["Завершено"]
        };

        private bool _isSyncingQueueSelection;
        private string _currentUserName = string.Empty;
        private readonly HashSet<string> _selectedFilterStatuses = new(StringComparer.Ordinal);
        private ToolStripDropDown? _statusFilterDropDown;
        private CheckedListBox? _statusFilterCheckedList;
        private bool _isUpdatingStatusFilterList;
        private bool _suppressNextStatusFilterLabelClick;

        private static readonly Color QueuePanelBackColor = Color.FromArgb(68, 74, 94);
        private static readonly Color QueueHeaderBackColor = Color.FromArgb(103, 163, 216);
        private static readonly Color QueueStatusSelectedBackColor = Color.FromArgb(57, 63, 81);
        private static readonly Color QueueTextColor = Color.FromArgb(244, 247, 252);

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

        public MainForm()
        {
            InitializeComponent();
            LoadSettings();
            ApplyQueueVisualStyle();
            InitializeStatusFilter();
            InitializeQueueNavigation();
        }

        // обработчик нажатия кнопок в ToolStrip
        private void TsMainActions_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == tsbParameters)
            {
                ShowSettingsDialog();
            }
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

            if (treeView1.Nodes.Count == 0)
                return;

            var firstUserNode = treeView1.Nodes[0];
            _isSyncingQueueSelection = true;
            SelectUser(firstUserNode, QueueStatuses[0]);
            treeView1.SelectedNode = firstUserNode;
            firstUserNode.EnsureVisible();
            _isSyncingQueueSelection = false;
        }

        private void InitializeStatusFilter()
        {
            lblFStatus.Click += LblFStatus_Click;
            UpdateStatusFilterCaption();
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
            if (e.ColumnIndex == colStatus.Index || e.ColumnIndex < 0)
                HandleOrdersGridChanged();
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
            ApplyStatusFilterToGrid();
            UpdateStatusFilterCaption();
            RefreshStatusFilterChecklist();
            RefreshQueuePresentation();
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

        private void ShowStatusFilterDropDown()
        {
            EnsureStatusFilterDropDown();
            RefreshStatusFilterChecklist();

            if (_statusFilterDropDown == null)
                return;

            _statusFilterDropDown.Show(lblFStatus, new Point(0, lblFStatus.Height));
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
            if (labelRect.Contains(Cursor.Position))
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
        }

        private void ApplyStatusFilterToGrid()
        {
            var hasSelectedStatuses = _selectedFilterStatuses.Count > 0;

            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var statusValue = row.Cells[colStatus.Index].Value?.ToString();
                var normalizedStatus = NormalizeStatus(statusValue);
                var shouldShow = !hasSelectedStatuses || (normalizedStatus != null && _selectedFilterStatuses.Contains(normalizedStatus));

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

                total++;
            }

            return total;
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

            if (value.Contains("Готово", StringComparison.OrdinalIgnoreCase))
                return "Завершено";

            return null;
        }

        private void ShowSettingsDialog()
        {
            using var settingsForm = new SettingsDialogForm(
                _ordersRootPath,
                _tempRootPath,
                _grandpaFolder,
                _archiveDoneSubfolder,
                _jsonHistoryFile,
                _managerLogFilePath,
                _orderLogsFolderPath,
                AppSettings.Load().MaxParallelism);

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
            settings.Save();

            Logger.LogFilePath = _managerLogFilePath;
            MessageBox.Show(this, "Настройки сохранены", "MainForm", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

    }
}
