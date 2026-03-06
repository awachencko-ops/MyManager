using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
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
        private static readonly string[] FilterUsers =
        {
            "Андрей",
            "Катя",
            "Вероника"
        };
        private const string StatusFilterLabelText = "Состояние задания";
        private const string OrderNoSearchLabelText = "Номер заказа";
        private const string UserFilterLabelText = "Пользователь";
        private const string CreatedDateFilterLabelText = "Дата поступления";

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
        private readonly HashSet<string> _selectedFilterUsers = new(StringComparer.Ordinal);
        private string _orderNumberFilterText = string.Empty;
        private CreatedDateFilterKind _createdDateFilterKind;
        private CreatedDateSingleMode _createdDateSingleMode = CreatedDateSingleMode.ExactDate;
        private DateTime _createdDateSingleValue = DateTime.Today;
        private DateTime _createdDateRangeFrom = DateTime.Today;
        private DateTime _createdDateRangeTo = DateTime.Today;
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
            ApplyQueueVisualStyle();
            InitializeStatusFilter();
            InitializeOrderNoSearch();
            InitializeUserFilter();
            InitializeCreatedDateFilter();
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
            if (e.ColumnIndex == colStatus.Index || e.ColumnIndex == colCreated.Index || e.ColumnIndex < 0)
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
            UpdateOrderNoSearchCaption();
            UpdateUserFilterCaption();
            UpdateCreatedDateFilterCaption();
            RefreshStatusFilterChecklist();
            RefreshUserFilterChecklist();
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
        }

        private void UpdateOrderNoSearchCaption()
        {
            lblFOrderNo.Text = OrderNoSearchLabelText;
        }

        private void UpdateUserFilterCaption()
        {
            if (_userFilterLabel == null)
                return;

            _userFilterLabel.Text = UserFilterLabelText;
        }

        private void UpdateCreatedDateFilterCaption()
        {
            if (_createdFilterLabel == null)
                return;

            _createdFilterLabel.Text = CreatedDateFilterLabelText;
        }

        private void ApplyStatusFilterToGrid()
        {
            var hasSelectedStatuses = _selectedFilterStatuses.Count > 0;
            var hasOrderNoFilter = !string.IsNullOrWhiteSpace(_orderNumberFilterText);

            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var statusValue = row.Cells[colStatus.Index].Value?.ToString();
                var normalizedStatus = NormalizeStatus(statusValue);
                var statusMatches = !hasSelectedStatuses || (normalizedStatus != null && _selectedFilterStatuses.Contains(normalizedStatus));
                var orderNoValue = row.Cells[colOrderNumber.Index].Value?.ToString();
                var orderNoMatches = !hasOrderNoFilter ||
                                     (!string.IsNullOrWhiteSpace(orderNoValue) &&
                                      orderNoValue.IndexOf(_orderNumberFilterText, StringComparison.OrdinalIgnoreCase) >= 0);
                var createdDateValue = row.Cells[colCreated.Index].Value?.ToString();
                var createdDateMatches = MatchesCreatedDateFilter(createdDateValue);
                var shouldShow = statusMatches && orderNoMatches && createdDateMatches;

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
