using System;
using System.Diagnostics;
using System.IO;
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

        private MenuStrip? _mainMenu;
        private ToolStripMenuItem? _menuParameters;
        private ToolStripMenuItem? _menuSettings;
        private ToolStripMenuItem? _menuManagerLog;
        private Panel? _contentHost;

        public MainForm()
        {
            InitializeComponent();
            LoadSettings();
            SetupTopMenu();

            // просто чтобы было видно, что всё живое
            Load += (_, __) =>
            {
                var root = new TreeNode("C60-C70-713D");
                root.Nodes.Add("Все задания");
                root.Nodes.Add("Удержанные");
                root.Nodes.Add("Напечатано");
                root.Nodes.Add("В архиве");
                root.Nodes.Add("Выполняется печать");
                treeView1.Nodes.Add(root);
                root.Expand();
            };
        }

        // обработчик нажатия кнопок в ToolStrip
        private void TsMainActions_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == tsbConfig)
            {
                ShowSettingsDialog();
                return;
            }

            // можно раскидать switch по кнопкам при необходимости
            // MessageBox.Show($"Нажато: {e.ClickedItem.Text}");
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
            MessageBox.Show(this, "Настройки сохранены", "MainForm", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }


        private void SetupTopMenu()
        {
            _mainMenu = new MenuStrip();
            _menuParameters = new ToolStripMenuItem("Параметры");
            _menuSettings = new ToolStripMenuItem("Настройки");
            _menuManagerLog = new ToolStripMenuItem("Лог менеджера");

            _menuSettings.Click += (_, __) => ShowSettingsDialog();
            _menuManagerLog.Click += (_, __) => OpenManagerLogFile();

            _menuParameters.DropDownItems.Add(_menuSettings);
            _menuParameters.DropDownItems.Add(new ToolStripSeparator());
            _menuParameters.DropDownItems.Add(_menuManagerLog);

            _mainMenu.Items.Add(_menuParameters);
            _mainMenu.Dock = DockStyle.Top;

            MainMenuStrip = _mainMenu;
            Controls.Add(_mainMenu);

            EnsureTopLevelLayout();
            _mainMenu.BringToFront();
        }

        private void EnsureTopLevelLayout()
        {
            if (_contentHost != null)
                return;

            _contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                Name = "contentHost"
            };

            Controls.Add(_contentHost);
            _contentHost.BringToFront();

            _contentHost.Controls.Add(scMain);
            _contentHost.Controls.Add(pnlSidebar);
        }

        private void OpenManagerLogFile()
        {
            if (string.IsNullOrWhiteSpace(_managerLogFilePath))
            {
                MessageBox.Show(this, "Путь к логу не задан", "Лог", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!File.Exists(_managerLogFilePath))
            {
                MessageBox.Show(this, "Файл лога пока не создан", "Лог", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _managerLogFilePath,
                UseShellExecute = true
            });
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
