using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyManager
{
    public partial class Form1 : Form
    {

        private string sourceColumnName = ""; // Добавь эту переменную в начало класса
        private OrderProcessor _processor;
        private readonly OrderGridContextMenu _gridMenu = new OrderGridContextMenu();
        private List<OrderData> _orderHistory = new List<OrderData>();
        private string _ordersRootPath = @"C:\Андрей ПК";
        private readonly string _jsonHistoryFile = "history.json";
        private string _tempRootPath = "";
        private string _grandpaFolder = @"\\NAS\work\Temp\!!!Дедушка";
        private bool _useExtendedMode = true;
        private bool _sortArrivalDescending = true;

        private Rectangle dragBoxFromMouseDown;
        private object itemFromMouseDown;
        private int sourceColumnIndex = -1;
        private int sourceRowIndex = -1;

        private int _hoveredRowIndex = -1;
        private int _ctxRow = -1;
        private int _ctxCol = -1; // Добавлено
        public Form1()
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterScreen; // Добавь это
            var settings = AppSettings.Load();
            _ordersRootPath = settings.OrdersRootPath;
            _grandpaFolder = settings.GrandpaPath;
            _useExtendedMode = settings.UseExtendedMode;
            _sortArrivalDescending = settings.SortArrivalDescending;
            _tempRootPath = string.IsNullOrWhiteSpace(settings.TempFolderPath)
                ? Path.Combine(_ordersRootPath, settings.TempFolderName)
                : settings.TempFolderPath;
            EnsureTempFolders();

            InitializeProcessor();

            PdfSharp.Fonts.GlobalFontSettings.FontResolver = new SimpleFontResolver();
            SetupContextMenuActions();
            LoadHistory();
            ApplyModernDesign();
            EnsureGridStyle();
            FillGrid();

            btnCreateOrder.Click += (s, e) =>
            {
                if (_useExtendedMode)
                    ShowOrderEditor(null);
                else
                    CreateEmptyOrder();
            };
            ButtonSettings.Click += (s, e) => ShowSettingsMenu();

            gridOrders.CellDoubleClick += GridOrders_CellDoubleClick;
            gridOrders.CellContentClick += GridOrders_CellContentClick;
            gridOrders.CellMouseDown += GridOrders_CellMouseDown;
            gridOrders.CellFormatting += GridOrders_CellFormatting;
            gridOrders.CellMouseEnter += GridOrders_CellMouseEnter;
            gridOrders.CellMouseLeave += GridOrders_CellMouseLeave;

            // ВКЛЮЧАЕМ DRAG AND DROP
            gridOrders.AllowDrop = true;
            gridOrders.DragEnter += GridOrders_DragEnter;
            gridOrders.DragDrop += GridOrders_DragDrop;

            gridOrders.MouseDown += GridOrders_MouseDown;
            gridOrders.MouseMove += GridOrders_MouseMove;
            gridOrders.DragOver += GridOrders_DragOver;

            // Подписываемся на клик (если еще не подписаны)
            gridOrders.CellClick += GridOrders_CellClick;

            SetBottomStatus("Готово");
        }

        private void InitializeProcessor()
        {
            _processor = new OrderProcessor(_ordersRootPath);
            _processor.OnStatusChanged += (id, status) =>
            {
                var order = _orderHistory.FirstOrDefault(x => x.Id == id);
                if (order != null) SetOrderStatus(order, status);
            };
            _processor.OnLog += (msg) => SetBottomStatus(msg);
        }

        private void SetupContextMenuActions()
        {
            // --- ОСНОВНЫЕ ДЕЙСТВИЯ ---
            // Передаем stage (0, 1, 2 или 3), чтобы открывать конкретную подпапку
            _gridMenu.OpenFolder = (stage) => {
                var o = GetOrderByRow(_ctxRow);
                if (o != null) OpenOrderStageFolder(o, stage);
            };

            _gridMenu.Delete = () => { var o = GetOrderByRow(_ctxRow); if (o != null) DeleteOrder(o); };

            _gridMenu.Run = async () => { var o = GetOrderByRow(_ctxRow); if (o != null) await RunForOrderAsync(o); };

            // --- УПРАВЛЕНИЕ ФАЙЛАМИ ---
            _gridMenu.PickFile = async (stage, type) => { var o = GetOrderByRow(_ctxRow); if (o != null) await PickAndCopyFileAsync(o, stage, type); };

            _gridMenu.RemoveFile = (stage) => { var o = GetOrderByRow(_ctxRow); if (o != null) RemoveFileFromOrder(o, stage); };

            _gridMenu.CopyToPrepared = () => { var o = GetOrderByRow(_ctxRow); if (o != null) CopySourceToPrepared(o); };

            _gridMenu.CopyToPrint = () => { var o = GetOrderByRow(_ctxRow); if (o != null) CopyPreparedToPrint(o); };

            _gridMenu.CopyToGrandpa = () => { var o = GetOrderByRow(_ctxRow); if (o != null) CopyToGrandpa(o); };

            // --- ПЕРЕИМЕНОВАНИЕ И ВСТАВКА ИЗ БУФЕРА ---
            _gridMenu.RenameFile = (stage) => {
                var o = GetOrderByRow(_ctxRow);
                if (o != null) RenameFileHandler(o, stage);
            };

            _gridMenu.CopyPathToClipboard = (stage) => {
                var o = GetOrderByRow(_ctxRow);
                if (o != null) CopyPathToClipboard(o, stage);
            };

            _gridMenu.PastePathFromClipboard = async (stage) => {
                var o = GetOrderByRow(_ctxRow);
                if (o != null) await PasteFileFromClipboardAsync(o, stage);
            };

            // --- ВОДЯНЫЕ ЗНАКИ ---
            _gridMenu.ApplyWatermark = () => {
                var o = GetOrderByRow(_ctxRow);
                if (o != null) ProcessWatermark(o, false);
            };

            _gridMenu.ApplyWatermarkLeft = () => {
                var o = GetOrderByRow(_ctxRow);
                if (o != null) ProcessWatermark(o, true);
            };

            // --- ДИСПЕТЧЕРЫ ---
            _gridMenu.OpenPitStopMan = OpenPitStopManager;
            _gridMenu.OpenImpMan = OpenImposingManager;
        }

        private string SmartCopy(string sourceFile, OrderData o, int stage, string targetName, bool isInternal)
        {
            // 1. Очищаем входящий путь от кавычек и пробелов (частая причина глюков)
            sourceFile = sourceFile.Trim().Replace("\"", "");

            if (stage == 3 && GetOrderStartMode(o) == OrderStartMode.Simple)
                return CopyToGrandpaFromSource(sourceFile, targetName);

            string folder = GetStageFolder(o, stage);

            // Создаем папку, если её нет
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string destPath = Path.Combine(folder, targetName);

            // 2. ГЛАВНАЯ ПРОВЕРКА: Сравниваем полные канонические пути
            // Если это физически один и тот же файл — просто выходим без вопросов
            if (string.Equals(Path.GetFullPath(sourceFile).TrimEnd('\\'),
                              Path.GetFullPath(destPath).TrimEnd('\\'),
                              StringComparison.OrdinalIgnoreCase))
            {
                return destPath;
            }

            // 3. Если файл реально существует в целевой папке — дубликаты не создаем
            if (File.Exists(destPath))
                return destPath;

            // 4. Само копирование
            File.Copy(sourceFile, destPath, true);
            return destPath;
        }

        // Добавьте этот вспомогательный метод в Form1.cs, чтобы не дублировать логику
        private void ProcessWatermark(OrderData o, bool isVertical)
        {
            try
            {
                if (string.IsNullOrEmpty(o.PrintPath) || !File.Exists(o.PrintPath))
                {
                    MessageBox.Show("Файл печатного спуска не найден!", "Ошибка");
                    return;
                }

                PdfWatermark.Apply(o, isVertical);

                string pos = isVertical ? "слева" : "сверху";
                SetBottomStatus($"✅ Водяной знак ({pos}) нанесен на {GetOrderDisplayId(o)}");
            }
            catch (IOException)
            {
                MessageBox.Show("Файл занят! Закройте PDF в Acrobat или PitStop.", "Ошибка доступа");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        private void GridOrders_MouseDown(object sender, MouseEventArgs e)
        {
            var hit = gridOrders.HitTest(e.X, e.Y);
            if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0)
            {
                sourceRowIndex = hit.RowIndex;
                sourceColumnIndex = hit.ColumnIndex;
                sourceColumnName = gridOrders.Columns[hit.ColumnIndex].Name; // Запоминаем имя

                Size dragSize = SystemInformation.DragSize;
                dragBoxFromMouseDown = new Rectangle(new Point(e.X - (dragSize.Width / 2),
                                                               e.Y - (dragSize.Height / 2)), dragSize);
            }
        }

        private void GridOrders_MouseMove(object sender, MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                // Проверяем, что мы вообще начали движение (вышли за пределы dragBox)
                if (dragBoxFromMouseDown != Rectangle.Empty && !dragBoxFromMouseDown.Contains(e.X, e.Y))
                {
                    // Проверяем, что тянем из правильных колонок (Исходники, Подготовка или Печать)
                    // Индексы колонок: colSource=2, colReady=3, colPrint=6 (согласно твоему коду)
                    if (sourceColumnName == "colSource" || sourceColumnName == "colReady" || sourceColumnName == "colPrint")
                    {
                        var o = GetOrderByRow(sourceRowIndex);
                        if (o == null) return;

                        string filePath = sourceColumnIndex switch
                        {
                            2 => o.SourcePath,
                            3 => o.PreparedPath,
                            6 => o.PrintPath,
                            _ => ""
                        };

                        // Если в ячейке реально есть файл — начинаем перетаскивание
                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        {
                            DataObject dragData = new DataObject();
                            dragData.SetData(DataFormats.FileDrop, new string[] { filePath });
                            dragData.SetData("InternalSourceColumn", sourceColumnIndex);
                            dragData.SetData("InternalSourceRow", sourceRowIndex);

                            // Запускаем процесс!
                            gridOrders.DoDragDrop(dragData, DragDropEffects.Copy | DragDropEffects.Move);
                        }
                    }
                }
            }
        }

        private void ApplyModernDesign()
        {
            gridOrders.EnableHeadersVisualStyles = false;
            gridOrders.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridOrders.MultiSelect = false;
            gridOrders.ShowCellToolTips = false;
            gridOrders.RowTemplate.Height = 45;

            Color hBg = Color.FromArgb(245, 245, 247);
            gridOrders.ColumnHeadersDefaultCellStyle.BackColor = hBg;
            gridOrders.ColumnHeadersDefaultCellStyle.SelectionBackColor = hBg;
            gridOrders.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            gridOrders.DefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 240, 250);
            gridOrders.BackgroundColor = Color.White;
            gridOrders.BorderStyle = BorderStyle.None;
        }

        private void RenameFileHandler(OrderData o, int stage)
        {
            string currentPath = stage switch { 1 => o.SourcePath, 2 => o.PreparedPath, 3 => o.PrintPath, _ => "" };
            if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath)) return;

            string oldName = Path.GetFileNameWithoutExtension(currentPath);
            string extension = Path.GetExtension(currentPath);

            // Вызов диалогового окна (метод ShowInputDialog мы создавали ранее)
            string newName = ShowInputDialog("Переименование", "Введите новое имя:", oldName);

            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

            foreach (char c in Path.GetInvalidFileNameChars()) newName = newName.Replace(c, '_');

            string newPath = Path.Combine(Path.GetDirectoryName(currentPath), newName + extension);

            try
            {
                File.Move(currentPath, newPath);
                UpdateOrderFilePath(o, stage, newPath);
                SaveHistory(); FillGrid();
                SetBottomStatus("✅ Файл переименован");
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
        }
        private string ShowInputDialog(string title, string promptText, string value)
        {
            Form form = new Form();
            Label label = new Label();
            TextBox textBox = new TextBox();
            Button buttonOk = new Button();
            Button buttonCancel = new Button();

            form.Text = title;
            label.Text = promptText;
            textBox.Text = value;

            buttonOk.Text = "ОК";
            buttonCancel.Text = "Отмена";
            buttonOk.DialogResult = DialogResult.OK;
            buttonCancel.DialogResult = DialogResult.Cancel;

            label.SetBounds(20, 20, 350, 20);
            textBox.SetBounds(20, 50, 350, 20);
            buttonOk.SetBounds(210, 90, 75, 30);
            buttonCancel.SetBounds(295, 90, 75, 30);

            form.ClientSize = new Size(390, 140);
            form.Controls.AddRange(new Control[] { label, textBox, buttonOk, buttonCancel });
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterParent;
            form.AcceptButton = buttonOk;
            form.CancelButton = buttonCancel;

            return form.ShowDialog() == DialogResult.OK ? textBox.Text : value;
        }
        private async Task PasteFileFromClipboardAsync(OrderData o, int stage)
        {
            try
            {
                string text = Clipboard.GetText().Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    SetBottomStatus("Буфер обмена пуст");
                    return;
                }

                // Очистка пути: убираем кавычки (бывает в начале и конце)
                string cleanPath = text.Replace("\"", "").Trim();

                if (File.Exists(cleanPath))
                {
                    if (stage == 3 && !await EnsureSimpleOrderInfoForPrintAsync(o))
                    {
                        UpdateOrderFilePath(o, 3, "");
                        SaveHistory();
                        FillGrid();
                        return;
                    }

                    // Копируем файл в структуру заказа
                    string newPath = stage == 3
                        ? CopyPrintFile(
                            o,
                            cleanPath,
                            !string.IsNullOrWhiteSpace(o.Id)
                                ? $"{o.Id}{Path.GetExtension(cleanPath)}"
                                : Path.GetFileName(cleanPath))
                        : CopyIntoStage(o, stage, cleanPath);
                    if (stage == 2)
                        EnsureSourceCopy(o, cleanPath);
                    UpdateOrderFilePath(o, stage, newPath);

                    SaveHistory();
                    FillGrid();
                    SetBottomStatus($"✅ Файл подхвачен: {Path.GetFileName(cleanPath)}");
                }
                else
                {
                    MessageBox.Show($"Файл не найден по указанному пути:\n{cleanPath}", "Ошибка пути");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при вставке пути: " + ex.Message);
            }
        }

        private void CopyPathToClipboard(OrderData order, int stage)
        {
            string? path = stage switch
            {
                1 => order.SourcePath,
                2 => order.PreparedPath,
                3 => order.PrintPath,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SetBottomStatus("Путь к файлу не найден");
                return;
            }

            Clipboard.SetText(path);
            SetBottomStatus("Путь скопирован в буфер");
        }

        private void EnsureGridStyle()
        {
            gridOrders.RowHeadersVisible = false;
            gridOrders.AllowUserToAddRows = false;
            gridOrders.ReadOnly = true;
            gridOrders.AllowUserToResizeRows = false;   
            gridOrders.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            if (gridOrders.Columns.Contains("colSource"))
                gridOrders.Columns["colSource"].Visible = false;
        }

        private void FillGrid()
        {
            if (gridOrders.Columns.Count == 0) return;
            string? selInternalId = gridOrders.CurrentRow?.Tag?.ToString();
            var sorted = _sortArrivalDescending
                ? _orderHistory.OrderByDescending(x => x.ArrivalDate).ToList()
                : _orderHistory.OrderBy(x => x.ArrivalDate).ToList();

            if (gridOrders.Rows.Count != sorted.Count)
            {
                gridOrders.Rows.Clear();
                foreach (var o in sorted)
                {
                    int index = gridOrders.Rows.Add(o.Status, GetOrderDisplayId(o), GetFileName(o.SourcePath), GetFileName(o.PreparedPath), o.PitStopAction, o.ImposingAction, GetFileName(o.PrintPath));
                    gridOrders.Rows[index].Tag = o.InternalId;
                }
            }
            else
            {
                for (int i = 0; i < sorted.Count; i++)
                {
                    var o = sorted[i]; var r = gridOrders.Rows[i];
                    UpdateCell(r, "colState", o.Status); UpdateCell(r, "colId", GetOrderDisplayId(o));
                    UpdateCell(r, "colSource", GetFileName(o.SourcePath)); UpdateCell(r, "colReady", GetFileName(o.PreparedPath));
                    UpdateCell(r, "colPitStop", o.PitStopAction); UpdateCell(r, "colImposing", o.ImposingAction);
                    UpdateCell(r, "colPrint", GetFileName(o.PrintPath));
                    r.Tag = o.InternalId;
                }
            }
            if (!string.IsNullOrEmpty(selInternalId))
                foreach (DataGridViewRow row in gridOrders.Rows) if (row.Tag?.ToString() == selInternalId) { gridOrders.CurrentCell = row.Cells[0]; break; }
        }

        private void OpenOrderStageFolder(OrderData o, int stage)
        {
            try
            {
                string path = stage == 0 ? GetOrderRootFolder(o) : GetStageFolder(o, stage);

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                // ИСПОЛЬЗУЕМ SHELL EXECUTE ВМЕСТО ПРЯМОГО ВЫЗОВА EXPLORER.EXE
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true // Это заставляет Windows использовать программу по умолчанию
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть папку: {ex.Message}");
            }
        }

        private void UpdateCell(DataGridViewRow r, string n, object? v) { if (r.Cells[n].Value?.ToString() != v?.ToString()) r.Cells[n].Value = v; }
        private string GetFileName(string? p) => (string.IsNullOrWhiteSpace(p) || p == "..." || Directory.Exists(p)) ? "..." : Path.GetFileName(p);

        private string GetOrderDisplayId(OrderData order)
            => string.IsNullOrWhiteSpace(order.Id) ? "—" : order.Id;

        private string GetOrderRootFolder(OrderData order)
        {
            return string.IsNullOrWhiteSpace(order.FolderName)
                ? _tempRootPath
                : Path.Combine(_ordersRootPath, order.FolderName);
        }

        private string GetStageFolder(OrderData order, int stage)
        {
            if (stage == 3 && !string.IsNullOrWhiteSpace(order.PrintPath) && File.Exists(order.PrintPath))
                return Path.GetDirectoryName(order.PrintPath) ?? GetTempStageFolder(stage);

            if (string.IsNullOrWhiteSpace(order.FolderName))
                return GetTempStageFolder(stage);

            string sub = stage switch { 1 => "1. исходные", 2 => "2. подготовка", 3 => "3. печать", _ => "" };
            return Path.Combine(_ordersRootPath, order.FolderName, sub);
        }

        private string GetTempStageFolder(int stage)
        {
            string sub = stage switch { 1 => "in", 2 => "prepress", 3 => "print", _ => "" };
            string path = Path.Combine(_tempRootPath, sub);
            Directory.CreateDirectory(path);
            return path;
        }

        private void EnsureTempFolders()
        {
            Directory.CreateDirectory(Path.Combine(_tempRootPath, "in"));
            Directory.CreateDirectory(Path.Combine(_tempRootPath, "prepress"));
            Directory.CreateDirectory(Path.Combine(_tempRootPath, "print"));
        }

        private void DeleteTempFiles(OrderData order)
        {
            foreach (var path in new[] { order.SourcePath, order.PreparedPath, order.PrintPath })
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try { File.Delete(path); } catch { }
                }
            }
        }

        private void GridOrders_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var o = GetOrderByRow(e.RowIndex); if (o == null) return;
            string col = gridOrders.Columns[e.ColumnIndex].Name;

            Color selC = Color.FromArgb(235, 240, 250);
            Color hovC = Color.FromArgb(248, 250, 255);
            Color bg = gridOrders.Rows[e.RowIndex].Selected ? selC : (e.RowIndex == _hoveredRowIndex ? hovC : Color.White);

            if (col == "colState")
            {
                string s = (o.Status ?? "").ToLower(); Color b, f;
                if (s.Contains("ошибка")) { b = Color.FromArgb(255, 210, 210); f = Color.FromArgb(150, 0, 0); }
                else if (!string.IsNullOrEmpty(o.PrintPath) && File.Exists(o.PrintPath)) { b = Color.FromArgb(210, 255, 210); f = Color.FromArgb(0, 100, 0); }
                else { b = Color.FromArgb(255, 235, 200); f = Color.FromArgb(150, 80, 0); }
                e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = b;
                e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = f;
            }
            else if (col == "colSource" || col == "colReady" || col == "colPrint")
            {
                string p = col == "colSource" ? o.SourcePath : (col == "colReady" ? o.PreparedPath : o.PrintPath);
                Color txt = (string.IsNullOrEmpty(p) || p == "...") ? Color.Gray : (File.Exists(p) ? Color.DodgerBlue : Color.Red);
                e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = txt;
                e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = bg;
            }
            else { e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = bg; }
        }

        private void GridOrders_CellMouseEnter(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                gridOrders.Cursor = (e.ColumnIndex == 2 || e.ColumnIndex == 3 || e.ColumnIndex == 6) ? Cursors.Hand : Cursors.Default;
                if (e.RowIndex != _hoveredRowIndex) { int old = _hoveredRowIndex; _hoveredRowIndex = e.RowIndex; if (old >= 0) gridOrders.InvalidateRow(old); gridOrders.InvalidateRow(_hoveredRowIndex); }
            }
        }
        private void GridOrders_CellMouseLeave(object? sender, EventArgs e) { if (_hoveredRowIndex != -1) { int old = _hoveredRowIndex; _hoveredRowIndex = -1; gridOrders.InvalidateRow(old); } gridOrders.Cursor = Cursors.Default; }

        private void GridOrders_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                _ctxRow = e.RowIndex; _ctxCol = e.ColumnIndex;
                gridOrders.CurrentCell = gridOrders.Rows[e.RowIndex].Cells[e.ColumnIndex];
                var order = GetOrderByRow(e.RowIndex);
                bool allowCopyToGrandpa = order == null || ResolveMenuStartMode(order) != OrderStartMode.Simple;
                _gridMenu.Build(gridOrders.Columns[e.ColumnIndex].Name, allowCopyToGrandpa).Show(Cursor.Position);
            }
        }

        private OrderStartMode ResolveMenuStartMode(OrderData order)
        {
            if (order.StartMode == OrderStartMode.Unknown)
                return _useExtendedMode ? OrderStartMode.Extended : OrderStartMode.Simple;

            return order.StartMode;
        }

        private async Task RunForOrderAsync(OrderData order)
        {
            if (!await EnsureOrderInfoAsync(order))
                return;

            using var cts = new CancellationTokenSource();
            await _processor.RunAsync(order, cts.Token);
            SaveHistory(); FillGrid();
        }

        private void ShowOrderEditor(OrderData? existing)
        {
            using var f = new OrderForm(_ordersRootPath, existing);
            if (f.ShowDialog() == DialogResult.OK && f.ResultOrder != null)
            {
                if (existing == null) _orderHistory.Add(f.ResultOrder);
                else { int idx = _orderHistory.FindIndex(x => x.InternalId == existing.InternalId); if (idx >= 0) _orderHistory[idx] = f.ResultOrder; }
                SaveHistory(); FillGrid();
            }
        }

        private void CreateEmptyOrder()
        {
            var order = new OrderData
            {
                Id = "",
                StartMode = _useExtendedMode ? OrderStartMode.Extended : OrderStartMode.Simple,
                Keyword = "",
                ArrivalDate = DateTime.Now,
                OrderDate = DateTime.Now,
                FolderName = "",
                Status = "⚪ Ожидание",
                PitStopAction = "-",
                ImposingAction = "-"
            };

            _orderHistory.Add(order);
            SaveHistory();
            FillGrid();
        }

        private Task<DialogResult> ShowSimpleOrderFormAsync(SimpleOrderForm form)
        {
            var tcs = new TaskCompletionSource<DialogResult>();
            form.FormClosed += (s, e) => tcs.TrySetResult(form.DialogResult);
            form.Show(this);
            return tcs.Task;
        }

        private async Task<bool> EnsureOrderInfoAsync(OrderData order)
        {
            var mode = GetOrderStartMode(order);
            if (mode == OrderStartMode.Extended)
            {
                using var f = new OrderForm(_ordersRootPath, order, infoOnly: true);
                if (f.ShowDialog() != DialogResult.OK || f.ResultOrder == null)
                    return false;

                ApplyOrderInfo(order, f.ResultOrder);
                EnsureOrderFolder(order);
            }
            else
            {
                using var f = new SimpleOrderForm(order);
                if (await ShowSimpleOrderFormAsync(f) != DialogResult.OK)
                    return false;

                order.Id = f.OrderNumber.Trim();
                order.OrderDate = f.OrderDate;
                if (order.ArrivalDate == default)
                    order.ArrivalDate = DateTime.Now;
                order.FolderName = "";
            }

            SaveHistory();
            FillGrid();
            return true;
        }

        private OrderStartMode GetOrderStartMode(OrderData order)
        {
            if (order.StartMode == OrderStartMode.Unknown)
                order.StartMode = InferOrderStartMode(order);
            return order.StartMode;
        }

        private OrderStartMode InferOrderStartMode(OrderData order)
        {
            return string.IsNullOrWhiteSpace(order.FolderName)
                ? OrderStartMode.Simple
                : OrderStartMode.Extended;
        }

        private void ApplyOrderInfo(OrderData target, OrderData source)
        {
            target.Id = source.Id;
            target.Keyword = source.Keyword;
            target.ArrivalDate = source.ArrivalDate;
            target.OrderDate = source.OrderDate;
            target.FolderName = source.FolderName;
        }

        private async Task<bool> EnsureSimpleOrderInfoForPrintAsync(OrderData order)
        {
            using var f = new SimpleOrderForm(order);
            if (await ShowSimpleOrderFormAsync(f) != DialogResult.OK)
                return false;

            order.Id = f.OrderNumber.Trim();
            order.OrderDate = f.OrderDate;
            if (order.ArrivalDate == default)
                order.ArrivalDate = DateTime.Now;
            return true;
        }

        private void EnsureOrderFolder(OrderData order)
        {
            if (string.IsNullOrWhiteSpace(order.FolderName)) return;

            string root = Path.Combine(_ordersRootPath, order.FolderName);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "1. исходные"));
            Directory.CreateDirectory(Path.Combine(root, "2. подготовка"));
            Directory.CreateDirectory(Path.Combine(root, "3. печать"));
        }

        private void RemoveFileFromOrder(OrderData order, int stage)
        {
            string? path = stage == 1 ? order.SourcePath : stage == 2 ? order.PreparedPath : order.PrintPath;

            if (string.IsNullOrEmpty(path)) return;

            if (MessageBox.Show($"Удалить {Path.GetFileName(path)}?", "Удаление", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }

                // Обновляем путь на пустой — сработает наш авто-статус и вернет "В работе" или "Ожидание"
                UpdateOrderFilePath(order, stage, "");

                SaveHistory();
                FillGrid();
            }
        }

        private void UpdateOrderFilePath(OrderData o, int s, string p)
        {
            // 1. Обновляем путь в зависимости от стадии
            if (s == 1) o.SourcePath = p;
            else if (s == 2) o.PreparedPath = p;
            else if (s == 3) o.PrintPath = p;

            // 2. ЛОГИКА АВТО-СТАТУСА
            // Если в колонке "Печать" есть существующий файл
            if (!string.IsNullOrEmpty(o.PrintPath) && File.Exists(o.PrintPath))
            {
                o.Status = "✅ Готово";
            }
            // Если файла в печати нет, но есть в "Подготовке"
            else if (!string.IsNullOrEmpty(o.PreparedPath) && File.Exists(o.PreparedPath))
            {
                // Только если статус не "Ошибка", чтобы не затереть важное уведомление
                if (!o.Status.Contains("Ошибка"))
                    o.Status = "📂 В работе";
            }
            // Если файлов нет (удалили)
            else if (string.IsNullOrEmpty(o.SourcePath))
            {
                o.Status = "⚪ Ожидание";
            }
        }

        private OrderData? GetOrderByRow(int idx)
        {
            if (idx < 0 || idx >= gridOrders.Rows.Count) return null;
            string internalId = gridOrders.Rows[idx].Tag?.ToString() ?? "";
            return _orderHistory.FirstOrDefault(x => x.InternalId == internalId);
        }

        private void OpenOrderFolder(OrderData o) { try { Process.Start("explorer.exe", GetOrderRootFolder(o)); } catch { } }
        private void OpenPitStopManager() { using var f = new ActionManagerForm(); f.ShowDialog(); }
        private void OpenImposingManager() { using var f = new ImposingManagerForm(); f.ShowDialog(); }

        private async void GridOrders_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            var o = GetOrderByRow(e.RowIndex);
            if (o == null) return;

            // Проверяем, что нажали именно на "..."
            var cellValue = gridOrders.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            if (cellValue == "...")
            {
                string col = gridOrders.Columns[e.ColumnIndex].Name;
                if (col == "colSource") await PickAndCopyFileAsync(o, 1, "source");
                else if (col == "colReady") await PickAndCopyFileAsync(o, 2, "prepared");
                else if (col == "colPrint") await PickAndCopyFileAsync(o, 3, "print");
            }
        }

        private void GridOrders_DragEnter(object sender, DragEventArgs e)
        {
            // Проверяем, что перетаскивают именно файлы
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private async void GridOrders_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;

            // Очищаем путь от мусора сразу
            string sourceFile = files[0].Trim().Replace("\"", "");

            Point clientPoint = gridOrders.PointToClient(new Point(e.X, e.Y));
            var hit = gridOrders.HitTest(clientPoint.X, clientPoint.Y);

            if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0)
            {
                var targetOrder = GetOrderByRow(hit.RowIndex);
                if (targetOrder == null) return;

                string colName = gridOrders.Columns[hit.ColumnIndex].Name;
                int targetStage = colName switch { "colSource" => 1, "colReady" => 2, "colPrint" => 3, _ => 0 };
                if (targetStage == 0) return;

                if (targetStage == 3 && !await EnsureSimpleOrderInfoForPrintAsync(targetOrder))
                {
                    UpdateOrderFilePath(targetOrder, 3, "");
                    SaveHistory();
                    FillGrid();
                    return;
                }

                bool isInternal = e.Data.GetDataPresent("InternalSourceColumn");

                string targetName = (targetStage == 3 && !string.IsNullOrWhiteSpace(targetOrder.Id))
                    ? $"{targetOrder.Id}{Path.GetExtension(sourceFile)}"
                    : Path.GetFileName(sourceFile);

                try
                {
                    string newPath = SmartCopy(sourceFile, targetOrder, targetStage, targetName, isInternal);

                    // Если путь обновился — сохраняем
                    if (!string.Equals(Path.GetFullPath(sourceFile), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
                    {
                        if (targetStage == 2)
                            EnsureSourceCopy(targetOrder, sourceFile);
                        UpdateOrderFilePath(targetOrder, targetStage, newPath);
                        SaveHistory();
                        FillGrid();
                        SetBottomStatus("Файл добавлен");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка");
                }
            }
        }

        // Вспомогательный метод для определения стадии по индексу колонки (для очистки при Move)
        private int GetStageByColumnIndex(int index)
        {
            string name = gridOrders.Columns[index].Name;
            return name switch { "colSource" => 1, "colReady" => 2, "colPrint" => 3, _ => 0 };
        }

        private void GridOrders_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                Point clientPoint = gridOrders.PointToClient(new Point(e.X, e.Y));
                var hit = gridOrders.HitTest(clientPoint.X, clientPoint.Y);

                if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0)
                {
                    var targetOrder = GetOrderByRow(hit.RowIndex);
                    string colName = gridOrders.Columns[hit.ColumnIndex].Name;

                    // Проверяем, в какую стадию целимся
                    string existingPath = colName switch
                    {
                        "colSource" => targetOrder.SourcePath,
                        "colReady" => targetOrder.PreparedPath,
                        "colPrint" => targetOrder.PrintPath,
                        _ => ""
                    };

                    string[] draggedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
                    string draggingFile = (draggedFiles != null && draggedFiles.Length > 0) ? draggedFiles[0] : "";

                    // Если файл в ячейке уже ТОТ ЖЕ САМЫЙ — показываем КРЕСТИК
                    if (!string.IsNullOrEmpty(draggingFile) && !string.IsNullOrEmpty(existingPath) &&
                        string.Equals(Path.GetFullPath(draggingFile), Path.GetFullPath(existingPath), StringComparison.OrdinalIgnoreCase))
                    {
                        e.Effect = DragDropEffects.None;
                    }
                    else
                    {
                        // В остальных случаях — всегда Копирование (иконка с плюсиком)
                        e.Effect = DragDropEffects.Copy;
                    }
                }
                else { e.Effect = DragDropEffects.None; }
            }
        }

        private async Task PickAndCopyFileAsync(OrderData o, int s, string t)
        {
            // Определяем целевую подпапку в зависимости от стадии
            string targetFolder = GetStageFolder(o, s);

            // Если папки еще нет (вдруг удалили), создаем её
            if (!Directory.Exists(targetFolder))
                Directory.CreateDirectory(targetFolder);

            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "PDF|*.pdf|Все файлы|*.*";

                // ВОТ ТУТ МАГИЯ: заставляем проводник открыться в папке этого заказа
                ofd.InitialDirectory = targetFolder;

                // Это свойство заставляет диалог восстанавливать папку, если пользователь её сменит
                ofd.RestoreDirectory = false;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        if (s == 3 && !await EnsureSimpleOrderInfoForPrintAsync(o))
                        {
                            UpdateOrderFilePath(o, 3, "");
                            SaveHistory();
                            FillGrid();
                            return;
                        }

                        string newPath = s == 3
                            ? CopyPrintFile(
                                o,
                                ofd.FileName,
                                !string.IsNullOrWhiteSpace(o.Id)
                                    ? $"{o.Id}{Path.GetExtension(ofd.FileName)}"
                                    : Path.GetFileName(ofd.FileName))
                            : CopyIntoStage(
                                o,
                                s,
                                ofd.FileName,
                                s == 3 && !string.IsNullOrWhiteSpace(o.Id)
                                    ? $"{o.Id}{Path.GetExtension(ofd.FileName)}"
                                    : null);
                        if (s == 2)
                            EnsureSourceCopy(o, ofd.FileName);
                        UpdateOrderFilePath(o, s, newPath);
                        SaveHistory();
                        FillGrid();
                        SetBottomStatus("Файл успешно добавлен в заказ");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Ошибка: " + ex.Message);
                    }
                }
            }
        }

        private void CopySourceToPrepared(OrderData o)
        {
            if (string.IsNullOrEmpty(o.SourcePath) || !File.Exists(o.SourcePath)) return;
            using var f = new CopyForm(o.Keyword, Path.GetExtension(o.SourcePath));
            if (f.ShowDialog() == DialogResult.OK) { o.PreparedPath = CopyIntoStage(o, 2, o.SourcePath, f.ResultName); SaveHistory(); FillGrid(); }
        }

        private void CopyPreparedToPrint(OrderData o)
        {
            if (string.IsNullOrEmpty(o.PreparedPath) || !File.Exists(o.PreparedPath)) return;

            string extension = Path.GetExtension(o.PreparedPath);
            string fileName = !string.IsNullOrWhiteSpace(o.Id)
                ? $"{o.Id}{extension}"
                : Path.GetFileName(o.PreparedPath);

            o.PrintPath = CopyIntoStage(o, 3, o.PreparedPath, fileName);
            UpdateOrderFilePath(o, 3, o.PrintPath);
            SaveHistory();
            FillGrid();
        }

        private string CopyIntoStage(OrderData o, int s, string src, string? name = null)
        {
            string path = GetStageFolder(o, s);
            Directory.CreateDirectory(path);

            string dest = Path.Combine(path, name ?? Path.GetFileName(src));

            // Если мы пытаемся скопировать файл сам в себя - ничего не делаем
            if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                return dest;

            try
            {
                if (File.Exists(dest))
                    return dest;
                File.Copy(src, dest, true);
                return dest;
            }
            catch (IOException)
            {
                MessageBox.Show("Ошибка доступа: Файл занят другой программой (Acrobat, PitStop или браузер).\n\nЗакройте файл и попробуйте снова.", "Файл заблокирован");
                throw; // "Пробрасываем" ошибку выше, чтобы не обновлять путь в базе
            }
        }

        private void EnsureSourceCopy(OrderData order, string sourceFile)
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
                return;

            if (!string.IsNullOrEmpty(order.SourcePath) && File.Exists(order.SourcePath))
                return;

            string newPath = CopyIntoStage(order, 1, sourceFile);
            UpdateOrderFilePath(order, 1, newPath);
        }

        private string CopyPrintFile(OrderData order, string sourceFile, string targetName)
        {
            if (GetOrderStartMode(order) == OrderStartMode.Simple)
                return CopyToGrandpaFromSource(sourceFile, targetName);

            return CopyIntoStage(order, 3, sourceFile, targetName);
        }

        private string CopyToGrandpaFromSource(string sourceFile, string targetName)
        {
            Directory.CreateDirectory(_grandpaFolder);
            string destPath = Path.Combine(_grandpaFolder, targetName);

            if (string.Equals(Path.GetFullPath(sourceFile), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            {
                Clipboard.SetText(destPath);
                return destPath;
            }

            if (File.Exists(destPath))
            {
                Clipboard.SetText(destPath);
                return destPath;
            }

            File.Copy(sourceFile, destPath, true);
            Clipboard.SetText(destPath);
            return destPath;
        }

        private void GridOrders_CellContentClick(object? s, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var o = GetOrderByRow(e.RowIndex); if (o == null) return;
            string col = gridOrders.Columns[e.ColumnIndex].Name;
            string? p = col == "colSource" ? o.SourcePath : col == "colReady" ? o.PreparedPath : col == "colPrint" ? o.PrintPath : null;
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) OpenPdfDefault(p);
        }

        private void OpenPdfDefault(string p) { try { Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true }); } catch { } }
        private void DeleteOrder(OrderData o)
        {
            string orderDir = string.IsNullOrWhiteSpace(o.FolderName)
                ? ""
                : Path.Combine(_ordersRootPath, o.FolderName);

            var res = MessageBox.Show(
                $"Заказ №{GetOrderDisplayId(o)}\n\n" +
                "Желаете удалить папку заказа физически с диска?\n\n" +
                "[Да] — Удалить папку со всеми файлами и убрать из списка.\n" +
                "[Нет] — Только убрать из списка (папка останется на диске).\n" +
                "[Отмена] — Ничего не менять.",
                "Удаление заказа",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (res == DialogResult.Cancel) return;

            if (res == DialogResult.Yes)
            {
                try
                {
                    if (!string.IsNullOrEmpty(orderDir) && Directory.Exists(orderDir))
                    {
                        // Удаляем папку со всем содержимым
                        Directory.Delete(orderDir, true);
                    }
                    else
                    {
                        DeleteTempFiles(o);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось удалить папку: {ex.Message}\nВозможно, файл из папки открыт в другой программе.", "Ошибка удаления");
                    return; // Не удаляем из списка, если физическое удаление сорвалось
                }
            }

            _orderHistory.Remove(o);
            SaveHistory();
            FillGrid();
            SetBottomStatus($"Заказ {GetOrderDisplayId(o)} удален");
        }
        private void LoadHistory()
        {
            if (File.Exists(_jsonHistoryFile))
            {
                try
                {
                    _orderHistory = JsonSerializer.Deserialize<List<OrderData>>(File.ReadAllText(_jsonHistoryFile)) ?? new List<OrderData>();
                }
                catch
                {
                    _orderHistory = new List<OrderData>();
                }
            }

            foreach (var order in _orderHistory)
            {
                if (string.IsNullOrWhiteSpace(order.InternalId))
                    order.InternalId = Guid.NewGuid().ToString("N");
                if (order.StartMode == OrderStartMode.Unknown)
                    order.StartMode = InferOrderStartMode(order);
                if (order.ArrivalDate == default)
                    order.ArrivalDate = order.OrderDate != default ? order.OrderDate : DateTime.Now;
            }
        }
        private void SaveHistory() { File.WriteAllText(_jsonHistoryFile, JsonSerializer.Serialize(_orderHistory, new JsonSerializerOptions { WriteIndented = true })); }
        private void SetOrderStatus(OrderData o, string s) { o.Status = s; SaveHistory(); if (InvokeRequired) Invoke(new Action(FillGrid)); else FillGrid(); }
        private void SetBottomStatus(string t) { if (InvokeRequired) Invoke(new Action(() => lblBottomStatus.Text = t)); else lblBottomStatus.Text = t; }

        private string GetSortArrivalMenuText()
        {
            return _sortArrivalDescending
                ? "Сортировка: поступление (сначала новые)"
                : "Сортировка: поступление (сначала старые)";
        }

        private void ShowSettingsMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Папка хранения", null, (s, e) =>
            {
                using var f = new FolderBrowserDialog();
                if (f.ShowDialog() == DialogResult.OK)
                {
                    _ordersRootPath = f.SelectedPath;
                    var settings = AppSettings.Load();
                    settings.OrdersRootPath = _ordersRootPath;
                    settings.Save();
                    _tempRootPath = string.IsNullOrWhiteSpace(settings.TempFolderPath)
                        ? Path.Combine(_ordersRootPath, settings.TempFolderName)
                        : settings.TempFolderPath;
                    EnsureTempFolders();
                    InitializeProcessor();
                    SetBottomStatus("Путь обновлен");
                }
            });
            m.Items.Add("Папка временных файлов", null, (s, e) =>
            {
                using var f = new FolderBrowserDialog();
                if (f.ShowDialog() == DialogResult.OK)
                {
                    _tempRootPath = f.SelectedPath;
                    var settings = AppSettings.Load();
                    settings.TempFolderPath = _tempRootPath;
                    settings.Save();
                    EnsureTempFolders();
                }
            });
            var modeItem = new ToolStripMenuItem("Расширенный режим") { Checked = _useExtendedMode, CheckOnClick = true };
            modeItem.CheckedChanged += (s, e) =>
            {
                _useExtendedMode = modeItem.Checked;
                var settings = AppSettings.Load();
                settings.UseExtendedMode = _useExtendedMode;
                settings.Save();
            };
            m.Items.Add(modeItem);
            var sortArrivalItem = new ToolStripMenuItem(GetSortArrivalMenuText());
            sortArrivalItem.Click += (s, e) =>
            {
                _sortArrivalDescending = !_sortArrivalDescending;
                var settings = AppSettings.Load();
                settings.SortArrivalDescending = _sortArrivalDescending;
                settings.Save();
                sortArrivalItem.Text = GetSortArrivalMenuText();
                FillGrid();
            };
            m.Items.Add(sortArrivalItem);
            m.Items.Add("Диспетчер PitStop", null, (s, e) => OpenPitStopManager());
            m.Items.Add("Диспетчер Imposing", null, (s, e) => OpenImposingManager());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Открыть лог", null, (s, e) => { if (File.Exists("manager.log")) Process.Start(new ProcessStartInfo { FileName = "manager.log", UseShellExecute = true }); });
            m.Show(ButtonSettings, new Point(0, ButtonSettings.Height));
        }

        private void CopyToGrandpa(OrderData o)
        {
            if (string.IsNullOrEmpty(o.PrintPath) || !File.Exists(o.PrintPath)) return;
            Directory.CreateDirectory(_grandpaFolder);
            string d = Path.Combine(_grandpaFolder, Path.GetFileName(o.PrintPath));
            File.Copy(o.PrintPath, d, true); Clipboard.SetText(d); SetBottomStatus("Скопировано в Дедушку");
        }

        private async void GridOrders_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var o = GetOrderByRow(e.RowIndex); if (o == null) return;
            string col = gridOrders.Columns[e.ColumnIndex].Name;
            if (col == "colId")
            {
                if (GetOrderStartMode(o) == OrderStartMode.Extended)
                {
                    ShowOrderEditor(o);
                }
                else
                {
                    using var f = new SimpleOrderForm(o);
                    if (await ShowSimpleOrderFormAsync(f) == DialogResult.OK)
                    {
                        o.Id = f.OrderNumber.Trim();
                        o.OrderDate = f.OrderDate;
                        if (o.ArrivalDate == default)
                            o.ArrivalDate = DateTime.Now;
                        o.FolderName = "";
                        SaveHistory();
                        FillGrid();
                    }
                }
            }
            else if (col == "colPitStop") { using var f = new PitStopSelectForm(o.PitStopAction); if (f.ShowDialog() == DialogResult.OK) { o.PitStopAction = f.SelectedName; SaveHistory(); FillGrid(); } }
            else if (col == "colImposing") { using var f = new ImposingSelectForm(o.ImposingAction); if (f.ShowDialog() == DialogResult.OK) { o.ImposingAction = f.SelectedName; SaveHistory(); FillGrid(); } }
        }
    }
}
