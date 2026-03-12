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
        private void DgvJobs_MouseDown(object? sender, MouseEventArgs e)
        {
            StopGridHoverActivation();

            _dragBoxFromMouseDown = Rectangle.Empty;
            _dragSourceRowIndex = -1;
            _dragSourceColumnIndex = -1;

            var hit = dgvJobs.HitTest(e.X, e.Y);
            if (e.Button == MouseButtons.Left && hit.Type == DataGridViewHitTestType.None)
            {
                ClearGridSelection();
                SyncTilesSelectionWithGrid();
                UpdateActionButtonsState();
                UpdateTrayStatsIndicator();
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            var hasSelectionModifier = (ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None;
            if (hasSelectionModifier)
                return;

            if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
                return;

            var clickedRow = dgvJobs.Rows[hit.RowIndex];
            var clickedRowTag = clickedRow.Tag?.ToString();

            var stage = GetStageByColumnIndex(hit.ColumnIndex);
            if (stage == 0)
                return;

            var canStartFileDrag =
                clickedRow.Selected
                && dgvJobs.SelectedRows.Count <= 1
                && dgvJobs.CurrentCell != null
                && dgvJobs.CurrentCell.RowIndex == hit.RowIndex;
            if (!canStartFileDrag)
                return;

            if (string.IsNullOrWhiteSpace(clickedRowTag))
                return;

            if (!IsOrderTag(clickedRowTag))
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
            if ((e.Button & MouseButtons.Left) == MouseButtons.Left)
                StopGridHoverActivation();

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

        private void DgvJobs_MouseUp(object? sender, MouseEventArgs e)
        {
            // Selection behavior is handled by DataGridView defaults.
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
            if (e.ColumnIndex < 0)
                return;

            if (e.RowIndex >= 0)
            {
                // Suppress dotted focus rectangle ("marching ants") on current cell.
                e.Paint(e.CellBounds, e.PaintParts & ~DataGridViewPaintParts.Focus);
                e.Handled = true;
                return;
            }

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
            var isAttachmentColumn = e.ColumnIndex == colSource.Index
                || e.ColumnIndex == colPrep.Index
                || e.ColumnIndex == colPrint.Index;
            var textValue = e.Value?.ToString();
            var hasAttachmentText = isAttachmentColumn
                && !string.IsNullOrWhiteSpace(textValue)
                && !string.Equals(textValue, "-", StringComparison.Ordinal)
                && !string.Equals(textValue, "...", StringComparison.Ordinal);
            var foreColor = hasAttachmentText ? Color.RoyalBlue : Color.Black;

            if (e.CellStyle == null)
                return;

            e.CellStyle.BackColor = rowBackColor;
            e.CellStyle.SelectionBackColor = OrdersRowSelectedBackColor;
            e.CellStyle.ForeColor = foreColor;
            e.CellStyle.SelectionForeColor = foreColor;
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
                StopGridHoverActivation();

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
            // Explorer-like behavior: hovering should not activate or change selection.
            StopGridHoverActivation();
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

        private void DgvJobs_MouseLeave(object? sender, EventArgs e)
        {
            StopGridHoverActivation();
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
            StopGridHoverActivation();

            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            // Ctrl/Shift clicks are used for multi-selection (Explorer-like behavior).
            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
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

        private void SetGridHoverActivationCandidate(int rowIndex)
        {
            if (!dgvJobs.Visible || rowIndex < 0 || rowIndex >= dgvJobs.Rows.Count)
            {
                StopGridHoverActivation();
                return;
            }

            if ((Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left)
            {
                StopGridHoverActivation();
                return;
            }

            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
            {
                StopGridHoverActivation();
                return;
            }

            var row = dgvJobs.Rows[rowIndex];
            if (row.IsNewRow || !row.Visible)
            {
                StopGridHoverActivation();
                return;
            }

            var orderInternalId = ExtractOrderInternalIdFromTag(row.Tag?.ToString());
            if (string.IsNullOrWhiteSpace(orderInternalId))
            {
                StopGridHoverActivation();
                return;
            }

            if (string.Equals(_gridHoverCandidateOrderInternalId, orderInternalId, StringComparison.Ordinal))
                return;

            _gridHoverCandidateOrderInternalId = orderInternalId;
            _gridHoverActivateTimer?.Stop();
            _gridHoverActivateTimer?.Start();
        }

        private void StopGridHoverActivation()
        {
            _gridHoverActivateTimer?.Stop();
            _gridHoverCandidateOrderInternalId = null;
        }

        private void GridHoverActivateTimer_Tick(object? sender, EventArgs e)
        {
            _gridHoverActivateTimer?.Stop();

            if (!dgvJobs.Visible)
                return;

            if ((Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left)
                return;

            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
                return;

            var orderInternalId = _gridHoverCandidateOrderInternalId;
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return;

            var selectedOrderIds = GetSelectedOrderInternalIdsFromGrid();
            if (selectedOrderIds.Count == 1 && selectedOrderIds.Contains(orderInternalId))
                return;

            if (!TrySelectGridRowByOrderInternalId(orderInternalId))
                return;

            SyncTilesSelectionWithGrid();
            UpdateActionButtonsState();
            UpdateTrayStatsIndicator();
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

    }
}
