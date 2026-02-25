using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyManager
{
    public class SettingsDialogForm : Form
    {
        private readonly TabControl _tabs = new TabControl();
        private readonly TextBox _txtOrdersRoot = new TextBox();
        private readonly TextBox _txtTempRoot = new TextBox();

        private readonly ActionManagerForm _pitStopForm;
        private readonly ImposingManagerForm _imposingForm;

        public string OrdersRootPath => _txtOrdersRoot.Text.Trim();
        public string TempRootPath => _txtTempRoot.Text.Trim();

        public SettingsDialogForm(string ordersRootPath, string tempRootPath)
        {
            Text = "Настройки";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(980, 720);
            Size = new Size(1120, 820);

            _pitStopForm = new ActionManagerForm();
            _pitStopForm.SetEmbeddedMode(true);

            _imposingForm = new ImposingManagerForm();
            _imposingForm.SetEmbeddedMode(true);

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
            _tabs.TabPages.Add(CreateEmbeddedManagerTab("Диспетчер PitStop", _pitStopForm));
            _tabs.TabPages.Add(CreateEmbeddedManagerTab("Диспетчер Imposing", _imposingForm));

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
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(18, 18, 18, 0)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            var lblOrders = new Label
            {
                Text = "Папка хранения",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            var lblTemp = new Label
            {
                Text = "Папка временных файлов",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            _txtOrdersRoot.Dock = DockStyle.Fill;
            _txtOrdersRoot.Margin = new Padding(0, 6, 8, 6);
            _txtOrdersRoot.Text = ordersRootPath;

            _txtTempRoot.Dock = DockStyle.Fill;
            _txtTempRoot.Margin = new Padding(0, 6, 8, 6);
            _txtTempRoot.Text = tempRootPath;

            var btnBrowseOrders = new Button { Text = "Обзор...", Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6) };
            btnBrowseOrders.Click += (s, e) => BrowseFolder(_txtOrdersRoot);

            var btnBrowseTemp = new Button { Text = "Обзор...", Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6) };
            btnBrowseTemp.Click += (s, e) => BrowseFolder(_txtTempRoot);

            panel.Controls.Add(lblOrders, 0, 0);
            panel.Controls.Add(_txtOrdersRoot, 1, 0);
            panel.Controls.Add(btnBrowseOrders, 2, 0);

            panel.Controls.Add(lblTemp, 0, 1);
            panel.Controls.Add(_txtTempRoot, 1, 1);
            panel.Controls.Add(btnBrowseTemp, 2, 1);

            page.Controls.Add(panel);
            return page;
        }

        private TabPage CreateEmbeddedManagerTab(string title, Form managerForm)
        {
            var page = new TabPage(title);
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(8)
            };

            managerForm.TopLevel = false;
            managerForm.FormBorderStyle = FormBorderStyle.None;
            managerForm.Dock = DockStyle.Top;
            managerForm.AutoScroll = true;
            host.Controls.Add(managerForm);
            managerForm.Show();

            page.Controls.Add(host);
            return page;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pitStopForm?.Dispose();
                _imposingForm?.Dispose();
            }

            base.Dispose(disposing);
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
