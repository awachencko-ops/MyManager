using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Replica
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
        private readonly TextBox _txtSharedThumbnailCachePath = new TextBox();
        private readonly TextBox _txtFontsFolderPath = new TextBox();
        private readonly ComboBox _cmbOrdersStorageBackend = new ComboBox();
        private readonly TextBox _txtLanPostgreSqlConnectionString = new TextBox();
        private readonly TextBox _txtLanApiBaseUrl = new TextBox();
        private readonly NumericUpDown _numLanPushMinRefreshIntervalMs = new NumericUpDown();
        private readonly NumericUpDown _numLanPushPressureAlertMinEvents = new NumericUpDown();
        private readonly NumericUpDown _numLanPushCoalescedRateAlertThreshold = new NumericUpDown();
        private readonly NumericUpDown _numLanPushThrottledRateAlertThreshold = new NumericUpDown();
        private readonly NumericUpDown _numLanPushPressureAlertCooldownSeconds = new NumericUpDown();
        private readonly NumericUpDown _numLanPushPressureHintActiveWindowSeconds = new NumericUpDown();
        private readonly NumericUpDown _numLanPushPressureStateResetWindowSeconds = new NumericUpDown();
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
        public string SharedThumbnailCachePath => _txtSharedThumbnailCachePath.Text.Trim();
        public string FontsFolderPath => _txtFontsFolderPath.Text.Trim();
        public OrdersStorageMode OrdersStorageBackend
            => (_cmbOrdersStorageBackend.SelectedItem as OrdersStorageBackendOption)?.Mode ?? OrdersStorageMode.FileSystem;
        public string LanPostgreSqlConnectionString => _txtLanPostgreSqlConnectionString.Text.Trim();
        public string LanApiBaseUrl => _txtLanApiBaseUrl.Text.Trim();
        public int LanPushMinRefreshIntervalMs => (int)_numLanPushMinRefreshIntervalMs.Value;
        public int LanPushPressureAlertMinEvents => (int)_numLanPushPressureAlertMinEvents.Value;
        public double LanPushCoalescedRateAlertThreshold => (double)_numLanPushCoalescedRateAlertThreshold.Value;
        public double LanPushThrottledRateAlertThreshold => (double)_numLanPushThrottledRateAlertThreshold.Value;
        public int LanPushPressureAlertCooldownSeconds => (int)_numLanPushPressureAlertCooldownSeconds.Value;
        public int LanPushPressureHintActiveWindowSeconds => (int)_numLanPushPressureHintActiveWindowSeconds.Value;
        public int LanPushPressureStateResetWindowSeconds => (int)_numLanPushPressureStateResetWindowSeconds.Value;
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
            string sharedThumbnailCachePath,
            string fontsFolderPath,
            OrdersStorageMode ordersStorageBackend,
            string lanPostgreSqlConnectionString,
            string lanApiBaseUrl,
            int lanPushMinRefreshIntervalMs,
            int lanPushPressureAlertMinEvents,
            double lanPushCoalescedRateAlertThreshold,
            double lanPushThrottledRateAlertThreshold,
            int lanPushPressureAlertCooldownSeconds,
            int lanPushPressureHintActiveWindowSeconds,
            int lanPushPressureStateResetWindowSeconds,
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
                sharedThumbnailCachePath,
                fontsFolderPath,
                ordersStorageBackend,
                lanPostgreSqlConnectionString,
                lanApiBaseUrl,
                lanPushMinRefreshIntervalMs,
                lanPushPressureAlertMinEvents,
                lanPushCoalescedRateAlertThreshold,
                lanPushThrottledRateAlertThreshold,
                lanPushPressureAlertCooldownSeconds,
                lanPushPressureHintActiveWindowSeconds,
                lanPushPressureStateResetWindowSeconds,
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

                if (OrdersStorageBackend == OrdersStorageMode.LanPostgreSql
                    && string.IsNullOrWhiteSpace(LanPostgreSqlConnectionString))
                {
                    MessageBox.Show(this, "Для режима LAN PostgreSQL укажите строку подключения.", "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _tabs.SelectedIndex = 0;
                    return;
                }

                if (OrdersStorageBackend == OrdersStorageMode.LanPostgreSql
                    && string.IsNullOrWhiteSpace(LanApiBaseUrl))
                {
                    MessageBox.Show(this, "Для режима LAN PostgreSQL укажите LAN API base URL.", "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _tabs.SelectedIndex = 0;
                    return;
                }

                if (LanPushPressureStateResetWindowSeconds < LanPushPressureHintActiveWindowSeconds)
                {
                    MessageBox.Show(
                        this,
                        "Окно сброса push-состояния должно быть не меньше окна подсказки.",
                        "Проверка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
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
            string sharedThumbnailCachePath,
            string fontsFolderPath,
            OrdersStorageMode ordersStorageBackend,
            string lanPostgreSqlConnectionString,
            string lanApiBaseUrl,
            int lanPushMinRefreshIntervalMs,
            int lanPushPressureAlertMinEvents,
            double lanPushCoalescedRateAlertThreshold,
            double lanPushThrottledRateAlertThreshold,
            int lanPushPressureAlertCooldownSeconds,
            int lanPushPressureHintActiveWindowSeconds,
            int lanPushPressureStateResetWindowSeconds,
            int maxParallelism,
            bool useExtendedMode)
        {
            var page = new TabPage("Основное");
            page.AutoScroll = true;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 21,
                Padding = new Padding(18, 18, 18, 0)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            for (int i = 0; i < panel.RowCount; i++)
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            AddRow(panel, 0, "Папка хранения заказов", _txtOrdersRoot, ordersRootPath, true);
            AddRow(panel, 1, "Папка временных файлов", _txtTempRoot, tempRootPath, true);
            AddRow(panel, 2, "Папка архива (Дедушка)", _txtGrandpaRoot, grandpaPath, true);
            AddRowTextOnly(panel, 3, "Подпапка архивации", _txtArchiveDoneSubfolder, archiveDoneSubfolder);
            AddRow(panel, 4, "Файл истории заказов", _txtHistoryFilePath, historyFilePath, false);
            AddRow(panel, 5, "Файл общего лога", _txtManagerLogFilePath, managerLogFilePath, false);
            AddRow(panel, 6, "Папка логов заказов (опц.)", _txtOrderLogsFolderPath, orderLogsFolderPath, true, optional: true);
            AddRow(panel, 7, "Папка шрифтов PDF (опц.)", _txtFontsFolderPath, fontsFolderPath, true, optional: true);
            AddRow(panel, 8, "Общий кэш превью (опц.)", _txtSharedThumbnailCachePath, sharedThumbnailCachePath, true, optional: true);
            AddStorageBackendRow(panel, 9, "Хранилище заказов", _cmbOrdersStorageBackend, ordersStorageBackend);
            AddRowTextOnly(panel, 10, "LAN PostgreSQL connection string", _txtLanPostgreSqlConnectionString, lanPostgreSqlConnectionString, optional: true);
            AddRowTextOnly(panel, 11, "LAN API base URL", _txtLanApiBaseUrl, lanApiBaseUrl, optional: true);
            AddNumericRow(panel, 12, "LAN Push: min refresh (ms)", _numLanPushMinRefreshIntervalMs, lanPushMinRefreshIntervalMs, 0, 10000, "Интервал анти-шторм обновления snapshot.");
            AddNumericRow(panel, 13, "LAN Push: min events alert", _numLanPushPressureAlertMinEvents, lanPushPressureAlertMinEvents, 1, 100000, "Минимум событий для pressure-предупреждения.");
            AddFractionRow(panel, 14, "LAN Push: coalesced threshold", _numLanPushCoalescedRateAlertThreshold, lanPushCoalescedRateAlertThreshold, 0m, 1m, 2, "Порог доли coalesced-событий (0..1).");
            AddFractionRow(panel, 15, "LAN Push: throttled threshold", _numLanPushThrottledRateAlertThreshold, lanPushThrottledRateAlertThreshold, 0m, 1m, 2, "Порог доли throttled-обновлений (0..1).");
            AddNumericRow(panel, 16, "LAN Push: alert cooldown (sec)", _numLanPushPressureAlertCooldownSeconds, lanPushPressureAlertCooldownSeconds, 1, 3600, "Минимальный интервал между предупреждениями.");
            AddNumericRow(panel, 17, "LAN Push: hint window (sec)", _numLanPushPressureHintActiveWindowSeconds, lanPushPressureHintActiveWindowSeconds, 5, 86400, "Окно показа подсказки о высоком потоке.");
            AddNumericRow(panel, 18, "LAN Push: reset window (sec)", _numLanPushPressureStateResetWindowSeconds, lanPushPressureStateResetWindowSeconds, 30, 172800, "Окно авто-сброса pressure-состояния.");

            AddNumericRow(panel, 19, "Параллельных файлов (мульти-заказ)", _numMaxParallelism, maxParallelism);
            AddCheckboxRow(panel, 20, "Форма заказа", _chkUseExtendedMode, useExtendedMode, "Вкл. — расширенная форма; выкл. — простая.");

            var hint = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(22, 8, 22, 8),
                ForeColor = Color.DimGray,
                Text = "Если \"Папка шрифтов PDF\" пустая, используется системная Windows Fonts. Папка логов по умолчанию: ./order-logs рядом с приложением. Для режима LAN PostgreSQL обязательны строка подключения и LAN API base URL. Блок LAN Push позволяет тонко настроить anti-storm/pressure пороги и окна. Путь шрифтов и общий кэш превью полностью применяются после перезапуска приложения."
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

        private void AddStorageBackendRow(TableLayoutPanel panel, int row, string labelText, ComboBox box, OrdersStorageMode value)
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
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Items.Clear();
            box.Items.Add(new OrdersStorageBackendOption(OrdersStorageMode.FileSystem, "Локальный файл (history.json)"));
            box.Items.Add(new OrdersStorageBackendOption(OrdersStorageMode.LanPostgreSql, "LAN PostgreSQL"));

            var selected = box.Items
                .OfType<OrdersStorageBackendOption>()
                .FirstOrDefault(option => option.Mode == value);
            box.SelectedItem = selected ?? box.Items[0];

            var lblHint = new Label
            {
                Text = "Feature-gate хранения заказов",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                AutoSize = false
            };

            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(box, 1, row);
            panel.Controls.Add(lblHint, 2, row);
        }

        private void AddNumericRow(
            TableLayoutPanel panel,
            int row,
            string labelText,
            NumericUpDown box,
            int value,
            int minimum = 0,
            int maximum = 128,
            string hintText = "0 — без ограничений (все файлы сразу)")
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
            box.DecimalPlaces = 0;
            box.Increment = 1;
            box.Minimum = minimum;
            box.Maximum = maximum;
            box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, value));

            var lblHint = new Label
            {
                Text = hintText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                AutoSize = false
            };

            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(box, 1, row);
            panel.Controls.Add(lblHint, 2, row);
        }

        private void AddFractionRow(
            TableLayoutPanel panel,
            int row,
            string labelText,
            NumericUpDown box,
            double value,
            decimal minimum,
            decimal maximum,
            int decimalPlaces,
            string hintText)
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
            box.DecimalPlaces = decimalPlaces;
            box.Increment = decimalPlaces switch
            {
                <= 0 => 1m,
                1 => 0.1m,
                2 => 0.01m,
                _ => 0.001m
            };
            box.Minimum = minimum;
            box.Maximum = maximum;

            var decimalValue = decimal.TryParse(value.ToString(System.Globalization.CultureInfo.InvariantCulture), out var parsed)
                ? parsed
                : minimum;
            if (decimalValue < minimum)
                decimalValue = minimum;
            if (decimalValue > maximum)
                decimalValue = maximum;
            box.Value = decimalValue;

            var lblHint = new Label
            {
                Text = hintText,
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

        private sealed class OrdersStorageBackendOption
        {
            public OrdersStorageBackendOption(OrdersStorageMode mode, string text)
            {
                Mode = mode;
                Text = text;
            }

            public OrdersStorageMode Mode { get; }
            public string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
