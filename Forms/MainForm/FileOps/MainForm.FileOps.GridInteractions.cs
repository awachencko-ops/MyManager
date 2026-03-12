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
            EndGridRubberSelection(applySelectionSync: false);

            _dragBoxFromMouseDown = Rectangle.Empty;
            _dragSourceRowIndex = -1;
            _dragSourceColumnIndex = -1;

            var hit = dgvJobs.HitTest(e.X, e.Y);
            if (e.Button != MouseButtons.Left)
                return;

            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
                return;

            var canStartRubberSelection = hit.Type != DataGridViewHitTestType.ColumnHeader
                && hit.Type != DataGridViewHitTestType.TopLeftHeader;

            if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
            {
                if (canStartRubberSelection)
                    BeginGridRubberSelectionPending(e.Location);
                return;
            }

            var stage = GetStageByColumnIndex(hit.ColumnIndex);
            if (stage == 0)
            {
                if (canStartRubberSelection)
                    BeginGridRubberSelectionPending(e.Location);
                return;
            }

            var row = dgvJobs.Rows[hit.RowIndex];
            var canStartFileDrag =
                row.Selected
                && dgvJobs.SelectedRows.Count <= 1
                && dgvJobs.CurrentCell != null
                && dgvJobs.CurrentCell.RowIndex == hit.RowIndex;
            if (!canStartFileDrag)
            {
                if (canStartRubberSelection)
                    BeginGridRubberSelectionPending(e.Location);
                return;
            }

            var rowTag = dgvJobs.Rows[hit.RowIndex].Tag?.ToString();
            if (string.IsNullOrWhiteSpace(rowTag))
            {
                if (canStartRubberSelection)
                    BeginGridRubberSelectionPending(e.Location);
                return;
            }

            if (!IsOrderTag(rowTag))
            {
                if (canStartRubberSelection)
                    BeginGridRubberSelectionPending(e.Location);
                return;
            }

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

            if (_isGridRubberSelectionPending || _isGridRubberSelecting)
            {
                if ((e.Button & MouseButtons.Left) != MouseButtons.Left)
                {
                    EndGridRubberSelection(applySelectionSync: true);
                    return;
                }

                if ((ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
                {
                    EndGridRubberSelection(applySelectionSync: false);
                    return;
                }

                if (_isGridRubberSelectionPending)
                {
                    var dragSize = SystemInformation.DragSize;
                    var thresholdRect = new Rectangle(
                        _gridRubberSelectionStartPoint.X - (dragSize.Width / 2),
                        _gridRubberSelectionStartPoint.Y - (dragSize.Height / 2),
                        dragSize.Width,
                        dragSize.Height);
                    if (thresholdRect.Contains(e.Location))
                        return;

                    _isGridRubberSelectionPending = false;
                    _isGridRubberSelecting = true;
                    _dragBoxFromMouseDown = Rectangle.Empty;
                    dgvJobs.Capture = true;
                }

                UpdateGridRubberSelection(e.Location);
                return;
            }

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
            _dragBoxFromMouseDown = Rectangle.Empty;

            if (e.Button != MouseButtons.Left)
                return;

            if (_isGridRubberSelecting)
            {
                _suppressNextGridCellClick = true;
                EndGridRubberSelection(applySelectionSync: true);
                return;
            }

            if (!_isGridRubberSelectionPending)
                return;

            _isGridRubberSelectionPending = false;
            _gridRubberSelectionStartPoint = Point.Empty;

            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
                return;

            var hit = dgvJobs.HitTest(e.X, e.Y);
            if (hit.Type != DataGridViewHitTestType.None)
                return;

            ClearGridSelection();
            SyncTilesSelectionWithGrid();
            UpdateActionButtonsState();
            UpdateTrayStatsIndicator();
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

            if (e.CellStyle == null)
                return;

            e.CellStyle.BackColor = rowBackColor;
            e.CellStyle.SelectionBackColor = OrdersRowSelectedBackColor;
            e.CellStyle.SelectionForeColor = Color.Black;
        }

        private void DgvJobs_CellMouseEnter(object? sender, DataGridViewCellEventArgs e)
        {
            if (_isGridRubberSelecting || _isGridRubberSelectionPending)
            {
                dgvJobs.Cursor = Cursors.Default;
                return;
            }

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
            SetGridHoverActivationCandidate(e.RowIndex);
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

            if ((Control.MouseButtons & MouseButtons.Left) != MouseButtons.Left
                && _isGridRubberSelectionPending)
            {
                EndGridRubberSelection(applySelectionSync: false);
            }
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
            if (_suppressNextGridCellClick)
            {
                _suppressNextGridCellClick = false;
                return;
            }

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

        private void DgvJobs_Paint(object? sender, PaintEventArgs e)
        {
            if (!_isGridRubberSelecting || _gridRubberSelectionRect.IsEmpty || e.Graphics == null)
                return;

            using var fillBrush = new SolidBrush(Color.FromArgb(90, 103, 163, 216));
            using var borderPen = new Pen(Color.FromArgb(76, 133, 196), 1f);
            e.Graphics.FillRectangle(fillBrush, _gridRubberSelectionRect);

            var borderRect = new Rectangle(
                _gridRubberSelectionRect.X,
                _gridRubberSelectionRect.Y,
                Math.Max(0, _gridRubberSelectionRect.Width - 1),
                Math.Max(0, _gridRubberSelectionRect.Height - 1));
            e.Graphics.DrawRectangle(borderPen, borderRect);
        }

        private void BeginGridRubberSelectionPending(Point startPoint)
        {
            _isGridRubberSelectionPending = true;
            _isGridRubberSelecting = false;
            _gridRubberSelectionStartPoint = startPoint;
            _gridRubberSelectionRect = Rectangle.Empty;
            _suppressNextGridCellClick = false;
        }

        private void UpdateGridRubberSelection(Point currentPoint)
        {
            var nextRect = BuildNormalizedRectangle(_gridRubberSelectionStartPoint, currentPoint);
            if (nextRect == _gridRubberSelectionRect)
                return;

            var oldRect = _gridRubberSelectionRect;
            _gridRubberSelectionRect = nextRect;
            ApplyGridRubberSelectionByRectangle(_gridRubberSelectionRect);

            if (!oldRect.IsEmpty)
                dgvJobs.Invalidate(Rectangle.Inflate(oldRect, 2, 2));
            if (!_gridRubberSelectionRect.IsEmpty)
                dgvJobs.Invalidate(Rectangle.Inflate(_gridRubberSelectionRect, 2, 2));
        }

        private void ApplyGridRubberSelectionByRectangle(Rectangle selectionRect)
        {
            var targetColumnIndex = colPrint.Index >= 0 ? colPrint.Index : colStatus.Index;
            DataGridViewRow? firstSelectedRow = null;

            _isSyncingGridSelection = true;
            try
            {
                foreach (DataGridViewRow row in dgvJobs.Rows)
                {
                    if (row.IsNewRow || !row.Visible || !IsOrderTag(row.Tag?.ToString()))
                    {
                        if (row.Selected)
                            row.Selected = false;
                        continue;
                    }

                    var rowBounds = GetGridRowSelectionBounds(row.Index);
                    var shouldSelect = !rowBounds.IsEmpty && selectionRect.IntersectsWith(rowBounds);
                    if (row.Selected != shouldSelect)
                        row.Selected = shouldSelect;

                    if (shouldSelect && firstSelectedRow == null)
                        firstSelectedRow = row;
                }

                dgvJobs.CurrentCell = firstSelectedRow != null
                    ? firstSelectedRow.Cells[targetColumnIndex]
                    : null;
            }
            finally
            {
                _isSyncingGridSelection = false;
            }
        }

        private Rectangle GetGridRowSelectionBounds(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= dgvJobs.Rows.Count)
                return Rectangle.Empty;

            var rowRect = dgvJobs.GetRowDisplayRectangle(rowIndex, cutOverflow: false);
            if (rowRect.Width <= 0 || rowRect.Height <= 0)
                return Rectangle.Empty;

            return new Rectangle(0, rowRect.Top, dgvJobs.ClientSize.Width, rowRect.Height);
        }

        private static Rectangle BuildNormalizedRectangle(Point startPoint, Point endPoint)
        {
            var left = Math.Min(startPoint.X, endPoint.X);
            var top = Math.Min(startPoint.Y, endPoint.Y);
            var right = Math.Max(startPoint.X, endPoint.X);
            var bottom = Math.Max(startPoint.Y, endPoint.Y);
            if (right == left)
                right++;
            if (bottom == top)
                bottom++;

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private void EndGridRubberSelection(bool applySelectionSync)
        {
            var hadActiveRubberSelection = _isGridRubberSelecting;
            var oldRect = _gridRubberSelectionRect;

            _isGridRubberSelectionPending = false;
            _isGridRubberSelecting = false;
            _gridRubberSelectionStartPoint = Point.Empty;
            _gridRubberSelectionRect = Rectangle.Empty;

            if (dgvJobs.Capture)
                dgvJobs.Capture = false;

            if (!oldRect.IsEmpty)
                dgvJobs.Invalidate(Rectangle.Inflate(oldRect, 2, 2));

            if (!hadActiveRubberSelection || !applySelectionSync)
                return;

            SyncTilesSelectionWithGrid();
            UpdateActionButtonsState();
            UpdateTrayStatsIndicator();
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
