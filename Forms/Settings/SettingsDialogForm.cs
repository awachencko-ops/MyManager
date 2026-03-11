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
        private readonly TextBox _txtGrandpaRoot = new TextBox();
        private readonly TextBox _txtArchiveDoneSubfolder = new TextBox();
        private readonly TextBox _txtHistoryFilePath = new TextBox();
        private readonly TextBox _txtManagerLogFilePath = new TextBox();
        private readonly TextBox _txtOrderLogsFolderPath = new TextBox();
        private readonly NumericUpDown _numMaxParallelism = new NumericUpDown();
        private readonly CheckBox _chkUseExtendedMode = new CheckBox();

        private readonly ActionManagerForm _pitStopForm;
        private readonly ImposingManagerForm _imposingForm;

        public string OrdersRootPath => _txtOrdersRoot.Text.Trim();
        public string TempRootPath => _txtTempRoot.Text.Trim();
        public string GrandpaPath => _txtGrandpaRoot.Text.Trim();
        public string ArchiveDoneSubfolder => _txtArchiveDoneSubfolder.Text.Trim();
        public string HistoryFilePath => _txtHistoryFilePath.Text.Trim();
        public string ManagerLogFilePath => _txtManagerLogFilePath.Text.Trim();
        public string OrderLogsFolderPath => _txtOrderLogsFolderPath.Text.Trim();
        public int MaxParallelism => (int)_numMaxParallelism.Value;
        public bool UseExtendedMode => _chkUseExtendedMode.Checked;

        public SettingsDialogForm(
            string ordersRootPath,
            string tempRootPath,
            string grandpaPath,
            string archiveDoneSubfolder,
            string historyFilePath,
            string managerLogFilePath,
            string orderLogsFolderPath,
            int maxParallelism,
            bool useExtendedMode = false)
        {
            Text = "Настройки";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(1460, 980);
            MinimumSize = Size;
            MaximumSize = Size;

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
            _tabs.TabPages.Add(CreateGeneralTab(
                ordersRootPath,
                tempRootPath,
                grandpaPath,
                archiveDoneSubfolder,
                historyFilePath,
                managerLogFilePath,
                orderLogsFolderPath,
                maxParallelism,
                useExtendedMode));
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
                if (string.IsNullOrWhiteSpace(OrdersRootPath)
                    || string.IsNullOrWhiteSpace(TempRootPath)
                    || string.IsNullOrWhiteSpace(GrandpaPath)
                    || string.IsNullOrWhiteSpace(ArchiveDoneSubfolder)
                    || string.IsNullOrWhiteSpace(HistoryFilePath)
                    || string.IsNullOrWhiteSpace(ManagerLogFilePath))
                {
                    MessageBox.Show(this, "Заполните обязательные пути в разделе 'Основное'.", "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private TabPage CreateGeneralTab(
            string ordersRootPath,
            string tempRootPath,
            string grandpaPath,
            string archiveDoneSubfolder,
            string historyFilePath,
            string managerLogFilePath,
            string orderLogsFolderPath,
            int maxParallelism,
            bool useExtendedMode)
        {
            var page = new TabPage("Основное");

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 9,
                Padding = new Padding(18, 18, 18, 0)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            for (int i = 0; i < 9; i++)
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            AddRow(panel, 0, "Папка хранения заказов", _txtOrdersRoot, ordersRootPath, true);
            AddRow(panel, 1, "Папка временных файлов", _txtTempRoot, tempRootPath, true);
            AddRow(panel, 2, "Папка архива (Дедушка)", _txtGrandpaRoot, grandpaPath, true);
            AddRowTextOnly(panel, 3, "Подпапка архивации", _txtArchiveDoneSubfolder, archiveDoneSubfolder);
            AddRow(panel, 4, "Файл истории заказов", _txtHistoryFilePath, historyFilePath, false);
            AddRow(panel, 5, "Файл общего лога", _txtManagerLogFilePath, managerLogFilePath, false);
            AddRow(panel, 6, "Папка логов заказов (опц.)", _txtOrderLogsFolderPath, orderLogsFolderPath, true, optional: true);

            AddNumericRow(panel, 7, "Параллельных файлов (мульти-заказ)", _numMaxParallelism, maxParallelism);
            AddCheckboxRow(panel, 8, "Форма заказа", _chkUseExtendedMode, useExtendedMode, "Вкл. — расширенная форма; выкл. — простая.");

            var hint = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(22, 8, 22, 8),
                ForeColor = Color.DimGray,
                Text = "Если \"Папка логов заказов\" пустая, будет использоваться ./order-logs рядом с приложением."
            };

            page.Controls.Add(hint);
            page.Controls.Add(panel);
            return page;
        }

        private void AddCheckboxRow(TableLayoutPanel panel, int row, string labelText, CheckBox box, bool value, string hintText)
        {
            var label = new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            box.Text = "Расширенная форма заказа";
            box.Checked = value;
            box.AutoSize = true;
            box.Dock = DockStyle.Left;
            box.Margin = new Padding(0, 10, 8, 6);

            var boxHost = new Panel
            {
                Dock = DockStyle.Fill
            };
            boxHost.Controls.Add(box);

            var lblHint = new Label
            {
                Text = hintText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                AutoSize = false
            };

            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(boxHost, 1, row);
            panel.Controls.Add(lblHint, 2, row);
        }

        private void AddNumericRow(TableLayoutPanel panel, int row, string labelText, NumericUpDown box, int value)
        {
            var label = new Label
            {
                Text = $"{labelText} *",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 6, 8, 6);
            box.Minimum = 0;
            box.Maximum = 128;
            box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, value));

            var lblHint = new Label
            {
                Text = "0 — без ограничений (все файлы сразу)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                AutoSize = false
            };

            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(box, 1, row);
            panel.Controls.Add(lblHint, 2, row);
        }

        private void AddRow(TableLayoutPanel panel, int row, string labelText, TextBox box, string value, bool folderPicker, bool optional = false)
        {
            var label = new Label
            {
                Text = optional ? $"{labelText}" : $"{labelText} *",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 6, 8, 6);
            box.Text = value ?? string.Empty;

            var btnBrowse = new Button { Text = "Обзор...", Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6) };
            btnBrowse.Click += (s, e) =>
            {
                if (folderPicker)
                    BrowseFolder(box);
                else
                    BrowseFile(box);
            };

            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(box, 1, row);
            panel.Controls.Add(btnBrowse, 2, row);
        }


        private void AddRowTextOnly(TableLayoutPanel panel, int row, string labelText, TextBox box, string value, bool optional = false)
        {
            var label = new Label
            {
                Text = optional ? $"{labelText}" : $"{labelText} *",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 6, 8, 6);
            box.Text = value ?? string.Empty;

            var lblHint = new Label
            {
                Text = "(введите вручную)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                AutoSize = false
            };

            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(box, 1, row);
            panel.Controls.Add(lblHint, 2, row);
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

        private void BrowseFile(TextBox target)
        {
            using var dialog = new SaveFileDialog
            {
                FileName = string.IsNullOrWhiteSpace(target.Text) ? string.Empty : target.Text,
                Filter = "JSON/LOG/TXT|*.json;*.log;*.txt|Все файлы|*.*"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
                target.Text = dialog.FileName;
        }
    }
}
