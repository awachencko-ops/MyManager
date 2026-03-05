ï»¿using System;
using System.Windows.Forms;

namespace MyManager
{
    public partial class MainForm : Form
    {
        private string _ordersRootPath = @"C:\MyManager\Orders";
        private string _tempRootPath = string.Empty;
        private string _grandpaFolder = @"C:\MyManager\Archive";
        private string _archiveDoneSubfolder = "ÐÐ¾ÑÐ¾Ð²Ð¾";
        private string _jsonHistoryFile = "history.json";
        private string _managerLogFilePath = "manager.log";
        private string _orderLogsFolderPath = string.Empty;

        public MainForm()
        {
            InitializeComponent();
            LoadSettings();

            // Ð¿ÑÐ¾ÑÑÐ¾ ÑÑÐ¾Ð±Ñ Ð±ÑÐ»Ð¾ Ð²Ð¸Ð´Ð½Ð¾, ÑÑÐ¾ Ð²ÑÑ Ð¶Ð¸Ð²Ð¾Ðµ
            Load += (_, __) =>
            {
                var root = new TreeNode("C60-C70-713D");
                root.Nodes.Add("ÐÑÐµ Ð·Ð°Ð´Ð°Ð½Ð¸Ñ");
                root.Nodes.Add("Ð£Ð´ÐµÑÐ¶Ð°Ð½Ð½ÑÐµ");
                root.Nodes.Add("ÐÐ°Ð¿ÐµÑÐ°ÑÐ°Ð½Ð¾");
                root.Nodes.Add("Ð Ð°ÑÑÐ¸Ð²Ðµ");
                root.Nodes.Add("ÐÑÐ¿Ð¾Ð»Ð½ÑÐµÑÑÑ Ð¿ÐµÑÐ°ÑÑ");
                treeView1.Nodes.Add(root);
                root.Expand();
            };
        }

        // Ð¾Ð±ÑÐ°Ð±Ð¾ÑÑÐ¸Ðº Ð½Ð°Ð¶Ð°ÑÐ¸Ñ ÐºÐ½Ð¾Ð¿Ð¾Ðº Ð² ToolStrip
        private void TsMainActions_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            // Ð¼Ð¾Ð¶Ð½Ð¾ ÑÐ°ÑÐºÐ¸Ð´Ð°ÑÑ switch Ð¿Ð¾ ÐºÐ½Ð¾Ð¿ÐºÐ°Ð¼ Ð¿ÑÐ¸ Ð½ÐµÐ¾Ð±ÑÐ¾Ð´Ð¸Ð¼Ð¾ÑÑÐ¸
            // MessageBox.Show($"ÐÐ°Ð¶Ð°ÑÐ¾: {e.ClickedItem.Text}");
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
            MessageBox.Show(this, "ÐÐ°ÑÑÑÐ¾Ð¹ÐºÐ¸ ÑÐ¾ÑÑÐ°Ð½ÐµÐ½Ñ", "MainForm", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
