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
        private readonly List<string> _users = ["Пользователь"];

        private static readonly string[] QueueStatuses =
        {
            "Все задания",
            "Обработанные",
            "В архиве",
            "Обрабатывается",
            "Завершено"
        };

        private bool _isSyncingQueueSelection;
        private string _currentUserName = string.Empty;

        private static readonly Color QueuePanelBackColor = Color.FromArgb(68, 74, 94);
        private static readonly Color QueueHeaderBackColor = Color.FromArgb(103, 163, 216);
        private static readonly Color QueueStatusSelectedBackColor = Color.FromArgb(57, 63, 81);
        private static readonly Color QueueTextColor = Color.FromArgb(244, 247, 252);

        public MainForm()
        {
            InitializeComponent();
            LoadSettings();
            ApplyQueueVisualStyle();
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
            cbQueue.Font = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
            cbQueue.FlatStyle = FlatStyle.Standard;
            cbQueue.IntegralHeight = false;
            cbQueue.DropDownHeight = 240;
            cbQueue.BackColor = Color.White;
            cbQueue.ForeColor = Color.FromArgb(47, 53, 72);
            cbQueue.Size = cbFStatus.Size;
            cbQueue.DropDownWidth = cbFStatus.Width;
        }

        private void InitializeQueueNavigation()
        {
            PopulateQueueTree();

            treeView1.AfterSelect += TreeView1_AfterSelect;
            cbQueue.SelectedIndexChanged += CbQueue_SelectedIndexChanged;

            if (treeView1.Nodes.Count == 0)
                return;

            var firstUserNode = treeView1.Nodes[0];
            _isSyncingQueueSelection = true;
            SelectUser(firstUserNode, QueueStatuses[0]);
            treeView1.SelectedNode = firstUserNode;
            firstUserNode.EnsureVisible();
            _isSyncingQueueSelection = false;
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

            cbQueue.BeginUpdate();
            cbQueue.Items.Clear();
            cbQueue.Items.AddRange(QueueStatuses);
            cbQueue.EndUpdate();

            var targetStatus = string.IsNullOrWhiteSpace(preferredStatus)
                ? QueueStatuses[0]
                : preferredStatus;

            if (cbQueue.Items.Contains(targetStatus))
                cbQueue.SelectedItem = targetStatus;
            else if (cbQueue.Items.Count > 0)
                cbQueue.SelectedIndex = 0;
        }

        private void TreeView1_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (_isSyncingQueueSelection || e.Node == null)
                return;

            var userNode = e.Node.Level == 0 ? e.Node : e.Node.Parent;
            if (userNode == null)
                return;

            var preferredStatus = e.Node.Level == 0
                ? cbQueue.SelectedItem as string
                : e.Node.Text;

            _isSyncingQueueSelection = true;
            SelectUser(userNode, preferredStatus);
            _isSyncingQueueSelection = false;
        }

        private void CbQueue_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isSyncingQueueSelection || cbQueue.SelectedItem is not string selectedStatus)
                return;

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
            if (string.Equals(statusName, "Все задания", StringComparison.Ordinal))
                return dgvJobs.Rows.Count.ToString();

            return "0";
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
