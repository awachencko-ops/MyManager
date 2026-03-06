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
            PopulateQueueDropDown();

            treeView1.AfterSelect += TreeView1_AfterSelect;
            cbQueue.SelectedIndexChanged += CbQueue_SelectedIndexChanged;

            if (cbQueue.Items.Count > 0)
                cbQueue.SelectedIndex = 0;
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

        private void PopulateQueueDropDown()
        {
            cbQueue.BeginUpdate();
            cbQueue.Items.Clear();

            foreach (var userName in _users)
                cbQueue.Items.Add(userName);

            cbQueue.EndUpdate();
        }

        private void CbQueue_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isSyncingQueueSelection || cbQueue.SelectedItem is not string userName)
                return;

            var userNode = FindUserNode(userName);
            if (userNode == null)
                return;

            _isSyncingQueueSelection = true;
            treeView1.SelectedNode = userNode;
            userNode.EnsureVisible();
            _isSyncingQueueSelection = false;
        }

        private void TreeView1_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (_isSyncingQueueSelection || e.Node == null)
                return;

            var userNode = e.Node.Level == 0 ? e.Node : e.Node.Parent;
            if (userNode == null)
                return;

            var userName = userNode.Text;
            if (string.Equals(cbQueue.SelectedItem as string, userName, StringComparison.Ordinal))
                return;

            _isSyncingQueueSelection = true;
            cbQueue.SelectedItem = userName;
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