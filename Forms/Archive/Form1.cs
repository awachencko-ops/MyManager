using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Replica
{
    public partial class Form1 : Form
    {

        private string sourceColumnName = ""; // Добавь эту переменную в начало класса
        private OrderProcessor _processor;
        private readonly OrderGridContextMenu _gridMenu = new OrderGridContextMenu();
        private List<OrderData> _orderHistory = new List<OrderData>();
        private string _ordersRootPath = @"C:\Replica\Orders";
        private string _jsonHistoryFile = "history.json";
        private string _tempRootPath = "";
        private string _grandpaFolder = @"C:\Replica\Archive";
        private string _archiveDoneSubfolder = "Готово";
        private string _managerLogFilePath = "manager.log";
        private string _orderLogsFolderPath = "";
        private bool _useExtendedMode = false;
        private bool _sortArrivalDescending = true;
        private readonly Dictionary<string, bool> _fileExistsCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _archivedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _archivedFilePathsByFingerprint = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _orderArchiveStateCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private DateTime _archiveIndexLoadedAt = DateTime.MinValue;
        private static readonly TimeSpan ArchiveIndexLifetime = TimeSpan.FromSeconds(5);

        private Rectangle dragBoxFromMouseDown;
        private int sourceColumnIndex = -1;
        private int sourceRowIndex = -1;

        private int _hoveredRowIndex = -1;
        private int _ctxRow = -1;
        private int _ctxCol = -1; // Добавлено
        private readonly Dictionary<string, bool> _expandedGroups = new Dictionary<string, bool>();
        public Form1()
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterScreen; // Добавь это
            var settings = AppSettings.Load();
            _ordersRootPath = settings.OrdersRootPath;
            _grandpaFolder = settings.GrandpaPath;
            _archiveDoneSubfolder = string.IsNullOrWhiteSpace(settings.ArchiveDoneSubfolder) ? "Готово" : settings.ArchiveDoneSubfolder;
            _jsonHistoryFile = StoragePaths.ResolveExistingFilePath(settings.HistoryFilePath, "history.json");
            _managerLogFilePath = StoragePaths.ResolveFilePath(settings.ManagerLogFilePath, "manager.log");
            _orderLogsFolderPath = string.IsNullOrWhiteSpace(settings.OrderLogsFolderPath)
                ? StoragePaths.ResolveFolderPath(string.Empty, "order-logs")
                : StoragePaths.ResolveFolderPath(settings.OrderLogsFolderPath, "order-logs");
            _useExtendedMode = settings.UseExtendedMode;
            _sortArrivalDescending = settings.SortArrivalDescending;
            _tempRootPath = string.IsNullOrWhiteSpace(settings.TempFolderPath)
                ? Path.Combine(_ordersRootPath, settings.TempFolderName)
                : settings.TempFolderPath;
            Logger.LogFilePath = _managerLogFilePath;
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
            ButtonSettings.Click += (s, e) => ShowSettingsDialog();
            btnExtendedMode.Click += (s, e) => ToggleExtendedMode();
            btnSortArrival.Click += (s, e) => ToggleArrivalSort();
            btnOpenLog.Click += (s, e) => OpenLogFile();
            txtSearch.TextChanged += (s, e) => FillGrid();
            UpdateTopButtons();

            gridOrders.CellDoubleClick += GridOrders_CellDoubleClick;
            gridOrders.CellContentClick += GridOrders_CellContentClick;
            gridOrders.CellMouseDown += GridOrders_CellMouseDown;
            gridOrders.CellFormatting += GridOrders_CellFormatting;
            gridOrders.CellMouseEnter += GridOrders_CellMouseEnter;
            gridOrders.CellMouseLeave += GridOrders_CellMouseLeave;
            gridOrders.CellToolTipTextNeeded += GridOrders_CellToolTipTextNeeded;

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
            _processor.OnStatusChanged += (id, status, reason) =>
            {
                var order = _orderHistory.FirstOrDefault(x => x.Id == id);
                if (order != null) SetOrderStatus(order, status, "processor", reason);
            };
            _processor.OnLog += (msg) => SetBottomStatus(msg);
            _processor.OnCapturedOrderLog += (orderId, message) => AppendCapturedProcessorLog(orderId, message);
        }

        private void SetupContextMenuActions()
        {
            // --- ОСНОВНЫЕ ДЕЙСТВИЯ ---
            // Передаем stage (0, 1, 2 или 3), чтобы открывать конкретную подпапку
            _gridMenu.OpenFolder = (stage) =>
            {
                var o = GetOrderByRow(_ctxRow);
                if (o != null) OpenOrderStageFolder(o, stage);
            };

            _gridMenu.Delete = () => { var o = GetOrderByRow(_ctxRow); if (o != null) DeleteOrder(o); };

            _gridMenu.Run = async () => { var o = GetOrderByRow(_ctxRow); if (o != null) await RunForOrderAsync(o); };

            // --- УПРАВЛЕНИЕ ФАЙЛАМИ ---
            _gridMenu.PickFile = async (stage, type) =>
            {
                if (TryGetItemByRow(_ctxRow, out var itemOrder, out var item) && itemOrder != null && item != null)
                {
                    await PickAndCopyFileForItemAsync(itemOrder, item, stage);
                    return;
                }

                var o = GetOrderByRow(_ctxRow);
                if (o == null) return;
                if (IsVisualGroupOrder(o))
                {
                    SetBottomStatus("Головная строка группы заблокирована для файловых операций");
                    return;
                }

                await PickAndCopyFileAsync(o, stage, type);
            };

            _gridMenu.RemoveFile = (stage) =>
            {
                if (TryGetItemByRow(_ctxRow, out var itemOrder, out var item) && itemOrder != null && item != null)
                {
                    RemoveFileFromItem(itemOrder, item, stage);
                    return;
                }

                var o = GetOrderByRow(_ctxRow);
                if (o == null) return;
                if (IsVisualGroupOrder(o))
                {
                    SetBottomStatus("Головная строка группы заблокирована для файловых операций");
                    return;
                }
                RemoveFileFromOrder(o, stage);
            };

            _gridMenu.CopyToPrepared = () => { var o = GetOrderByRow(_ctxRow); if (o != null) CopySourceToPrepared(o); };

            _gridMenu.CopyToPrint = () => { var o = GetOrderByRow(_ctxRow); if (o != null) CopyPreparedToPrint(o); };

            _gridMenu.CopyToGrandpa = () => { var o = GetOrderByRow(_ctxRow); if (o != null) CopyToGrandpa(o); };

            // --- ПЕРЕИМЕНОВАНИЕ И ВСТАВКА ИЗ БУФЕРА ---
            _gridMenu.RenameFile = (stage) =>
            {
                var o = GetOrderByRow(_ctxRow);
                if (o == null) return;
                if (TryGetItemByRow(_ctxRow, out var itemOrder, out var item) && itemOrder != null && item != null)
                {
                    RenameFileHandler(itemOrder, item, stage);
                    return;
                }
                if (IsVisualGroupOrder(o))
                {
                    SetBottomStatus("Головная строка группы заблокирована для файловых операций");
                    return;
                }
                RenameFileHandler(o, stage);
            };

            _gridMenu.CopyPathToClipboard = (stage) =>
            {
                var o = GetOrderByRow(_ctxRow);
                if (o == null) return;
                if (TryGetItemByRow(_ctxRow, out var itemOrder, out var item) && itemOrder != null && item != null)
                {
                    CopyPathToClipboard(item, stage);
                    return;
                }
                if (IsVisualGroupOrder(o))
                {
                    SetBottomStatus("Головная строка группы заблокирована для файловых операций");
                    return;
                }
                CopyPathToClipboard(o, stage);
            };

            _gridMenu.PastePathFromClipboard = async (stage) =>
            {
                var o = GetOrderByRow(_ctxRow);
                if (o == null) return;
                if (TryGetItemByRow(_ctxRow, out var itemOrder, out var item) && itemOrder != null && item != null)
                {
                    await PasteFileFromClipboardAsync(itemOrder, item, stage);
                    return;
                }
                if (IsVisualGroupOrder(o))
                {
                    SetBottomStatus("Головная строка группы заблокирована для файловых операций");
                    return;
                }
                await PasteFileFromClipboardAsync(o, stage);
            };

            // --- ВОДЯНЫЕ ЗНАКИ ---
            _gridMenu.ApplyWatermark = () =>
            {
                var o = GetOrderByRow(_ctxRow);
                if (o == null) return;
                if (TryGetItemByRow(_ctxRow, out var itemOrder, out var item) && itemOrder != null && item != null)
                {
                    ProcessWatermark(itemOrder, item, false);
                    return;
                }
                if (IsVisualGroupOrder(o))
                {
                    SetBottomStatus("Головная строка группы заблокирована для файловых операций");
                    return;
                }
                ProcessWatermark(o, false);
            };

            _gridMenu.ApplyWatermarkLeft = () =>
            {
                var o = GetOrderByRow(_ctxRow);
                if (o == null) return;
                if (TryGetItemByRow(_ctxRow, out var itemOrder, out var item) && itemOrder != null && item != null)
                {
                    ProcessWatermark(itemOrder, item, true);
                    return;
                }
                if (IsVisualGroupOrder(o))
                {
                    SetBottomStatus("Головная строка группы заблокирована для файловых операций");
                    return;
                }
                ProcessWatermark(o, true);
            };

            // --- ДИСПЕТЧЕРЫ ---
            _gridMenu.OpenPitStopMan = OpenPitStopManager;
            _gridMenu.OpenImpMan = OpenImposingManager;
            _gridMenu.RemovePitStopAction = () =>
            {
                if (TryGetItemByRow(_ctxRow, out var itemOrder, out var item) && itemOrder != null && item != null)
                {
                    RemovePitStopAction(itemOrder, item);
                    return;
                }
                var o = GetOrderByRow(_ctxRow);
                if (o != null) RemovePitStopAction(o);
            };
            _gridMenu.RemoveImposingAction = () =>
            {
                if (TryGetItemByRow(_ctxRow, out var itemOrder, out var item) && itemOrder != null && item != null)
                {
                    RemoveImposingAction(itemOrder, item);
                    return;
                }
                var o = GetOrderByRow(_ctxRow);
                if (o != null) RemoveImposingAction(o);
            };
            _gridMenu.OpenOrderLog = () =>
            {
                var o = GetOrderByRow(_ctxRow);
                if (o != null) OpenOrderLog(o);
            };
        }

        private string SmartCopy(string sourceFile, OrderData o, int stage, string targetName, bool isInternal)
        {
            // 1. Очищаем входящий путь от кавычек и пробелов (частая причина глюков)
            sourceFile = sourceFile.Trim().Replace("\"", "");

            if (stage == 3 && !UsesOrderFolderStorage(o))
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

        private void ProcessWatermark(OrderData order, OrderFileItem item, bool isVertical)
        {
            try
            {
                if (string.IsNullOrEmpty(item.PrintPath) || !File.Exists(item.PrintPath))
                {
                    MessageBox.Show("Файл печатного спуска item не найден!", "Ошибка");
                    return;
                }

                string original = order.PrintPath;
                order.PrintPath = item.PrintPath;
                PdfWatermark.Apply(order, isVertical);
                order.PrintPath = original;

                string pos = isVertical ? "слева" : "сверху";
                SetBottomStatus($"✅ Водяной знак ({pos}) нанесен на {Path.GetFileName(item.PrintPath)}");
                AppendItemOperationLog(order, item, "watermark", $"vertical={isVertical}");
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

        private void RenameFileHandler(OrderData order, OrderFileItem item, int stage)
        {
            string currentPath = GetItemStagePath(item, stage);
            if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath)) return;

            string oldName = Path.GetFileNameWithoutExtension(currentPath);
            string extension = Path.GetExtension(currentPath);
            string newName = ShowInputDialog("Переименование", "Введите новое имя:", oldName);
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
            foreach (char c in Path.GetInvalidFileNameChars()) newName = newName.Replace(c, '_');

            string newPath = Path.Combine(Path.GetDirectoryName(currentPath) ?? string.Empty, newName + extension);
            try
            {
                File.Move(currentPath, newPath);
                UpdateItemFilePath(order, item, stage, newPath);
                SaveHistory(); FillGrid();
                SetBottomStatus("✅ Файл item переименован");
                AppendItemOperationLog(order, item, "rename", Path.GetFileName(newPath));
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
        }

        private async Task PasteFileFromClipboardAsync(OrderData order, OrderFileItem item, int stage)
        {
            try
            {
                string text = Clipboard.GetText().Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    SetBottomStatus("Буфер обмена пуст");
                    return;
                }

                string cleanPath = text.Replace("\"", "").Trim();
                if (!File.Exists(cleanPath))
                {
                    MessageBox.Show($"Файл не найден по указанному пути:\n{cleanPath}", "Ошибка пути");
                    return;
                }

                await AddFileToItemAsync(order, item, cleanPath, stage);
                AppendItemOperationLog(order, item, "paste", cleanPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при вставке пути: " + ex.Message);
            }
        }

        private void CopyPathToClipboard(OrderFileItem item, int stage)
        {
            string path = GetItemStagePath(item, stage);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SetBottomStatus("Путь к файлу item не найден");
                return;
            }

            Clipboard.SetText(path);
            SetBottomStatus("Путь item скопирован в буфер");
        }

        private void RemoveFileFromItem(OrderData order, OrderFileItem item, int stage)
        {
            string path = GetItemStagePath(item, stage);
            if (string.IsNullOrEmpty(path)) return;

            if (MessageBox.Show($"Удалить {Path.GetFileName(path)}?", "Удаление", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }

                UpdateItemFilePath(order, item, stage, "");
                SaveHistory();
                FillGrid();
            }
        }

        private void GridOrders_MouseDown(object sender, MouseEventArgs e)
        {
            var hit = gridOrders.HitTest(e.X, e.Y);
            if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0)
            {
                if (IsGroupOrderRow(hit.RowIndex))
                    return;

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

                        string filePath;
                        if (TryGetItemByRow(sourceRowIndex, out var itemOrder, out var item) && item != null)
                        {
                            filePath = sourceColumnIndex switch
                            {
                                2 => item.SourcePath,
                                3 => item.PreparedPath,
                                6 => item.PrintPath,
                                _ => ""
                            };
                        }
                        else
                        {
                            if (IsVisualGroupOrder(o))
                                return;

                            filePath = sourceColumnIndex switch
                            {
                                2 => o.SourcePath,
                                3 => o.PreparedPath,
                                6 => o.PrintPath,
                                _ => ""
                            };
                        }

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

            // Вызов диалогового окна (метод ShowInputDialog мы создали ранее)
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
            if (NormalizeOrderTopologyInHistory(logIssues: false))
                SaveHistory();

            PrepareGridCaches();
            RefreshArchivedStatuses();
            string? selTag = gridOrders.CurrentRow?.Tag?.ToString();
            var sorted = _sortArrivalDescending
                ? _orderHistory.OrderByDescending(x => x.ArrivalDate).ToList()
                : _orderHistory.OrderBy(x => x.ArrivalDate).ToList();

            string search = (txtSearch?.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(search))
                sorted = sorted.Where(o => OrderMatchesSearch(o, search)).ToList();

            gridOrders.Rows.Clear();

            foreach (var o in sorted)
            {
                bool isGroup = IsVisualGroupOrder(o);
                bool expanded = isGroup && IsGroupExpanded(o.InternalId);
                string statePrefix = isGroup ? (expanded ? "∧ " : "∨ ") : string.Empty;

                string groupSource = isGroup ? "..." : GetFileName(o.SourcePath);
                string groupPrepared = isGroup ? "..." : GetFileName(o.PreparedPath);
                string groupPrint = isGroup ? "..." : GetFileName(o.PrintPath);
                string groupPit = isGroup ? GetCommonGroupAction(o.Items, x => x.PitStopAction) : o.PitStopAction;
                string groupImp = isGroup ? GetCommonGroupAction(o.Items, x => x.ImposingAction) : o.ImposingAction;

                int orderRowIndex = gridOrders.Rows.Add(
                    statePrefix + o.Status,
                    GetOrderDisplayId(o),
                    groupSource,
                    groupPrepared,
                    groupPit,
                    groupImp,
                    groupPrint);
                gridOrders.Rows[orderRowIndex].Tag = $"order|{o.InternalId}";

                if (!expanded)
                    continue;

                var orderedItems = o.Items.OrderBy(x => x.SequenceNo).ToList();
                foreach (var item in orderedItems)
                {
                    int itemRowIndex = gridOrders.Rows.Add(
                        $"   • {item.FileStatus}",
                        $"   └ {GetOrderDisplayId(o)}",
                        GetFileName(item.SourcePath),
                        GetFileName(item.PreparedPath),
                        string.IsNullOrWhiteSpace(item.PitStopAction) ? "-" : item.PitStopAction,
                        string.IsNullOrWhiteSpace(item.ImposingAction) ? "-" : item.ImposingAction,
                        GetFileName(item.PrintPath));
                    gridOrders.Rows[itemRowIndex].Tag = $"item|{o.InternalId}|{item.ItemId}";
                }

            }

            if (!string.IsNullOrEmpty(selTag))
            {
                foreach (DataGridViewRow row in gridOrders.Rows)
                {
                    if (row.Tag?.ToString() == selTag)
                    {
                        gridOrders.CurrentCell = row.Cells[0];
                        break;
                    }
                }
            }
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

        private bool IsVisualGroupOrder(OrderData order)
            => OrderTopologyService.IsMultiFileOrder(order);

        private string GetCommonGroupAction(List<OrderFileItem> items, Func<OrderFileItem, string> selector)
        {
            if (items == null || items.Count == 0)
                return "-";

            var values = items
                .Where(x => x != null)
                .Select(x => string.IsNullOrWhiteSpace(selector(x)) ? "-" : selector(x).Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return values.Count == 1 ? values[0] : "-";
        }

        private bool OrderMatchesSearch(OrderData order, string search)
        {
            if (order == null)
                return false;

            string q = search.Trim();
            if (string.IsNullOrWhiteSpace(q))
                return true;

            bool Contains(string? value)
                => !string.IsNullOrWhiteSpace(value) && value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

            if (Contains(order.Id)
                || Contains(Path.GetFileName(order.SourcePath))
                || Contains(Path.GetFileName(order.PreparedPath))
                || Contains(Path.GetFileName(order.PrintPath)))
            {
                return true;
            }

            if (order.Items == null || order.Items.Count == 0)
                return false;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                if (Contains(item.ClientFileLabel)
                    || Contains(Path.GetFileName(item.SourcePath))
                    || Contains(Path.GetFileName(item.PreparedPath))
                    || Contains(Path.GetFileName(item.PrintPath)))
                {
                    return true;
                }
            }

            return false;
        }

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
            if (IsItemRow(e.RowIndex))
            {
                e.CellStyle.BackColor = Color.FromArgb(248, 248, 248);
                e.CellStyle.SelectionBackColor = Color.FromArgb(235, 240, 250);

                string colName = gridOrders.Columns[e.ColumnIndex].Name;
                if (TryGetItemByRow(e.RowIndex, out _, out var item) && item != null
                    && (colName == "colSource" || colName == "colReady" || colName == "colPrint"))
                {
                    string p = colName == "colSource" ? item.SourcePath : (colName == "colReady" ? item.PreparedPath : item.PrintPath);
                    Color txt = (string.IsNullOrEmpty(p) || p == "...")
                        ? Color.Gray
                        : (FileExistsCached(p) ? Color.DodgerBlue : Color.Red);
                    e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = txt;
                }
                else
                {
                    e.CellStyle.ForeColor = Color.DimGray;
                    e.CellStyle.SelectionForeColor = Color.Black;
                }
                return;
            }

            var o = GetOrderByRow(e.RowIndex); if (o == null) return;
            string col = gridOrders.Columns[e.ColumnIndex].Name;

            Color selC = Color.FromArgb(235, 240, 250);
            Color hovC = Color.FromArgb(248, 250, 255);
            Color bg = gridOrders.Rows[e.RowIndex].Selected ? selC : (e.RowIndex == _hoveredRowIndex ? hovC : Color.White);

            if (col == "colState")
            {
                string s = (o.Status ?? "").ToLower(); Color b, f;
                if (s.Contains("ошибка")) { b = Color.FromArgb(255, 210, 210); f = Color.FromArgb(150, 0, 0); }
                else if (s.Contains("готов")) { b = Color.FromArgb(210, 255, 210); f = Color.FromArgb(0, 100, 0); }
                else if (IsOrderArchivedCached(o)) { b = Color.FromArgb(220, 235, 255); f = Color.FromArgb(0, 70, 140); }
                else if (!string.IsNullOrEmpty(o.PrintPath) && FileExistsCached(o.PrintPath)) { b = Color.FromArgb(210, 255, 210); f = Color.FromArgb(0, 100, 0); }
                else { b = Color.FromArgb(255, 235, 200); f = Color.FromArgb(150, 80, 0); }
                e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = b;
                e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = f;
            }
            else if (col == "colSource" || col == "colReady" || col == "colPrint")
            {
                string p = col == "colSource" ? o.SourcePath : (col == "colReady" ? o.PreparedPath : o.PrintPath);
                bool isArchivedPrint = col == "colPrint" && IsOrderArchivedCached(o);
                Color txt = (string.IsNullOrEmpty(p) || p == "...")
                    ? Color.Gray
                    : (FileExistsCached(p) || isArchivedPrint ? Color.DodgerBlue : Color.Red);
                e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = txt;
                e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = bg;
            }
            else { e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = bg; }
        }


        private void GridOrders_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (IsItemRow(e.RowIndex)) return;
            var o = GetOrderByRow(e.RowIndex);
            if (o == null) return;

            string col = gridOrders.Columns[e.ColumnIndex].Name;
            if (col != "colState") return;

            string status = o.Status ?? string.Empty;
            if (!status.Contains("Ошибка", StringComparison.OrdinalIgnoreCase)) return;

            string reason = string.IsNullOrWhiteSpace(o.LastStatusReason) ? "Причина не указана" : o.LastStatusReason;
            string source = string.IsNullOrWhiteSpace(o.LastStatusSource) ? "неизвестно" : o.LastStatusSource;
            e.ToolTipText = $"{status}\nИсточник: {source}\nПричина: {reason}\nВремя: {o.LastStatusAt:dd.MM.yyyy HH:mm:ss}";
        }

        private void GridOrders_CellMouseEnter(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                bool fileColumn = e.ColumnIndex == 2 || e.ColumnIndex == 3 || e.ColumnIndex == 6;
                gridOrders.Cursor = fileColumn ? Cursors.Hand : Cursors.Default;
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
                bool allowCopyToGrandpa = order == null || UsesOrderFolderStorage(order);
                _gridMenu.Build(gridOrders.Columns[e.ColumnIndex].Name, allowCopyToGrandpa).Show(Cursor.Position);
            }
        }

        private static bool UsesOrderFolderStorage(OrderData order)
        {
            return !string.IsNullOrWhiteSpace(order.FolderName);
        }

        private enum GroupRunMode
        {
            Cancel = 0,
            All = 1,
            SelectedOnly = 2
        }

        private GroupRunMode ShowGroupRunModeDialog()
        {
            using var form = new Form
            {
                Text = "Режим запуска",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(520, 150)
            };

            var lbl = new Label
            {
                Text = "Запустить обработку для выделенного файла или для всех?",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(16, 16, 488, 44)
            };

            var btnAll = new Button { Text = "Да, все", Bounds = new Rectangle(16, 88, 150, 34), DialogResult = DialogResult.Yes };
            var btnSel = new Button { Text = "Только выделенный", Bounds = new Rectangle(182, 88, 170, 34), DialogResult = DialogResult.No };
            var btnCancel = new Button { Text = "Не запускать", Bounds = new Rectangle(368, 88, 136, 34), DialogResult = DialogResult.Cancel };

            form.Controls.Add(lbl);
            form.Controls.Add(btnAll);
            form.Controls.Add(btnSel);
            form.Controls.Add(btnCancel);
            form.AcceptButton = btnAll;
            form.CancelButton = btnCancel;

            var result = form.ShowDialog(this);
            return result switch
            {
                DialogResult.Yes => GroupRunMode.All,
                DialogResult.No => GroupRunMode.SelectedOnly,
                _ => GroupRunMode.Cancel
            };
        }

        private async Task RunForOrderAsync(OrderData order)
        {
            if (!await EnsureOrderInfoAsync(order))
                return;

            List<string>? selectedItemIds = null;
            if (order.Items != null && order.Items.Count > 0)
            {
                var mode = ShowGroupRunModeDialog();
                if (mode == GroupRunMode.Cancel)
                    return;

                if (mode == GroupRunMode.SelectedOnly)
                {
                    selectedItemIds = GetSelectedItemIdsForOrder(order);
                    if (selectedItemIds.Count == 0)
                    {
                        MessageBox.Show("Не выбраны строки item для запуска.", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
            }

            using var cts = new CancellationTokenSource();
            await _processor.RunAsync(order, cts.Token, selectedItemIds);
            SaveHistory(); FillGrid();
        }

        private List<string> GetSelectedItemIdsForOrder(OrderData order)
        {
            var result = new List<string>();
            foreach (DataGridViewRow row in gridOrders.SelectedRows)
            {
                string tag = row.Tag?.ToString() ?? string.Empty;
                if (!tag.StartsWith("item|", StringComparison.Ordinal))
                    continue;

                if (ExtractOrderInternalIdFromTag(tag) != order.InternalId)
                    continue;

                string itemId = ExtractItemIdFromTag(tag);
                if (!string.IsNullOrWhiteSpace(itemId))
                    result.Add(itemId);
            }

            return result;
        }

        private void CreateEmptyItemRow(OrderData order)
        {
            if (order == null)
                return;

            order.Items ??= new List<OrderFileItem>();
            var item = new OrderFileItem
            {
                ClientFileLabel = GetOrderDisplayId(order),
                SequenceNo = order.Items.Count == 0 ? 0 : order.Items.Max(x => x.SequenceNo) + 1,
                FileStatus = "Ожидание",
                PitStopAction = string.IsNullOrWhiteSpace(order.PitStopAction) ? "-" : order.PitStopAction,
                ImposingAction = string.IsNullOrWhiteSpace(order.ImposingAction) ? "-" : order.ImposingAction,
                UpdatedAt = DateTime.Now
            };
            order.Items.Add(item);
            order.RefreshAggregatedStatus();
            SaveHistory();
            FillGrid();
            SetBottomStatus("Добавлена новая строка item");
        }

        private async Task AddItemFromPickerAsync(OrderData order, int stage)
        {
            using var ofd = new OpenFileDialog { Filter = "PDF|*.pdf|Все файлы|*.*" };
            if (ofd.ShowDialog() != DialogResult.OK)
                return;

            order.Items ??= new List<OrderFileItem>();

            string source = ofd.FileName;
            string label = Path.GetFileNameWithoutExtension(source);
            var item = new OrderFileItem
            {
                ClientFileLabel = label,
                SequenceNo = order.Items.Count == 0 ? 0 : order.Items.Max(x => x.SequenceNo) + 1,
                PitStopAction = string.IsNullOrWhiteSpace(order.PitStopAction) ? "-" : order.PitStopAction,
                ImposingAction = string.IsNullOrWhiteSpace(order.ImposingAction) ? "-" : order.ImposingAction
            };

            string ext = Path.GetExtension(source);
            if (stage == 1)
                item.SourcePath = CopyIntoStage(order, 1, source, EnsureUniqueStageFileName(order, 1, label + ext));
            else if (stage == 2)
            {
                item.PreparedPath = CopyIntoStage(order, 2, source, EnsureUniqueStageFileName(order, 2, label + ext));
                if (string.IsNullOrWhiteSpace(item.SourcePath))
                    item.SourcePath = item.PreparedPath;
            }
            else if (stage == 3)
                item.PrintPath = CopyPrintFile(order, source, EnsureUniqueStageFileName(order, 3, label + ext));

            item.FileStatus = stage == 3 ? "✅ Готово" : "Ожидание";
            item.UpdatedAt = DateTime.Now;
            order.Items.Add(item);
            order.RefreshAggregatedStatus();
            SaveHistory();
            FillGrid();
        }

        private string EnsureUniqueStageFileName(OrderData order, int stage, string fileName)
        {
            string folder = GetStageFolder(order, stage);
            Directory.CreateDirectory(folder);
            string ext = Path.GetExtension(fileName);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string candidate = fileName;
            int index = 1;
            while (File.Exists(Path.Combine(folder, candidate)))
            {
                candidate = $"{baseName}_{index}{ext}";
                index++;
            }
            return candidate;
        }

        private string BuildItemPrintFileName(OrderData order, OrderFileItem item, string sourceFile)
        {
            string ext = Path.GetExtension(sourceFile);
            string orderNo = string.IsNullOrWhiteSpace(order.Id) ? "order" : order.Id;
            var ordered = (order.Items ?? new List<OrderFileItem>()).OrderBy(x => x.SequenceNo).ToList();
            int idx = ordered.FindIndex(x => x.ItemId == item.ItemId);
            int itemIndex = idx >= 0 ? idx + 1 : 1;
            return $"{orderNo}_{itemIndex}{ext}";
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
                Status = "Ожидание",
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
                SetOrderStatus(o, "✅ Готово", "file-sync", "Найден печатный файл");
            }
            // Если файла в печати нет, но есть в "Подготовке"
            else if (!string.IsNullOrEmpty(o.PreparedPath) && File.Exists(o.PreparedPath))
            {
                // Только если статус не "Ошибка", чтобы не затереть важное уведомление
                SetOrderStatus(o, "📂 В работе", "file-sync", "Найден файл подготовки");
            }
            // Если файлов нет (удалили)
            else if (string.IsNullOrEmpty(o.SourcePath))
            {
                SetOrderStatus(o, "Ожидание", "file-sync", "Нет исходного файла");
            }
        }

        private OrderData? GetOrderByRow(int idx)
        {
            if (idx < 0 || idx >= gridOrders.Rows.Count)
                return null;

            string tag = gridOrders.Rows[idx].Tag?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            string orderInternalId = ExtractOrderInternalIdFromTag(tag);
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return null;

            return _orderHistory.FirstOrDefault(x => x.InternalId == orderInternalId);
        }

        private bool TryGetItemByRow(int rowIndex, out OrderData? order, out OrderFileItem? item)
        {
            order = GetOrderByRow(rowIndex);
            item = null;
            if (order == null || rowIndex < 0 || rowIndex >= gridOrders.Rows.Count)
                return false;

            string tag = gridOrders.Rows[rowIndex].Tag?.ToString() ?? string.Empty;
            if (!tag.StartsWith("item|", StringComparison.Ordinal))
                return false;

            string itemId = ExtractItemIdFromTag(tag);
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            item = order.Items?.FirstOrDefault(x => x.ItemId == itemId);
            return item != null;
        }

        private bool IsGroupOrderRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= gridOrders.Rows.Count)
                return false;

            if (IsItemRow(rowIndex) || IsDraftRow(rowIndex))
                return false;

            var order = GetOrderByRow(rowIndex);
            return order != null && IsVisualGroupOrder(order);
        }

        private string GetItemStagePath(OrderFileItem item, int stage)
            => stage switch
            {
                1 => item.SourcePath,
                2 => item.PreparedPath,
                3 => item.PrintPath,
                _ => string.Empty
            };

        private void UpdateItemFilePath(OrderData order, OrderFileItem item, int stage, string path)
        {
            if (stage == 1) item.SourcePath = path;
            else if (stage == 2) item.PreparedPath = path;
            else if (stage == 3) item.PrintPath = path;

            if (!string.IsNullOrEmpty(item.PrintPath) && File.Exists(item.PrintPath))
                item.FileStatus = "✅ Готово";
            else if (!string.IsNullOrEmpty(item.PreparedPath) && File.Exists(item.PreparedPath))
                item.FileStatus = "🟡 В работе";
            else if (string.IsNullOrEmpty(item.SourcePath))
                item.FileStatus = "Ожидание";

            item.UpdatedAt = DateTime.Now;
            order.RefreshAggregatedStatus();
        }

        private bool IsItemRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= gridOrders.Rows.Count)
                return false;

            string tag = gridOrders.Rows[rowIndex].Tag?.ToString() ?? string.Empty;
            return tag.StartsWith("item|", StringComparison.Ordinal);
        }

        private bool IsDraftRow(int rowIndex)
        {
            return false;
        }

        private string ExtractItemIdFromTag(string tag)
        {
            var parts = (tag ?? string.Empty).Split('|');
            return parts.Length >= 3 ? parts[2] : string.Empty;
        }

        private string ExtractOrderInternalIdFromTag(string tag)
        {
            if (tag.StartsWith("order|", StringComparison.Ordinal) || tag.StartsWith("item|", StringComparison.Ordinal))
            {
                var parts = tag.Split('|');
                if (parts.Length >= 2)
                    return parts[1];
            }

            return tag;
        }

        private bool IsGroupExpanded(string internalId)
        {
            if (string.IsNullOrWhiteSpace(internalId))
                return false;

            return _expandedGroups.TryGetValue(internalId, out var expanded) && expanded;
        }

        private void ToggleGroupExpanded(OrderData order)
        {
            if (!IsVisualGroupOrder(order))
                return;

            string key = order.InternalId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                return;

            _expandedGroups[key] = !IsGroupExpanded(key);
            FillGrid();
        }

        private void ConvertOrderToGroup(OrderData order)
        {
            if (order == null)
                return;

            order.Items ??= new List<OrderFileItem>();
            if (order.Items.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(order.SourcePath)
                    && string.IsNullOrWhiteSpace(order.PreparedPath)
                    && string.IsNullOrWhiteSpace(order.PrintPath))
                {
                    order.Items.Add(new OrderFileItem
                    {
                        ClientFileLabel = $"{GetOrderDisplayId(order)}_item",
                        SequenceNo = 0,
                        PitStopAction = string.IsNullOrWhiteSpace(order.PitStopAction) ? "-" : order.PitStopAction,
                        ImposingAction = string.IsNullOrWhiteSpace(order.ImposingAction) ? "-" : order.ImposingAction
                    });
                }
                else
                {
                    order.Items.Add(new OrderFileItem
                    {
                        ClientFileLabel = Path.GetFileNameWithoutExtension(order.SourcePath),
                        SourcePath = order.SourcePath ?? string.Empty,
                        PreparedPath = order.PreparedPath ?? string.Empty,
                        PrintPath = order.PrintPath ?? string.Empty,
                        FileStatus = order.Status ?? "Ожидание",
                        SequenceNo = 0,
                        PitStopAction = string.IsNullOrWhiteSpace(order.PitStopAction) ? "-" : order.PitStopAction,
                        ImposingAction = string.IsNullOrWhiteSpace(order.ImposingAction) ? "-" : order.ImposingAction
                    });
                }
            }
            else if (order.Items.Count == 1)
            {
                var firstItem = order.Items[0];
                firstItem.SourcePath = order.SourcePath ?? string.Empty;
                firstItem.PreparedPath = order.PreparedPath ?? string.Empty;
                firstItem.PrintPath = order.PrintPath ?? string.Empty;
                firstItem.FileStatus = order.Status ?? "Ожидание";
                firstItem.PitStopAction = string.IsNullOrWhiteSpace(firstItem.PitStopAction) || firstItem.PitStopAction == "-"
                    ? (string.IsNullOrWhiteSpace(order.PitStopAction) ? "-" : order.PitStopAction)
                    : firstItem.PitStopAction;
                firstItem.ImposingAction = string.IsNullOrWhiteSpace(firstItem.ImposingAction) || firstItem.ImposingAction == "-"
                    ? (string.IsNullOrWhiteSpace(order.ImposingAction) ? "-" : order.ImposingAction)
                    : firstItem.ImposingAction;
                firstItem.UpdatedAt = DateTime.Now;
            }

            _expandedGroups[order.InternalId] = true;
            order.RefreshAggregatedStatus();
            SaveHistory();
            FillGrid();
            SetBottomStatus($"Заказ {GetOrderDisplayId(order)} преобразован в группу");
        }

        private void ConvertGroupToSingle(OrderData order)
        {
            if (order?.Items == null || order.Items.Count != 1)
            {
                SetBottomStatus("Преобразование в одиночный заказ доступно только для группы с одним файлом");
                return;
            }

            var item = order.Items.OrderBy(x => x.SequenceNo).First();
            order.SourcePath = item.SourcePath ?? string.Empty;
            order.PreparedPath = item.PreparedPath ?? string.Empty;
            order.PrintPath = item.PrintPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(item.PitStopAction) && item.PitStopAction != "-")
                order.PitStopAction = item.PitStopAction;
            if (!string.IsNullOrWhiteSpace(item.ImposingAction) && item.ImposingAction != "-")
                order.ImposingAction = item.ImposingAction;

            order.Status = item.FileStatus;
            order.Items.Clear();
            _expandedGroups[order.InternalId] = false;
            SaveHistory();
            FillGrid();
            SetBottomStatus($"Группа {GetOrderDisplayId(order)} преобразована в одиночный заказ");
        }

        private void OpenOrderFolder(OrderData o) { try { Process.Start("explorer.exe", GetOrderRootFolder(o)); } catch { } }
        private void OpenPitStopManager() { using var f = new ActionManagerForm(); f.ShowDialog(); }
        private void OpenImposingManager() { using var f = new ImposingManagerForm(); f.ShowDialog(); }

        private void RemovePitStopAction(OrderData o)
        {
            o.PitStopAction = "-";
            if (o.Items != null)
            {
                foreach (var item in o.Items)
                    item.PitStopAction = "-";
            }
            SaveHistory();
            FillGrid();
            SetBottomStatus($"✅ Секвенция PitStop удалена из {GetOrderDisplayId(o)}");
        }

        private void RemoveImposingAction(OrderData o)
        {
            o.ImposingAction = "-";
            if (o.Items != null)
            {
                foreach (var item in o.Items)
                    item.ImposingAction = "-";
            }
            SaveHistory();
            FillGrid();
            SetBottomStatus($"✅ Секвенция Imposing удалена из {GetOrderDisplayId(o)}");
        }

        private void RemovePitStopAction(OrderData order, OrderFileItem item)
        {
            item.PitStopAction = "-";
            SaveHistory();
            FillGrid();
            SetBottomStatus($"✅ PitStop очищен для item {item.ClientFileLabel}");
        }

        private void RemoveImposingAction(OrderData order, OrderFileItem item)
        {
            item.ImposingAction = "-";
            SaveHistory();
            FillGrid();
            SetBottomStatus($"✅ Imposing очищен для item {item.ClientFileLabel}");
        }

        private async void GridOrders_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            var o = GetOrderByRow(e.RowIndex);
            if (o == null) return;

            if (IsItemRow(e.RowIndex))
            {
                if (!TryGetItemByRow(e.RowIndex, out var itemOrder, out var item) || itemOrder == null || item == null)
                    return;

                var itemCellValue = gridOrders.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
                if (itemCellValue == "...")
                {
                    string itemCol = gridOrders.Columns[e.ColumnIndex].Name;
                    if (itemCol == "colSource") await PickAndCopyFileForItemAsync(itemOrder, item, 1);
                    else if (itemCol == "colReady") await PickAndCopyFileForItemAsync(itemOrder, item, 2);
                    else if (itemCol == "colPrint") await PickAndCopyFileForItemAsync(itemOrder, item, 3);
                }
                return;
            }

            if (gridOrders.Columns[e.ColumnIndex].Name == "colState")
            {
                ToggleGroupExpanded(o);
                return;
            }

            bool isGroup = IsVisualGroupOrder(o);
            string colName = gridOrders.Columns[e.ColumnIndex].Name;
            if (isGroup && (colName == "colSource" || colName == "colReady" || colName == "colPrint"))
                return;

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

                if (IsItemRow(hit.RowIndex))
                {
                    if (!TryGetItemByRow(hit.RowIndex, out var itemOrder, out var item) || itemOrder == null || item == null)
                        return;
                    await AddFileToItemAsync(itemOrder, item, sourceFile, targetStage);
                    return;
                }

                if (IsVisualGroupOrder(targetOrder))
                    return;

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

        private async Task AddFileToItemAsync(OrderData order, OrderFileItem item, string sourceFile, int stage)
        {
            try
            {
                if (stage == 3 && !await EnsureSimpleOrderInfoForPrintAsync(order))
                    return;

                string sourceLabel = Path.GetFileNameWithoutExtension(sourceFile);
                string label = string.IsNullOrWhiteSpace(item.ClientFileLabel)
                    ? sourceLabel
                    : item.ClientFileLabel;
                if (label == "—" || label == GetOrderDisplayId(order) || label.StartsWith("item_", StringComparison.OrdinalIgnoreCase))
                    label = sourceLabel;
                item.ClientFileLabel = label;
                string targetName = EnsureUniqueStageFileName(order, stage, Path.GetFileName(sourceFile));

                string newPath = stage == 3
                    ? CopyPrintFile(order, sourceFile, EnsureUniqueStageFileName(order, 3, BuildItemPrintFileName(order, item, sourceFile)))
                    : CopyIntoStage(order, stage, sourceFile, targetName);

                UpdateItemFilePath(order, item, stage, newPath);
                SaveHistory();
                FillGrid();
                SetBottomStatus("Файл добавлен в item");
                AppendItemOperationLog(order, item, "add-file", Path.GetFileName(newPath));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка");
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
                    if (targetOrder == null) { e.Effect = DragDropEffects.None; return; }
                    string colName = gridOrders.Columns[hit.ColumnIndex].Name;
                    int stage = colName switch { "colSource" => 1, "colReady" => 2, "colPrint" => 3, _ => 0 };
                    if (stage == 0) { e.Effect = DragDropEffects.None; return; }

                    if (IsGroupOrderRow(hit.RowIndex)) { e.Effect = DragDropEffects.None; return; }

                    string existingPath = string.Empty;
                    if (IsItemRow(hit.RowIndex) && TryGetItemByRow(hit.RowIndex, out var itemOrder, out var item) && item != null)
                        existingPath = GetItemStagePath(item, stage);
                    else
                        existingPath = colName switch
                        {
                            "colSource" => targetOrder.SourcePath,
                            "colReady" => targetOrder.PreparedPath,
                            "colPrint" => targetOrder.PrintPath,
                            _ => ""
                        };

                    string draggingFile = (e.Data.GetData(DataFormats.FileDrop) as string[])?.FirstOrDefault() ?? "";
                    draggingFile = draggingFile.Trim().Replace("\"", "");

                    // Если перетаскиваемый файл уже существует в ячейке — показываем курсор "запрещено"
                    if (!string.IsNullOrEmpty(existingPath) && string.Equals(Path.GetFullPath(existingPath), Path.GetFullPath(draggingFile), StringComparison.OrdinalIgnoreCase))
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

        private async Task PickAndCopyFileForItemAsync(OrderData order, OrderFileItem item, int stage)
        {
            string targetFolder = GetStageFolder(order, stage);
            if (!Directory.Exists(targetFolder))
                Directory.CreateDirectory(targetFolder);

            using var ofd = new OpenFileDialog
            {
                Filter = "PDF|*.pdf|Все файлы|*.*",
                InitialDirectory = targetFolder,
                RestoreDirectory = false
            };

            if (ofd.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                if (stage == 3 && !await EnsureSimpleOrderInfoForPrintAsync(order))
                    return;

                string sourceLabel = Path.GetFileNameWithoutExtension(ofd.FileName);
                string label = string.IsNullOrWhiteSpace(item.ClientFileLabel)
                    ? sourceLabel
                    : item.ClientFileLabel;
                if (label == "—" || label == GetOrderDisplayId(order) || label.StartsWith("item_", StringComparison.OrdinalIgnoreCase))
                    label = sourceLabel;
                item.ClientFileLabel = label;
                string targetName = EnsureUniqueStageFileName(order, stage, Path.GetFileName(ofd.FileName));

                string newPath = stage == 3
                    ? CopyPrintFile(order, ofd.FileName, EnsureUniqueStageFileName(order, 3, BuildItemPrintFileName(order, item, ofd.FileName)))
                    : CopyIntoStage(order, stage, ofd.FileName, targetName);

                if (stage == 2 && string.IsNullOrWhiteSpace(item.SourcePath))
                    item.SourcePath = newPath;

                UpdateItemFilePath(order, item, stage, newPath);
                SaveHistory();
                FillGrid();
                SetBottomStatus("Файл успешно добавлен в item");
                AppendItemOperationLog(order, item, "pick-file", Path.GetFileName(newPath));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message);
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
            if (!UsesOrderFolderStorage(order))
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

            string col = gridOrders.Columns[e.ColumnIndex].Name;
            if (IsItemRow(e.RowIndex))
            {
                if (!TryGetItemByRow(e.RowIndex, out _, out var item) || item == null)
                    return;

                string p = col == "colSource" ? item.SourcePath : col == "colReady" ? item.PreparedPath : col == "colPrint" ? item.PrintPath : string.Empty;
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    OpenPdfDefault(p);
                return;
            }

            var o = GetOrderByRow(e.RowIndex); if (o == null) return;
            string? pOrder = col == "colSource" ? o.SourcePath : col == "colReady" ? o.PreparedPath : col == "colPrint" ? o.PrintPath : null;
            if (!string.IsNullOrEmpty(pOrder) && File.Exists(pOrder)) OpenPdfDefault(pOrder);
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

            if (NormalizeOrderTopologyInHistory(logIssues: true))
                SaveHistory();
        }

        private void SaveHistory()
        {
            NormalizeOrderTopologyInHistory(logIssues: false);

            string? dir = Path.GetDirectoryName(_jsonHistoryFile);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_jsonHistoryFile, JsonSerializer.Serialize(_orderHistory, new JsonSerializerOptions { WriteIndented = true }));
        }

        private bool NormalizeOrderTopologyInHistory(bool logIssues)
        {
            if (_orderHistory == null || _orderHistory.Count == 0)
                return false;

            var changed = false;
            foreach (var order in _orderHistory)
            {
                var result = OrderTopologyService.Normalize(order);
                if (result.Changed)
                    changed = true;

                if (!logIssues || result.Issues.Count == 0)
                    continue;

                foreach (var issue in result.Issues)
                    Logger.Warn($"TOPOLOGY | order={GetOrderDisplayId(order)} | {issue}");
            }

            return changed;
        }
        private bool SetOrderStatus(OrderData o, string s, string source = "manual", string reason = "", bool refreshGrid = true, bool persistHistory = true)
        {
            if (o == null)
                return false;

            string old = o.Status ?? string.Empty;
            if (string.Equals(old, s, StringComparison.Ordinal)
                && string.Equals(o.LastStatusSource ?? string.Empty, source ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(o.LastStatusReason ?? string.Empty, reason ?? string.Empty, StringComparison.Ordinal))
                return false;

            o.Status = s;
            o.LastStatusSource = source;
            o.LastStatusReason = reason;
            o.LastStatusAt = DateTime.Now;

            AppendOrderStatusLog(o, old, s, source, reason);

            if (persistHistory)
                SaveHistory();
            if (!refreshGrid)
                return true;

            if (InvokeRequired) Invoke(new Action(FillGrid)); else FillGrid();
            return true;
        }
        private void SetBottomStatus(string t) { if (InvokeRequired) Invoke(new Action(() => lblBottomStatus.Text = t)); else lblBottomStatus.Text = t; }

        private void RefreshArchivedStatuses()
        {
            RefreshArchiveIndexIfNeeded();
            _orderArchiveStateCache.Clear();

            bool changed = false;
            foreach (var order in _orderHistory)
            {
                bool archived = IsOrderInArchive(order);
                _orderArchiveStateCache[GetOrderCacheKey(order)] = archived;

                if ((order.Status ?? string.Empty).Contains("Ошибка", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (archived)
                {
                    changed |= SetOrderStatus(order, WorkflowStatusNames.Archived, "archive-sync", "Файл найден в папке Готово (совпадение по содержимому)", refreshGrid: false, persistHistory: false);
                }
                else if (string.Equals(order.Status, WorkflowStatusNames.Archived, StringComparison.Ordinal))
                {
                    string nextStatus = (!string.IsNullOrWhiteSpace(order.PrintPath) && FileExistsCached(order.PrintPath))
                        ? WorkflowStatusNames.Archived
                        : "Ожидание";
                    changed |= SetOrderStatus(order, nextStatus, "archive-sync", "Заказ больше не считается архивным", refreshGrid: false, persistHistory: false);
                }
            }

            if (changed)
                SaveHistory();
        }


        private string GetOrderLogFilePath(OrderData order)
        {
            string safeId = string.IsNullOrWhiteSpace(order.InternalId) ? order.Id : order.InternalId;
            if (string.IsNullOrWhiteSpace(safeId))
                safeId = "unknown-order";

            foreach (char c in Path.GetInvalidFileNameChars())
                safeId = safeId.Replace(c, '_');

            string logFolder = StoragePaths.ResolveFolderPath(_orderLogsFolderPath, "order-logs");
            Directory.CreateDirectory(logFolder);
            return Path.Combine(logFolder, $"{safeId}.log");
        }

        private void AppendOrderStatusLog(OrderData order, string oldStatus, string newStatus, string source, string reason)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | status: {oldStatus} -> {newStatus} | source: {source} | reason: {reason}";
                File.AppendAllText(GetOrderLogFilePath(order), line + Environment.NewLine);
                Logger.Info($"ORDER-STATUS | order={GetOrderDisplayId(order)} | {line}");
            }
            catch
            {
            }
        }

        private void AppendCapturedProcessorLog(string orderId, string message)
        {
            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(message))
                return;

            var order = _orderHistory.FirstOrDefault(x => string.Equals(x.Id, orderId, StringComparison.Ordinal));
            if (order == null)
                return;

            try
            {
                var trimmed = message.Trim();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {trimmed}";
                File.AppendAllText(GetOrderLogFilePath(order), line + Environment.NewLine);
                Logger.Info($"ORDER-CAPTURED | order={GetOrderDisplayId(order)} | {trimmed}");
            }
            catch
            {
            }
        }

        private void AppendItemOperationLog(OrderData order, OrderFileItem item, string operation, string details = "")
        {
            try
            {
                string safeItemId = string.IsNullOrWhiteSpace(item.ItemId) ? "unknown-item" : item.ItemId;
                foreach (char c in Path.GetInvalidFileNameChars())
                    safeItemId = safeItemId.Replace(c, '_');

                string logFolder = StoragePaths.ResolveFolderPath(_orderLogsFolderPath, "order-logs");
                Directory.CreateDirectory(logFolder);

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | item={item.ClientFileLabel} | op={operation} | {details}";
                File.AppendAllText(Path.Combine(logFolder, $"{order.InternalId}_{safeItemId}.log"), line + Environment.NewLine);
                File.AppendAllText(GetOrderLogFilePath(order), line + Environment.NewLine);
            }
            catch { }
        }

        private bool IsOrderInArchive(OrderData order)
        {
            if (string.IsNullOrWhiteSpace(_grandpaFolder))
                return false;

            var fingerprints = GetOrderArchiveFingerprints(order);
            if (fingerprints.Count == 0)
                return false;

            RefreshArchiveIndexIfNeeded();
            foreach (var fingerprint in fingerprints)
            {
                if (_archivedFilePathsByFingerprint.ContainsKey(fingerprint))
                    return true;
            }

            return false;
        }

        private bool IsOrderArchivedCached(OrderData order)
        {
            string key = GetOrderCacheKey(order);
            if (_orderArchiveStateCache.TryGetValue(key, out bool archived))
                return archived;

            archived = IsOrderInArchive(order);
            _orderArchiveStateCache[key] = archived;
            return archived;
        }

        private string GetOrderCacheKey(OrderData order)
        {
            return !string.IsNullOrWhiteSpace(order.InternalId)
                ? order.InternalId
                : (!string.IsNullOrWhiteSpace(order.Id) ? order.Id : string.Empty);
        }

        private void PrepareGridCaches()
        {
            _fileExistsCache.Clear();
            _orderArchiveStateCache.Clear();
        }

        private bool FileExistsCached(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || path == "...")
                return false;

            if (_fileExistsCache.TryGetValue(path, out bool exists))
                return exists;

            exists = File.Exists(path);
            _fileExistsCache[path] = exists;
            return exists;
        }

        private void RefreshArchiveIndexIfNeeded(bool force = false)
        {
            if (!force && DateTime.UtcNow - _archiveIndexLoadedAt < ArchiveIndexLifetime)
                return;

            _archivedFileNames.Clear();
            _archivedFilePathsByFingerprint.Clear();
            _archiveIndexLoadedAt = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(_grandpaFolder))
                return;

            string archivedFolder = Path.Combine(_grandpaFolder, _archiveDoneSubfolder);
            if (!Directory.Exists(archivedFolder))
                return;

            try
            {
                foreach (string filePath in Directory.EnumerateFiles(archivedFolder, "*", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(filePath);
                    if (!string.IsNullOrWhiteSpace(fileName))
                        _archivedFileNames.Add(fileName);

                    if (!TryGetFileFingerprint(filePath, out var fingerprint))
                        continue;

                    if (!_archivedFilePathsByFingerprint.ContainsKey(fingerprint))
                        _archivedFilePathsByFingerprint[fingerprint] = filePath;
                }
            }
            catch
            {
                // Игнорируем ошибки чтения архива, чтобы не блокировать отрисовку таблицы.
            }
        }

        private static HashSet<string> GetOrderArchiveFingerprints(OrderData order)
        {
            var fingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddFileFingerprint(fingerprints, order?.PrintPath);

            if (order?.Items == null || order.Items.Count == 0)
                return fingerprints;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                AddFileFingerprint(fingerprints, item.PrintPath);
            }

            return fingerprints;
        }

        private static void AddFileFingerprint(HashSet<string> fingerprints, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            if (TryGetFileFingerprint(path, out var fingerprint))
                fingerprints.Add(fingerprint);
        }

        private static bool TryGetFileFingerprint(string path, out string fingerprint)
        {
            fingerprint = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sha256 = SHA256.Create();
                fingerprint = Convert.ToHexString(sha256.ComputeHash(stream));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string GetSortArrivalMenuText()
        {
            return _sortArrivalDescending
                ? "Сортировка: поступление (сначала новые)"
                : "Сортировка: поступление (сначала старые)";
        }

        private void UpdateTopButtons()
        {
            btnExtendedMode.Text = _useExtendedMode ? "Режим: Расширенный" : "Режим: Обычный";
            btnSortArrival.Text = _sortArrivalDescending ? "Поступление: новые сверху" : "Поступление: старые сверху";
        }

        private void ToggleExtendedMode()
        {
            _useExtendedMode = !_useExtendedMode;
            var settings = AppSettings.Load();
            settings.UseExtendedMode = _useExtendedMode;
            settings.Save();
            UpdateTopButtons();
        }

        private void ToggleArrivalSort()
        {
            _sortArrivalDescending = !_sortArrivalDescending;
            var settings = AppSettings.Load();
            settings.SortArrivalDescending = _sortArrivalDescending;
            settings.Save();
            UpdateTopButtons();
            FillGrid();
        }


        private void OpenOrderLog(OrderData order)
        {
            string path = GetOrderLogFilePath(order);
            if (!File.Exists(path))
            {
                SetBottomStatus("Лог заказа пока не создан");
                return;
            }

            using var viewer = new OrderLogViewerForm(path, GetOrderDisplayId(order));
            viewer.ShowDialog(this);
        }

        private void OpenLogFile()
        {
            if (!File.Exists(_managerLogFilePath))
            {
                SetBottomStatus("Лог пока не создан");
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = _managerLogFilePath, UseShellExecute = true });
        }

        private void ShowSettingsDialog()
        {
            var currentSettings = AppSettings.Load();
            using var settingsForm = new SettingsDialogForm(
                _ordersRootPath,
                _tempRootPath,
                _grandpaFolder,
                _archiveDoneSubfolder,
                _jsonHistoryFile,
                _managerLogFilePath,
                _orderLogsFolderPath,
                currentSettings.MaxParallelism,
                useExtendedMode: currentSettings.UseExtendedMode);
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
            settings.UseExtendedMode = settingsForm.UseExtendedMode;
            settings.Save();
            Logger.LogFilePath = _managerLogFilePath;
            _useExtendedMode = settings.UseExtendedMode;

            EnsureTempFolders();
            InitializeProcessor();
            UpdateTopButtons();
            SetBottomStatus("Настройки сохранены");
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

            if (IsItemRow(e.RowIndex))
            {
                if (!TryGetItemByRow(e.RowIndex, out var itemOrder, out var item) || itemOrder == null || item == null)
                    return;

                if (col == "colPitStop")
                {
                    using var ps = new PitStopSelectForm(item.PitStopAction);
                    if (ps.ShowDialog() == DialogResult.OK)
                    {
                        item.PitStopAction = ps.SelectedName;
                        SaveHistory();
                        FillGrid();
                    }
                }
                else if (col == "colImposing")
                {
                    using var imp = new ImposingSelectForm(item.ImposingAction);
                    if (imp.ShowDialog() == DialogResult.OK)
                    {
                        item.ImposingAction = imp.SelectedName;
                        SaveHistory();
                        FillGrid();
                    }
                }
                return;
            }

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
            else if (col == "colPitStop")
            {
                using var f = new PitStopSelectForm(o.PitStopAction);
                if (f.ShowDialog() == DialogResult.OK)
                {
                    o.PitStopAction = f.SelectedName;
                    if (IsVisualGroupOrder(o))
                    {
                        foreach (var item in o.Items)
                            item.PitStopAction = f.SelectedName;
                    }
                    SaveHistory();
                    FillGrid();
                    if (IsVisualGroupOrder(o))
                        SetBottomStatus($"PitStop обновлен для группы {GetOrderDisplayId(o)} (применяется ко всем item)");
                }
            }
            else if (col == "colImposing")
            {
                using var f = new ImposingSelectForm(o.ImposingAction);
                if (f.ShowDialog() == DialogResult.OK)
                {
                    o.ImposingAction = f.SelectedName;
                    if (IsVisualGroupOrder(o))
                    {
                        foreach (var item in o.Items)
                            item.ImposingAction = f.SelectedName;
                    }
                    SaveHistory();
                    FillGrid();
                    if (IsVisualGroupOrder(o))
                        SetBottomStatus($"Imposing обновлен для группы {GetOrderDisplayId(o)} (применяется ко всем item)");
                }
            }
        }
    }
}
