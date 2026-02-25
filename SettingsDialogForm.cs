using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyManager
{
    public class SettingsDialogForm : Form
    {
        private readonly Action _openPitStopManager;
        private readonly Action _openImposingManager;

        private readonly TabControl _tabs = new TabControl();
        private readonly TextBox _txtOrdersRoot = new TextBox();
        private readonly TextBox _txtTempRoot = new TextBox();

        public string OrdersRootPath => _txtOrdersRoot.Text.Trim();
        public string TempRootPath => _txtTempRoot.Text.Trim();

        public SettingsDialogForm(string ordersRootPath, string tempRootPath, Action openPitStopManager, Action openImposingManager)
        {
            _openPitStopManager = openPitStopManager;
            _openImposingManager = openImposingManager;

            Text = "Настройки";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 420);
            Size = new Size(760, 480);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.Add(CreateGeneralTab(ordersRootPath, tempRootPath));
            _tabs.TabPages.Add(CreatePitStopTab());
            _tabs.TabPages.Add(CreateImposingTab());

            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };

            var btnOk = new Button { Text = "Сохранить", AutoSize = true };
            btnOk.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(OrdersRootPath) || string.IsNullOrWhiteSpace(TempRootPath))
                {
                    MessageBox.Show(this, "Заполните обе папки в разделе 'Основное'.", "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _tabs.SelectedIndex = 0;
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            };

            var btnCancel = new Button { Text = "Отмена", AutoSize = true };
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            bottomPanel.Controls.Add(btnOk);
            bottomPanel.Controls.Add(btnCancel);

            root.Controls.Add(_tabs, 0, 0);
            root.Controls.Add(bottomPanel, 0, 1);
            Controls.Add(root);
        }

        private TabPage CreateGeneralTab(string ordersRootPath, string tempRootPath)
        {
            var page = new TabPage("Основное");
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(10)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtOrdersRoot.Dock = DockStyle.Fill;
            _txtOrdersRoot.Text = ordersRootPath;

            _txtTempRoot.Dock = DockStyle.Fill;
            _txtTempRoot.Text = tempRootPath;

            var btnBrowseOrders = new Button { Text = "Обзор...", Dock = DockStyle.Fill };
            btnBrowseOrders.Click += (s, e) => BrowseFolder(_txtOrdersRoot);

            var btnBrowseTemp = new Button { Text = "Обзор...", Dock = DockStyle.Fill };
            btnBrowseTemp.Click += (s, e) => BrowseFolder(_txtTempRoot);

            panel.Controls.Add(new Label { Text = "Папка хранения", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            panel.Controls.Add(_txtOrdersRoot, 1, 0);
            panel.Controls.Add(btnBrowseOrders, 2, 0);

            panel.Controls.Add(new Label { Text = "Папка временных файлов", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            panel.Controls.Add(_txtTempRoot, 1, 1);
            panel.Controls.Add(btnBrowseTemp, 2, 1);

            page.Controls.Add(panel);
            return page;
        }

        private TabPage CreatePitStopTab()
        {
            var page = new TabPage("Диспетчер PitStop");
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(16),
                WrapContents = false
            };

            panel.Controls.Add(new Label
            {
                Text = "Управление пресетами PitStop.",
                AutoSize = true
            });

            var btnOpen = new Button { Text = "Открыть диспетчер PitStop", AutoSize = true };
            btnOpen.Click += (s, e) => _openPitStopManager();
            panel.Controls.Add(btnOpen);

            page.Controls.Add(panel);
            return page;
        }

        private TabPage CreateImposingTab()
        {
            var page = new TabPage("Диспетчер Imposing");
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(16),
                WrapContents = false
            };

            panel.Controls.Add(new Label
            {
                Text = "Управление пресетами Imposing.",
                AutoSize = true
            });

            var btnOpen = new Button { Text = "Открыть диспетчер Imposing", AutoSize = true };
            btnOpen.Click += (s, e) => _openImposingManager();
            panel.Controls.Add(btnOpen);

            page.Controls.Add(panel);
            return page;
        }

        private void BrowseFolder(TextBox target)
        {
            using var dialog = new FolderBrowserDialog();
            if (!string.IsNullOrWhiteSpace(target.Text))
                dialog.SelectedPath = target.Text;

            if (dialog.ShowDialog(this) == DialogResult.OK)
                target.Text = dialog.SelectedPath;
        }
    }
}
