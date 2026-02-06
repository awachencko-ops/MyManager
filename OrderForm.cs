using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;


namespace MyManager
{
    public partial class OrderForm : Form
    {
        public OrderData ResultOrder { get; private set; }
        private readonly string ordersRootPath;
        private readonly bool _infoOnly;
        private readonly string _internalId;
        private readonly OrderStartMode _startMode;
        private readonly DateTime _arrivalDate;

        public OrderForm(string ordersRootPath, OrderData data = null, bool infoOnly = false)
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterParent;

            this.ordersRootPath = ordersRootPath;
            _infoOnly = infoOnly;
            _internalId = data?.InternalId ?? Guid.NewGuid().ToString("N");
            _startMode = data?.StartMode ?? OrderStartMode.Extended;
            _arrivalDate = data?.ArrivalDate ?? DateTime.Now;

            LoadConfigLists();
            SetupValidation();

            if (data != null) // РЕДАКТИРОВАНИЕ
            {
                label2.Text = _infoOnly ? "Данные заказа" : "Редактирование заказа";
                textBoxNumberOrder.Text = data.Id;
                textKey.Text = data.Keyword;
                dateTimeOrder.Value = data.OrderDate;
                textFolder.Text = string.IsNullOrWhiteSpace(data.FolderName)
                    ? ""
                    : Path.Combine(ordersRootPath, data.FolderName);
                textOriginal.Text = data.SourcePath;
                textPrepared.Text = data.PreparedPath;
                textPrint.Text = data.PrintPath;
                comboBoxPitStop.Text = data.PitStopAction;
                comboBoxHotImposing.Text = data.ImposingAction;

                ToggleFields(true);
                if (_infoOnly && string.IsNullOrWhiteSpace(textFolder.Text))
                {
                    ToggleFields(false);
                    buttonCreateFolder.Enabled = !string.IsNullOrWhiteSpace(textBoxNumberOrder.Text) &&
                                                 !string.IsNullOrWhiteSpace(textKey.Text);
                }
            }
            else // СОЗДАНИЕ
            {
                label2.Text = "Новый заказ";
                dateTimeOrder.Value = DateTime.Now;
                ToggleFields(false);
                buttonCreateFolder.Enabled = false; // Выключена, пока нет текста
            }

            // Привязываем кнопки выбора файлов
            btnSelectOriginal.Click += (s, e) => textOriginal.Text = ProcessFileSelection(textOriginal.Text, "1. исходные");
            btnSelectPrepared.Click += (s, e) => textPrepared.Text = ProcessFileSelection(textPrepared.Text, "2. подготовка");
            btnSelectPrint.Click += (s, e) => textPrint.Text = ProcessFileSelection(textPrint.Text, "3. печать");
        }

        private void ToggleFields(bool enabled)
        {
            bool allowFileEdits = enabled && !_infoOnly;
            textOriginal.Enabled = allowFileEdits;
            textPrepared.Enabled = allowFileEdits;
            textPrint.Enabled = allowFileEdits;
            btnSelectOriginal.Enabled = allowFileEdits;
            btnSelectPrepared.Enabled = allowFileEdits;
            btnSelectPrint.Enabled = allowFileEdits;
            comboBoxPitStop.Enabled = allowFileEdits;
            comboBoxHotImposing.Enabled = allowFileEdits;
            btnOk.Enabled = enabled;
        }

        private void SetupValidation()
        {
            textBoxNumberOrder.TextChanged += (s, e) => ValidateMainFields();
            textKey.TextChanged += (s, e) => ValidateMainFields();
            textKey.TextChanged += (s, e) => {
                int cursor = textKey.SelectionStart;
                string clean = SanitizeInput(textKey.Text);
                if (textKey.Text != clean)
                {
                    textKey.Text = clean;
                    textKey.SelectionStart = cursor; // Возвращаем курсор на место
                }
                ValidateMainFields();
            };
        }

        private void ValidateMainFields()
        {
            buttonCreateFolder.Enabled = !string.IsNullOrWhiteSpace(textBoxNumberOrder.Text) &&
                                       !string.IsNullOrWhiteSpace(textKey.Text);
        }

        private void buttonCreateFolder_Click(object sender, EventArgs e)
        {
            string datePart = dateTimeOrder.Value.ToString("dd_MM_yy");
            string folderName = $"{datePart} {textKey.Text.Trim()}";
            string fullPath = Path.Combine(ordersRootPath, folderName);

            try
            {
                // Создаем структуру
                Directory.CreateDirectory(fullPath);
                Directory.CreateDirectory(Path.Combine(fullPath, "1. исходные"));
                Directory.CreateDirectory(Path.Combine(fullPath, "2. подготовка"));
                Directory.CreateDirectory(Path.Combine(fullPath, "3. печать"));

                textFolder.Text = fullPath;

                // Прописываем пути по умолчанию, если они пустые
                if (string.IsNullOrEmpty(textOriginal.Text)) textOriginal.Text = Path.Combine(fullPath, "1. исходные");
                if (string.IsNullOrEmpty(textPrepared.Text)) textPrepared.Text = Path.Combine(fullPath, "2. подготовка");
                if (string.IsNullOrEmpty(textPrint.Text)) textPrint.Text = Path.Combine(fullPath, "3. печать");

                ToggleFields(true);
                MessageBox.Show("Папки созданы: " + fullPath);
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
        }

        private string ProcessFileSelection(string currentContent, string subFolder)
        {
            // Путь к папке текущего заказа
            string datePart = dateTimeOrder.Value.ToString("dd_MM_yy");
            string orderFolder = Path.Combine(ordersRootPath, $"{datePart} {textKey.Text.Trim()}", subFolder);

            // Создаем папку, если её нет, чтобы InitialDirectory сработал
            if (!Directory.Exists(orderFolder))
            {
                try { Directory.CreateDirectory(orderFolder); } catch { }
            }

            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "PDF|*.pdf|Все файлы|*.*";

                // Указываем папку заказа как начальную
                ofd.InitialDirectory = orderFolder;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string sourceFile = ofd.FileName;
                    string destFile = Path.Combine(orderFolder, Path.GetFileName(sourceFile));

                    // Если файл уже не там, куда мы хотим его положить — копируем
                    if (Path.GetFullPath(sourceFile) != Path.GetFullPath(destFile))
                    {
                        try
                        {
                            File.Copy(sourceFile, destFile, true);
                            return destFile;
                        }
                        catch (Exception ex) { MessageBox.Show("Ошибка копирования: " + ex.Message); }
                    }
                    return sourceFile;
                }
            }
            return currentContent;
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            string datePart = dateTimeOrder.Value.ToString("dd_MM_yy");
            ResultOrder = new OrderData
            {
                InternalId = _internalId,
                Id = textBoxNumberOrder.Text.Trim(),
                StartMode = _startMode,
                Keyword = textKey.Text.Trim(),
                ArrivalDate = _arrivalDate,
                OrderDate = dateTimeOrder.Value,
                FolderName = $"{datePart} {textKey.Text.Trim()}",
                SourcePath = textOriginal.Text,
                PreparedPath = textPrepared.Text,
                PrintPath = textPrint.Text,
                PitStopAction = comboBoxPitStop.Text,
                ImposingAction = comboBoxHotImposing.Text,
                Status = "📂 В работе"
            };
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e) => this.Close();
        private void LoadConfigLists()
        {
            comboBoxPitStop.Items.Clear();
            comboBoxPitStop.Items.Add("-");
            // Используем наш новый сервис
            var pitActions = ConfigService.GetAllPitStopConfigs();
            foreach (var a in pitActions) comboBoxPitStop.Items.Add(a.Name);

            comboBoxHotImposing.Items.Clear();
            comboBoxHotImposing.Items.Add("-");
            var impActions = ConfigService.GetAllImposingConfigs();
            foreach (var a in impActions) comboBoxHotImposing.Items.Add(a.Name);

            comboBoxPitStop.SelectedIndex = 0;
            comboBoxHotImposing.SelectedIndex = 0;
        }

        private string SanitizeInput(string text)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                text = text.Replace(c.ToString(), "_"); // Заменяем всё плохое на подчеркивание
            }
            return text.Trim();
        }

        private void label2_Click(object sender, EventArgs e) { }
    }
}
