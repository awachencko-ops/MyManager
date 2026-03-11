using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PdfiumViewer;
using Svg;

namespace MyManager
{
    public partial class MainForm
    {
        private void InitializeOrderRowContextMenu()
        {
            _gridMenu.OpenFolder = (stage) =>
            {
                var order = GetContextOrder();
                if (order != null)
                    OpenOrderStageFolder(order, stage);
            };
            _gridMenu.Delete = () =>
            {
                if (TrySelectContextRow())
                    RemoveSelectedOrder();
            };
            _gridMenu.Run = async () =>
            {
                if (TrySelectContextRow())
                    await RunSelectedOrderAsync();
            };
            _gridMenu.Stop = () =>
            {
                if (TrySelectContextRow())
                    StopSelectedOrder();
            };
            _gridMenu.PickFile = (stage, fileType) => _ = PickFileFromContextAsync(stage);
            _gridMenu.RemoveFile = (stage) => RemoveFileFromContext(stage);
            _gridMenu.RenameFile = (stage) => RenameFileFromContext(stage);
            _gridMenu.CopyPathToClipboard = (stage) => CopyPathFromContextToClipboard(stage);
            _gridMenu.PastePathFromClipboard = (stage) => _ = PastePathFromClipboardToContextAsync(stage);
            _gridMenu.ApplyWatermark = () => ApplyWatermarkFromContext(isVertical: false);
            _gridMenu.ApplyWatermarkLeft = () => ApplyWatermarkFromContext(isVertical: true);
            _gridMenu.CopyToGrandpa = CopyPrintFromContextToGrandpa;
            _gridMenu.OpenPitStopMan = OpenPitStopManager;
            _gridMenu.OpenImpMan = OpenImposingManager;
            _gridMenu.RemovePitStopAction = RemovePitStopActionFromContext;
            _gridMenu.RemoveImposingAction = RemoveImposingActionFromContext;
            _gridMenu.OpenOrderLog = () =>
            {
                if (TrySelectContextRow())
                    OpenLogForSelectionOrManager();
            };

            dgvJobs.CellMouseDown += DgvJobs_CellMouseDown;
        }

        private void DgvJobs_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var row = dgvJobs.Rows[e.RowIndex];
            var rowTag = row.Tag?.ToString();
            if (!IsOrderTag(rowTag))
                return;

            _ctxRow = e.RowIndex;
            _ctxCol = e.ColumnIndex;
            if (!TrySelectContextRow())
                return;

            var order = GetContextOrder();
            var allowCopyToGrandpa = order == null || UsesOrderFolderStorage(order);
            var columnName = dgvJobs.Columns[e.ColumnIndex].Name;
            var menu = _gridMenu.Build(columnName, allowCopyToGrandpa);
            if (menu.Items.Count == 0)
                return;

            menu.Show(Cursor.Position);
        }

        private bool TrySelectContextRow()
        {
            if (_ctxRow < 0 || _ctxRow >= dgvJobs.Rows.Count)
                return false;

            var row = dgvJobs.Rows[_ctxRow];
            var columnIndex = _ctxCol >= 0 && _ctxCol < dgvJobs.Columns.Count
                ? _ctxCol
                : colStatus.Index;
            if (columnIndex < 0 || columnIndex >= row.Cells.Count)
                columnIndex = colStatus.Index;

            dgvJobs.CurrentCell = row.Cells[columnIndex];
            row.Selected = true;
            return true;
        }

        private OrderData? GetContextOrder()
        {
            if (_ctxRow < 0)
                return null;

            return GetOrderByRowIndex(_ctxRow);
        }

        private async Task PickFileFromContextAsync(int stage)
        {
            if (stage is < 1 or > 3)
                return;

            try
            {
                var order = GetContextOrder();
                if (order == null)
                    return;

                await PickAndCopyFileForOrderAsync(order, stage);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Ошибка выбора файла: {ex.Message}");
                MessageBox.Show(this, $"Не удалось выбрать файл: {ex.Message}", "Файловая операция", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemoveFileFromContext(int stage)
        {
            if (stage is < 1 or > 3)
                return;

            var order = GetContextOrder();
            if (order == null)
                return;

            RemoveFileFromOrder(order, stage);
        }

        private void RenameFileFromContext(int stage)
        {
            if (stage is < 1 or > 3)
                return;

            var order = GetContextOrder();
            if (order == null)
                return;

            RenameFileForOrder(order, stage);
        }

        private void CopyPathFromContextToClipboard(int stage)
        {
            if (stage is < 1 or > 3)
                return;

            var order = GetContextOrder();
            if (order == null)
                return;

            CopyPathToClipboard(order, stage);
        }

        private async Task PastePathFromClipboardToContextAsync(int stage)
        {
            if (stage is < 1 or > 3)
                return;

            try
            {
                var order = GetContextOrder();
                if (order == null)
                    return;

                await PasteFileFromClipboardAsync(order, stage);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Ошибка вставки из буфера: {ex.Message}");
                MessageBox.Show(this, $"Не удалось вставить файл из буфера: {ex.Message}", "Файловая операция", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyWatermarkFromContext(bool isVertical)
        {
            var order = GetContextOrder();
            if (order == null)
                return;

            ProcessWatermark(order, isVertical);
        }

        private void CopyPrintFromContextToGrandpa()
        {
            var order = GetContextOrder();
            if (order == null)
                return;

            CopyToGrandpa(order);
        }

        private void RemovePitStopActionFromContext()
        {
            var order = GetContextOrder();
            if (order == null)
                return;

            RemovePitStopAction(order);
        }

        private void RemoveImposingActionFromContext()
        {
            var order = GetContextOrder();
            if (order == null)
                return;

            RemoveImposingAction(order);
        }

        private void OpenPitStopManager()
        {
            using var form = new ActionManagerForm();
            form.ShowDialog(this);
        }

        private void OpenImposingManager()
        {
            using var form = new ImposingManagerForm();
            form.ShowDialog(this);
        }

        private void ProcessWatermark(OrderData order, bool isVertical)
        {
            try
            {
                if (!HasExistingFile(order.PrintPath))
                {
                    SetBottomStatus("Файл печати не найден");
                    MessageBox.Show(this, "Файл печати не найден.", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                PdfWatermark.Apply(order, isVertical);
                var pos = isVertical ? "слева" : "сверху";
                SetBottomStatus($"Водяной знак ({pos}) нанесен на {GetOrderDisplayId(order)}");
            }
            catch (IOException)
            {
                SetBottomStatus("Файл занят другой программой. Закройте PDF и повторите");
                MessageBox.Show(this, "Файл занят другой программой. Закройте PDF и повторите.", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось применить водяной знак: {ex.Message}");
                MessageBox.Show(this, $"Не удалось применить водяной знак: {ex.Message}", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ProcessWatermark(OrderData order, OrderFileItem item, bool isVertical)
        {
            try
            {
                if (!HasExistingFile(item.PrintPath))
                {
                    SetBottomStatus("Файл печати item не найден");
                    MessageBox.Show(this, "Файл печати item не найден.", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var originalPrintPath = order.PrintPath;
                try
                {
                    order.PrintPath = item.PrintPath ?? string.Empty;
                    PdfWatermark.Apply(order, isVertical);
                }
                finally
                {
                    order.PrintPath = originalPrintPath;
                }

                var pos = isVertical ? "слева" : "сверху";
                var fileName = Path.GetFileName(item.PrintPath);
                SetBottomStatus($"Водяной знак ({pos}) нанесен на {fileName}");
            }
            catch (IOException)
            {
                SetBottomStatus("Файл занят другой программой. Закройте PDF и повторите");
                MessageBox.Show(this, "Файл занят другой программой. Закройте PDF и повторите.", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось применить водяной знак: {ex.Message}");
                MessageBox.Show(this, $"Не удалось применить водяной знак: {ex.Message}", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemovePitStopAction(OrderData order)
        {
            order.PitStopAction = "-";
            if (order.Items != null)
            {
                foreach (var item in order.Items.Where(x => x != null))
                    item.PitStopAction = "-";
            }

            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus($"PitStop очищен для {GetOrderDisplayId(order)}");
        }

        private void RemoveImposingAction(OrderData order)
        {
            order.ImposingAction = "-";
            if (order.Items != null)
            {
                foreach (var item in order.Items.Where(x => x != null))
                    item.ImposingAction = "-";
            }

            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus($"Imposing очищен для {GetOrderDisplayId(order)}");
        }

        private void RemovePitStopAction(OrderData order, OrderFileItem item)
        {
            item.PitStopAction = "-";
            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
            SetBottomStatus($"PitStop очищен для item {item.ClientFileLabel}");
        }

        private void RemoveImposingAction(OrderData order, OrderFileItem item)
        {
            item.ImposingAction = "-";
            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
            SetBottomStatus($"Imposing очищен для item {item.ClientFileLabel}");
        }

        private string CopyToGrandpa(OrderData order)
        {
            if (!HasExistingFile(order.PrintPath))
            {
                SetBottomStatus("Файл печати не найден");
                MessageBox.Show(this, "Файл печати не найден.", "Копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return string.Empty;
            }

            var sourcePath = order.PrintPath ?? string.Empty;
            var targetName = Path.GetFileName(sourcePath);
            return CopyToGrandpaFromSource(sourcePath, targetName);
        }

        private string CopyToGrandpa(OrderData order, OrderFileItem item)
        {
            if (!HasExistingFile(item.PrintPath))
            {
                SetBottomStatus("Файл печати item не найден");
                MessageBox.Show(this, "Файл печати item не найден.", "Копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return string.Empty;
            }

            var sourcePath = item.PrintPath ?? string.Empty;
            var targetName = Path.GetFileName(sourcePath);
            return CopyToGrandpaFromSource(sourcePath, targetName);
        }

        private void DgvJobs_MouseDown(object? sender, MouseEventArgs e)
        {
            _dragBoxFromMouseDown = Rectangle.Empty;
            _dragSourceRowIndex = -1;
            _dragSourceColumnIndex = -1;

            if (e.Button != MouseButtons.Left)
                return;

            var hit = dgvJobs.HitTest(e.X, e.Y);
            if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
                return;

            var stage = GetStageByColumnIndex(hit.ColumnIndex);
            if (stage == 0)
                return;

            var rowTag = dgvJobs.Rows[hit.RowIndex].Tag?.ToString();
            if (string.IsNullOrWhiteSpace(rowTag))
                return;

            if (!IsOrderTag(rowTag))
                return;

            _dragSourceRowIndex = hit.RowIndex;
            _dragSourceColumnIndex = hit.ColumnIndex;

            var dragSize = SystemInformation.DragSize;
            _dragBoxFromMouseDown = new Rectangle(
                new Point(e.X - (dragSize.Width / 2), e.Y - (dragSize.Height / 2)),
                dragSize);
        }

        private void DgvJobs_MouseMove(object? sender, MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) != MouseButtons.Left)
                return;

            if (_dragBoxFromMouseDown == Rectangle.Empty || _dragBoxFromMouseDown.Contains(e.X, e.Y))
                return;

            if (_dragSourceRowIndex < 0 || _dragSourceColumnIndex < 0)
                return;

            var stage = GetStageByColumnIndex(_dragSourceColumnIndex);
            if (stage == 0)
                return;

            var sourcePath = GetDragSourceFilePath(_dragSourceRowIndex, stage);
            if (!HasExistingFile(sourcePath))
                return;

            var dragData = new DataObject();
            dragData.SetData(DataFormats.FileDrop, new[] { sourcePath });
            dragData.SetData("InternalSourceColumn", _dragSourceColumnIndex);
            dragData.SetData("InternalSourceRow", _dragSourceRowIndex);

            _dragBoxFromMouseDown = Rectangle.Empty;
            dgvJobs.DoDragDrop(dragData, DragDropEffects.Copy | DragDropEffects.Move);
        }

        private void DgvJobs_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (_isRebuildingGrid)
                return;

            if (e.ColumnIndex == colStatus.Index || e.ColumnIndex == colCreated.Index || e.ColumnIndex == colReceived.Index || e.ColumnIndex < 0)
                HandleOrdersGridChanged();
        }

        private void DgvJobs_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0)
                return;

            if (e.Graphics == null)
                return;

            var graphics = e.Graphics;
            var mouseClient = dgvJobs.PointToClient(Cursor.Position);
            var hit = dgvJobs.HitTest(mouseClient.X, mouseClient.Y);
            var isHoveredHeader = hit.RowIndex == -1 && hit.ColumnIndex == e.ColumnIndex;

            if (isHoveredHeader)
            {
                // Keep native hover look on the header under cursor.
                e.Paint(e.CellBounds, e.PaintParts);
            }

            if (!isHoveredHeader)
            {
                using var backBrush = new SolidBrush(Color.White);
                graphics.FillRectangle(backBrush, e.CellBounds);
                e.Paint(
                    e.CellBounds,
                    DataGridViewPaintParts.ContentForeground |
                    DataGridViewPaintParts.ErrorIcon |
                    DataGridViewPaintParts.Focus);
            }

            // Draw header separators in black.
            using var gridPen = new Pen(Color.Black);
            if (e.ColumnIndex == 0)
                graphics.DrawLine(gridPen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Left, e.CellBounds.Bottom - 1);
            graphics.DrawLine(gridPen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Top);
            graphics.DrawLine(gridPen, e.CellBounds.Right - 1, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
            graphics.DrawLine(gridPen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
            e.Handled = true;
        }

        private void DgvJobs_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var row = dgvJobs.Rows[e.RowIndex];
            var rowBackColor = row.Selected
                ? OrdersRowSelectedBackColor
                : (e.RowIndex == _hoveredRowIndex ? OrdersRowHoverBackColor : Color.White);

            if (e.CellStyle == null)
                return;

            e.CellStyle.BackColor = rowBackColor;
            e.CellStyle.SelectionBackColor = OrdersRowSelectedBackColor;
            e.CellStyle.SelectionForeColor = Color.Black;
        }

        private void DgvJobs_CellMouseEnter(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            var isFileColumn = e.ColumnIndex == colSource.Index
                || e.ColumnIndex == colPrep.Index
                || e.ColumnIndex == colPrint.Index;
            dgvJobs.Cursor = isFileColumn ? Cursors.Hand : Cursors.Default;

            var rowTag = dgvJobs.Rows[e.RowIndex].Tag?.ToString();
            if (!IsOrderTag(rowTag))
            {
                if (_hoveredRowIndex != -1)
                {
                    var oldIndex = _hoveredRowIndex;
                    _hoveredRowIndex = -1;
                    if (oldIndex >= 0 && oldIndex < dgvJobs.Rows.Count)
                        dgvJobs.InvalidateRow(oldIndex);
                }
                return;
            }

            if (e.RowIndex == _hoveredRowIndex)
                return;

            var prevIndex = _hoveredRowIndex;
            _hoveredRowIndex = e.RowIndex;
            if (prevIndex >= 0 && prevIndex < dgvJobs.Rows.Count)
                dgvJobs.InvalidateRow(prevIndex);
            dgvJobs.InvalidateRow(_hoveredRowIndex);
        }

        private void DgvJobs_CellMouseLeave(object? sender, EventArgs e)
        {
            dgvJobs.Cursor = Cursors.Default;

            if (_hoveredRowIndex == -1)
                return;

            var oldIndex = _hoveredRowIndex;
            _hoveredRowIndex = -1;
            if (oldIndex >= 0 && oldIndex < dgvJobs.Rows.Count)
                dgvJobs.InvalidateRow(oldIndex);
        }

        private void DgvJobs_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            var columnName = dgvJobs.Columns[e.ColumnIndex].Name;
            if (string.Equals(columnName, "colPitstop", StringComparison.Ordinal))
            {
                SelectPitStopActionFromGrid(e.RowIndex);
                return;
            }

            if (string.Equals(columnName, "colHotimposing", StringComparison.Ordinal))
            {
                SelectImposingActionFromGrid(e.RowIndex);
                return;
            }

            if (e.ColumnIndex == colOrderNumber.Index)
            {
                EditOrderFromGrid(e.RowIndex);
                return;
            }
        }

        private void SelectPitStopActionFromGrid(int rowIndex)
        {
            var order = GetOrderByRowIndex(rowIndex);
            if (order == null)
                return;

            using var form = new PitStopSelectForm(order.PitStopAction);
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            var selected = NormalizeAction(form.SelectedName);
            order.PitStopAction = selected;
            if (order.Items != null)
            {
                foreach (var entry in order.Items.Where(x => x != null))
                    entry.PitStopAction = selected;
            }

            PersistGridChanges($"order|{order.InternalId}");
        }

        private void SelectImposingActionFromGrid(int rowIndex)
        {
            var order = GetOrderByRowIndex(rowIndex);
            if (order == null)
                return;

            using var form = new ImposingSelectForm(order.ImposingAction);
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            var selected = NormalizeAction(form.SelectedName);
            order.ImposingAction = selected;
            if (order.Items != null)
            {
                foreach (var entry in order.Items.Where(x => x != null))
                    entry.ImposingAction = selected;
            }

            PersistGridChanges($"order|{order.InternalId}");
        }

        private void DgvJobs_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            if (!string.Equals(dgvJobs.Columns[e.ColumnIndex].Name, "colStatus", StringComparison.Ordinal))
                return;

            var rowTag = dgvJobs.Rows[e.RowIndex].Tag?.ToString();
            if (!IsOrderTag(rowTag))
                return;

            var order = GetOrderByRowIndex(e.RowIndex);
            if (order == null)
                return;

            var statusText = order.Status ?? string.Empty;
            if (!statusText.Contains("Ошибка", StringComparison.OrdinalIgnoreCase))
                return;

            var reason = string.IsNullOrWhiteSpace(order.LastStatusReason) ? "Причина не указана" : order.LastStatusReason;
            var source = string.IsNullOrWhiteSpace(order.LastStatusSource) ? "неизвестно" : order.LastStatusSource;
            var stamp = order.LastStatusAt == default
                ? "неизвестно"
                : order.LastStatusAt.ToString("dd.MM.yyyy HH:mm:ss");

            e.ToolTipText = $"{statusText}\nИсточник: {source}\nПричина: {reason}\nВремя: {stamp}";
        }

        private async void DgvJobs_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var stage = GetStageByColumnIndex(e.ColumnIndex);
            if (stage == 0)
                return;

            var rowTag = dgvJobs.Rows[e.RowIndex].Tag?.ToString();
            var order = GetOrderByRowIndex(e.RowIndex);
            if (order == null || string.IsNullOrWhiteSpace(rowTag))
                return;

            try
            {
                if (!IsOrderTag(rowTag))
                    return;

                var orderPath = GetOrderStagePath(order, stage);
                if (HasExistingFile(orderPath))
                {
                    OpenFileDefault(orderPath);
                    return;
                }

                await PickAndCopyFileForOrderAsync(order, stage);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось обработать действие по файлу: {ex.Message}");
                MessageBox.Show(this, $"Не удалось обработать действие по файлу: {ex.Message}", "Файловая операция", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DgvJobs_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void DgvJobs_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var clientPoint = dgvJobs.PointToClient(new Point(e.X, e.Y));
            var hit = dgvJobs.HitTest(clientPoint.X, clientPoint.Y);
            if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var stage = GetStageByColumnIndex(hit.ColumnIndex);
            if (stage == 0)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var rowTag = dgvJobs.Rows[hit.RowIndex].Tag?.ToString();
            var order = GetOrderByRowIndex(hit.RowIndex);
            if (order == null || string.IsNullOrWhiteSpace(rowTag))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if (!IsOrderTag(rowTag))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var draggingFile = (e.Data.GetData(DataFormats.FileDrop) as string[])?.FirstOrDefault();
            draggingFile = CleanPath(draggingFile);
            if (string.IsNullOrWhiteSpace(draggingFile))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            var existingPath = GetOrderStagePath(order, stage);

            e.Effect = PathsEqual(existingPath, draggingFile)
                ? DragDropEffects.None
                : DragDropEffects.Copy;
        }

        private async void DgvJobs_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true)
                return;

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var sourceFile = CleanPath(files?.FirstOrDefault());
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
                return;

            var clientPoint = dgvJobs.PointToClient(new Point(e.X, e.Y));
            var hit = dgvJobs.HitTest(clientPoint.X, clientPoint.Y);
            if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
                return;

            var stage = GetStageByColumnIndex(hit.ColumnIndex);
            if (stage == 0)
                return;

            var row = dgvJobs.Rows[hit.RowIndex];
            var rowTag = row.Tag?.ToString();
            var order = GetOrderByRowIndex(hit.RowIndex);
            if (order == null || string.IsNullOrWhiteSpace(rowTag))
                return;

            try
            {
                if (!IsOrderTag(rowTag))
                    return;

                if (!await AddFileToOrderAsync(order, sourceFile, stage))
                    return;

                PersistGridChanges($"order|{order.InternalId}");
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось добавить файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось добавить файл: {ex.Message}", "Drag&Drop", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task PickAndCopyFileForOrderAsync(OrderData order, int stage)
        {
            var targetFolder = GetStageFolder(order, stage);
            Directory.CreateDirectory(targetFolder);

            using var ofd = new OpenFileDialog
            {
                Filter = "PDF|*.pdf|Все файлы|*.*",
                InitialDirectory = targetFolder,
                RestoreDirectory = false
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            if (!await AddFileToOrderAsync(order, ofd.FileName, stage))
                return;

            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus("Файл добавлен в заказ");
        }

        private async Task PickAndCopyFileForItemAsync(OrderData order, OrderFileItem item, int stage)
        {
            var targetFolder = GetStageFolder(order, stage);
            Directory.CreateDirectory(targetFolder);

            using var ofd = new OpenFileDialog
            {
                Filter = "PDF|*.pdf|Все файлы|*.*",
                InitialDirectory = targetFolder,
                RestoreDirectory = false
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            if (!await AddFileToItemAsync(order, item, ofd.FileName, stage))
                return;

            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
            SetBottomStatus("Файл добавлен в item");
        }

        private async Task<bool> AddFileToOrderAsync(OrderData order, string sourceFile, int stage)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                return false;

            if (stage == 3 && !await EnsureSimpleOrderInfoForPrintAsync(order))
                return false;

            string targetName;
            if (stage == 3 && !string.IsNullOrWhiteSpace(order.Id))
                targetName = $"{order.Id}{Path.GetExtension(cleanSource)}";
            else
                targetName = EnsureUniqueStageFileName(order, stage, Path.GetFileName(cleanSource));

            var newPath = stage == 3
                ? CopyPrintFile(order, cleanSource, targetName)
                : CopyIntoStage(order, stage, cleanSource, targetName);

            if (stage == 2)
                EnsureSourceCopy(order, cleanSource);

            UpdateOrderFilePath(order, stage, newPath);
            return true;
        }

        private async Task<bool> AddFileToItemAsync(OrderData order, OrderFileItem item, string sourceFile, int stage)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                return false;

            if (stage == 3 && !await EnsureSimpleOrderInfoForPrintAsync(order))
                return false;

            if (string.IsNullOrWhiteSpace(item.ClientFileLabel))
                item.ClientFileLabel = Path.GetFileNameWithoutExtension(cleanSource);

            string newPath;
            if (stage == 3)
            {
                var printName = EnsureUniqueStageFileName(order, 3, BuildItemPrintFileName(order, item, cleanSource));
                newPath = CopyPrintFile(order, cleanSource, printName);
            }
            else
            {
                var targetName = EnsureUniqueStageFileName(order, stage, Path.GetFileName(cleanSource));
                newPath = CopyIntoStage(order, stage, cleanSource, targetName);
            }

            UpdateItemFilePath(order, item, stage, newPath);
            return true;
        }

        private void PersistGridChanges(string selectedTag)
        {
            SaveHistory();
            RebuildOrdersGrid();
            if (!string.IsNullOrWhiteSpace(selectedTag))
                TryRestoreSelectedRowByTag(selectedTag);
        }

        private int GetStageByColumnIndex(int columnIndex)
        {
            var colName = dgvJobs.Columns[columnIndex].Name;
            return colName switch
            {
                "colSource" => 1,
                "colPrep" => 2,
                "colPrint" => 3,
                _ => 0
            };
        }

        private OrderData? GetOrderByRowIndex(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= dgvJobs.Rows.Count)
                return null;

            var rowTag = dgvJobs.Rows[rowIndex].Tag?.ToString();
            if (!IsOrderTag(rowTag))
                return null;

            var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
            return FindOrderByInternalId(orderInternalId);
        }

        private static string GetOrderStagePath(OrderData order, int stage)
        {
            return stage switch
            {
                1 => order.SourcePath ?? string.Empty,
                2 => order.PreparedPath ?? string.Empty,
                3 => order.PrintPath ?? string.Empty,
                _ => string.Empty
            };
        }

        private static string GetItemStagePath(OrderFileItem item, int stage)
        {
            return stage switch
            {
                1 => item.SourcePath ?? string.Empty,
                2 => item.PreparedPath ?? string.Empty,
                3 => item.PrintPath ?? string.Empty,
                _ => string.Empty
            };
        }

        private void RemoveFileFromOrder(OrderData order, int stage)
        {
            var currentPath = GetOrderStagePath(order, stage);
            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            var decision = MessageBox.Show(
                this,
                $"Удалить файл {Path.GetFileName(currentPath)}?",
                "Удаление файла",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (decision != DialogResult.Yes)
                return;

            try
            {
                if (File.Exists(currentPath))
                    File.Delete(currentPath);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось удалить файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось удалить файл: {ex.Message}", "Удаление файла", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateOrderFilePath(order, stage, string.Empty);
            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus("Файл удален");
        }

        private void RemoveFileFromItem(OrderData order, OrderFileItem item, int stage)
        {
            var currentPath = GetItemStagePath(item, stage);
            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            var decision = MessageBox.Show(
                this,
                $"Удалить файл {Path.GetFileName(currentPath)}?",
                "Удаление файла",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (decision != DialogResult.Yes)
                return;

            try
            {
                if (File.Exists(currentPath))
                    File.Delete(currentPath);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось удалить файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось удалить файл: {ex.Message}", "Удаление файла", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateItemFilePath(order, item, stage, string.Empty);
            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
            SetBottomStatus("Файл item удален");
        }

        private void RenameFileForOrder(OrderData order, int stage)
        {
            var currentPath = GetOrderStagePath(order, stage);
            if (!HasExistingFile(currentPath))
                return;

            if (!TryBuildRenamedPath(currentPath, out var renamedPath))
                return;

            try
            {
                File.Move(currentPath, renamedPath);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось переименовать файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось переименовать файл: {ex.Message}", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateOrderFilePath(order, stage, renamedPath);
            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus("Файл переименован");
        }

        private void RenameFileForItem(OrderData order, OrderFileItem item, int stage)
        {
            var currentPath = GetItemStagePath(item, stage);
            if (!HasExistingFile(currentPath))
                return;

            if (!TryBuildRenamedPath(currentPath, out var renamedPath))
                return;

            try
            {
                File.Move(currentPath, renamedPath);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось переименовать файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось переименовать файл: {ex.Message}", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateItemFilePath(order, item, stage, renamedPath);
            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
            SetBottomStatus("Файл item переименован");
        }

        private bool TryBuildRenamedPath(string currentPath, out string renamedPath)
        {
            renamedPath = string.Empty;
            if (!HasExistingFile(currentPath))
                return false;

            var oldName = Path.GetFileNameWithoutExtension(currentPath);
            var extension = Path.GetExtension(currentPath);
            var nextName = ShowInputDialog("Переименование", "Введите новое имя файла:", oldName);
            if (string.IsNullOrWhiteSpace(nextName))
                return false;

            nextName = nextName.Trim();
            if (string.Equals(nextName, oldName, StringComparison.Ordinal))
                return false;

            foreach (var invalid in Path.GetInvalidFileNameChars())
                nextName = nextName.Replace(invalid, '_');

            var directory = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            var targetPath = Path.Combine(directory, nextName + extension);
            if (PathsEqual(currentPath, targetPath))
                return false;

            if (File.Exists(targetPath))
            {
                SetBottomStatus("Файл с таким именем уже существует");
                MessageBox.Show(this, "Файл с таким именем уже существует.", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            renamedPath = targetPath;
            return true;
        }

        private string ShowInputDialog(string title, string promptText, string initialValue)
        {
            using var form = new Form();
            using var promptLabel = new Label();
            using var inputTextBox = new TextBox();
            using var okButton = new Button();
            using var cancelButton = new Button();

            form.Text = title;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = new Size(430, 150);
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.ShowInTaskbar = false;

            promptLabel.Text = promptText;
            promptLabel.SetBounds(16, 16, 398, 22);

            inputTextBox.Text = initialValue;
            inputTextBox.SetBounds(16, 46, 398, 26);

            okButton.Text = "ОК";
            okButton.DialogResult = DialogResult.OK;
            okButton.SetBounds(238, 96, 82, 32);

            cancelButton.Text = "Отмена";
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.SetBounds(332, 96, 82, 32);

            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;
            form.Controls.AddRange([promptLabel, inputTextBox, okButton, cancelButton]);

            return form.ShowDialog(this) == DialogResult.OK
                ? inputTextBox.Text
                : initialValue;
        }

        private void CopyPathToClipboard(OrderData order, int stage)
        {
            CopyExistingPathToClipboard(GetOrderStagePath(order, stage));
        }

        private void CopyPathToClipboard(OrderFileItem item, int stage)
        {
            CopyExistingPathToClipboard(GetItemStagePath(item, stage));
        }

        private void CopyExistingPathToClipboard(string? path)
        {
            if (!HasExistingFile(path))
            {
                SetBottomStatus("Путь к файлу не найден");
                MessageBox.Show(this, "Путь к файлу не найден.", "Буфер обмена", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            TrySetClipboardText(path);
            SetBottomStatus("Путь скопирован в буфер");
        }

        private async Task PasteFileFromClipboardAsync(OrderData order, int stage)
        {
            var clipboardFilePath = TryGetClipboardFilePath();
            if (string.IsNullOrWhiteSpace(clipboardFilePath))
                return;

            if (!await AddFileToOrderAsync(order, clipboardFilePath, stage))
                return;

            PersistGridChanges($"order|{order.InternalId}");
        }

        private async Task PasteFileFromClipboardAsync(OrderData order, OrderFileItem item, int stage)
        {
            var clipboardFilePath = TryGetClipboardFilePath();
            if (string.IsNullOrWhiteSpace(clipboardFilePath))
                return;

            if (!await AddFileToItemAsync(order, item, clipboardFilePath, stage))
                return;

            PersistGridChanges($"item|{order.InternalId}|{item.ItemId}");
        }

        private string? TryGetClipboardFilePath()
        {
            string clipboardText;
            try
            {
                clipboardText = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось прочитать буфер обмена: {ex.Message}");
                MessageBox.Show(this, $"Не удалось прочитать буфер обмена: {ex.Message}", "Буфер обмена", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            var cleanPath = CleanPath(clipboardText?.Replace("\"", string.Empty));
            if (string.IsNullOrWhiteSpace(cleanPath))
            {
                SetBottomStatus("Буфер обмена пуст");
                MessageBox.Show(this, "Буфер обмена пуст.", "Буфер обмена", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            if (!File.Exists(cleanPath))
            {
                SetBottomStatus("Файл не найден по указанному пути");
                MessageBox.Show(this, $"Файл не найден по указанному пути:\n{cleanPath}", "Буфер обмена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            return cleanPath;
        }

        private string GetDragSourceFilePath(int rowIndex, int stage)
        {
            if (rowIndex < 0 || rowIndex >= dgvJobs.Rows.Count)
                return string.Empty;

            var rowTag = dgvJobs.Rows[rowIndex].Tag?.ToString();
            if (string.IsNullOrWhiteSpace(rowTag))
                return string.Empty;

            var order = GetOrderByRowIndex(rowIndex);
            if (order == null)
                return string.Empty;

            if (!IsOrderTag(rowTag))
                return string.Empty;

            return GetOrderStagePath(order, stage);
        }

        private static void SetItemStagePath(OrderFileItem item, int stage, string path)
        {
            if (stage == 1)
                item.SourcePath = path;
            else if (stage == 2)
                item.PreparedPath = path;
            else if (stage == 3)
                item.PrintPath = path;
        }

        private void UpdateOrderFilePath(OrderData order, int stage, string path)
        {
            if (stage == 1)
                order.SourcePath = path;
            else if (stage == 2)
                order.PreparedPath = path;
            else if (stage == 3)
                order.PrintPath = path;

            var status = ResolveWorkflowStatus(order.SourcePath, order.PreparedPath, order.PrintPath);
            SetOrderStatus(order, status, "file-sync", $"stage-{stage}", persistHistory: false, rebuildGrid: false);

            if (order.Items != null && order.Items.Count == 1)
            {
                var singleItem = order.Items[0];
                SetItemStagePath(singleItem, stage, path);
                singleItem.FileStatus = status;
                singleItem.UpdatedAt = DateTime.Now;
            }
        }

        private void UpdateItemFilePath(OrderData order, OrderFileItem item, int stage, string path)
        {
            SetItemStagePath(item, stage, path);
            item.FileStatus = ResolveWorkflowStatus(item.SourcePath, item.PreparedPath, item.PrintPath);
            item.UpdatedAt = DateTime.Now;

            if (order.Items != null && order.Items.Count == 1 && string.Equals(order.Items[0].ItemId, item.ItemId, StringComparison.Ordinal))
            {
                if (stage == 1)
                    order.SourcePath = item.SourcePath;
                else if (stage == 2)
                    order.PreparedPath = item.PreparedPath;
                else if (stage == 3)
                    order.PrintPath = item.PrintPath;

                SetOrderStatus(order, item.FileStatus, "file-sync", $"item-stage-{stage}", persistHistory: false, rebuildGrid: false);
                return;
            }

            RefreshOrderStatusFromItems(order);
        }

        private void RefreshOrderStatusFromItems(OrderData order)
        {
            if (order.Items == null || order.Items.Count == 0)
            {
                var status = ResolveWorkflowStatus(order.SourcePath, order.PreparedPath, order.PrintPath);
                SetOrderStatus(order, status, "file-sync", "no-items", persistHistory: false, rebuildGrid: false);
                return;
            }

            var items = order.Items.Where(x => x != null).ToList();
            if (items.Count == 0)
            {
                SetOrderStatus(order, "Ожидание", "file-sync", "empty-items", persistHistory: false, rebuildGrid: false);
                return;
            }

            var total = items.Count;
            var done = items.Count(x => HasExistingFile(x.PrintPath));
            var active = items.Count(x => HasExistingFile(x.SourcePath) || HasExistingFile(x.PreparedPath) || HasExistingFile(x.PrintPath));

            var statusValue = done == total
                ? "Завершено"
                : active > 0
                    ? "Обрабатывается"
                    : "Ожидание";

            SetOrderStatus(order, statusValue, "file-sync", "aggregate", persistHistory: false, rebuildGrid: false);
        }

        private static string ResolveWorkflowStatus(string? sourcePath, string? preparedPath, string? printPath)
        {
            if (HasExistingFile(printPath))
                return "Завершено";

            if (HasExistingFile(preparedPath) || HasExistingFile(sourcePath))
                return "Обрабатывается";

            return "Ожидание";
        }

        private string GetStageFolder(OrderData order, int stage)
        {
            if (stage == 3 && HasExistingFile(order.PrintPath))
                return Path.GetDirectoryName(order.PrintPath) ?? GetTempStageFolder(stage);

            if (string.IsNullOrWhiteSpace(order.FolderName))
                return GetTempStageFolder(stage);

            var sub = stage switch
            {
                1 => "1. исходные",
                2 => "2. подготовка",
                3 => "3. печать",
                _ => string.Empty
            };

            var path = Path.Combine(_ordersRootPath, order.FolderName, sub);
            Directory.CreateDirectory(path);
            return path;
        }

        private string GetTempStageFolder(int stage)
        {
            var sub = stage switch
            {
                1 => "in",
                2 => "prepress",
                3 => "print",
                _ => string.Empty
            };

            var root = string.IsNullOrWhiteSpace(_tempRootPath)
                ? Path.Combine(_ordersRootPath, "_temp")
                : _tempRootPath;
            var path = Path.Combine(root, sub);
            Directory.CreateDirectory(path);
            return path;
        }

        private string CopyIntoStage(OrderData order, int stage, string sourceFile, string? targetName = null)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                throw new FileNotFoundException("Файл для копирования не найден.", cleanSource);

            var stageFolder = GetStageFolder(order, stage);
            Directory.CreateDirectory(stageFolder);

            var fileName = string.IsNullOrWhiteSpace(targetName) ? Path.GetFileName(cleanSource) : targetName;
            var destination = Path.Combine(stageFolder, fileName);

            if (PathsEqual(cleanSource, destination))
                return destination;

            if (File.Exists(destination))
                return destination;

            File.Copy(cleanSource, destination, true);
            return destination;
        }

        private string CopyPrintFile(OrderData order, string sourceFile, string targetName)
        {
            if (!UsesOrderFolderStorage(order))
                return CopyToGrandpaFromSource(sourceFile, targetName);

            return CopyIntoStage(order, 3, sourceFile, targetName);
        }

        private static bool UsesOrderFolderStorage(OrderData order)
        {
            return !string.IsNullOrWhiteSpace(order.FolderName);
        }

        private string CopyToGrandpaFromSource(string sourceFile, string targetName)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                throw new FileNotFoundException("Файл для копирования не найден.", cleanSource);

            var destinationRoot = string.IsNullOrWhiteSpace(_grandpaFolder)
                ? GetTempStageFolder(3)
                : _grandpaFolder;
            Directory.CreateDirectory(destinationRoot);

            var destination = Path.Combine(destinationRoot, targetName);

            if (PathsEqual(cleanSource, destination))
            {
                TrySetClipboardText(destination);
                SetBottomStatus("Скопировано в Дедушку");
                return destination;
            }

            if (File.Exists(destination))
            {
                TrySetClipboardText(destination);
                SetBottomStatus("Скопировано в Дедушку");
                return destination;
            }

            File.Copy(cleanSource, destination, true);
            TrySetClipboardText(destination);
            SetBottomStatus("Скопировано в Дедушку");
            return destination;
        }

        private static void TrySetClipboardText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                // Clipboard access should not fail file workflow.
            }
        }

        private string EnsureUniqueStageFileName(OrderData order, int stage, string fileName)
        {
            var folder = GetStageFolder(order, stage);
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(fileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var candidate = fileName;
            var index = 1;

            while (File.Exists(Path.Combine(folder, candidate)))
            {
                candidate = $"{baseName}_{index}{ext}";
                index++;
            }

            return candidate;
        }

        private string BuildItemPrintFileName(OrderData order, OrderFileItem item, string sourceFile)
        {
            var ext = Path.GetExtension(sourceFile);
            var orderNo = string.IsNullOrWhiteSpace(order.Id) ? "order" : order.Id;
            var orderedItems = (order.Items ?? []).OrderBy(x => x.SequenceNo).ToList();
            var idx = orderedItems.FindIndex(x => string.Equals(x.ItemId, item.ItemId, StringComparison.Ordinal));
            var itemIndex = idx >= 0 ? idx + 1 : 1;
            return $"{orderNo}_{itemIndex}{ext}";
        }

        private void EnsureSourceCopy(OrderData order, string sourceFile)
        {
            if (!string.IsNullOrWhiteSpace(order.SourcePath) && HasExistingFile(order.SourcePath))
                return;

            var newPath = CopyIntoStage(order, 1, sourceFile);
            UpdateOrderFilePath(order, 1, newPath);
        }

        private async Task<bool> EnsureSimpleOrderInfoForPrintAsync(OrderData order)
        {
            if (UsesOrderFolderStorage(order))
                return true;

            if (!string.IsNullOrWhiteSpace(order.Id))
                return true;

            using var form = new SimpleOrderForm(order);
            if (form.ShowDialog(this) != DialogResult.OK)
                return false;

            order.Id = form.OrderNumber.Trim();
            order.OrderDate = form.OrderDate;
            if (order.ArrivalDate == default)
                order.ArrivalDate = DateTime.Now;

            return !string.IsNullOrWhiteSpace(order.Id);
        }

        private static void OpenFileDefault(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }

        private static bool HasExistingFile(string? path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        private static string CleanPath(string? path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Trim('"');
        }

        private static bool PathsEqual(string? leftPath, string? rightPath)
        {
            if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
                return false;

            var left = NormalizePath(leftPath);
            var right = NormalizePath(rightPath);
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim();
            }
        }
    }
}
