using System;
using System.Collections.Generic;
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
        private readonly List<string> _users = new List<string> { "Пользователь" };

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

        public MainForm()
        {
            InitializeComponent();
            LoadSettings();
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

        private void scMain_Panel2_Paint(object sender, PaintEventArgs e)
        {

        }

        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void pnlHeader_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
