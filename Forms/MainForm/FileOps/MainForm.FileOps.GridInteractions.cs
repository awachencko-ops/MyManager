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

namespace Replica
{
    public partial class MainForm
    {
        private bool IsGroupOrderContainerRow(string? rowTag, OrderData? order)
        {
            return order != null
                && IsOrderTag(rowTag)
                && OrderTopologyService.IsMultiOrder(order);
        }

        private bool IsGroupContainerFileStageLocked(OrderData? order, int stage)
        {
            if (order == null)
                return false;

            if (!OrderTopologyService.IsMultiOrder(order))
                return false;

            return stage is OrderStages.Source or OrderStages.Prepared or OrderStages.Print;
        }

        private bool IsGroupContainerLockedColumn(int columnIndex)
        {
            return columnIndex == colSource.Index
                || columnIndex == colPrep.Index
                || columnIndex == colPrint.Index;
        }

        private OrderData? ResolveOrderFromRowTag(string? rowTag, int rowIndex)
        {
            if (IsOrderTag(rowTag))
                return GetOrderByRowIndex(rowIndex);

            var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return null;

            return FindOrderByInternalId(orderInternalId);
        }

        private bool ShouldToggleGroupOrderOnCellClick(int rowIndex, int columnIndex)
        {
            if (_gridMouseDownRowIndex != rowIndex || !_gridMouseDownRowWasSelected)
                return false;

            if (columnIndex == colPitstop.Index || columnIndex == colHotimposing.Index)
                return false;

            return true;
        }

        private void ResetGridClickState()
        {
            _gridMouseDownRowIndex = -1;
            _gridMouseDownRowWasSelected = false;
        }

        private void DgvJobs_MouseDown(object? sender, MouseEventArgs e)
        {
            StopGridHoverActivation();
            ResetGridClickState();

            _dragBoxFromMouseDown = Rectangle.Empty;
            _dragSourceRowIndex = -1;
            _dragSourceColumnIndex = -1;

            if (IsGridInputOverOrdersViewScrollBar())
            {
                dgvJobs.Cursor = Cursors.Default;
                ClearGridHoverVisual();
                return;
            }

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
            var clickedOrder = GetOrderByRowIndex(hit.RowIndex);
            _gridMouseDownRowIndex = hit.RowIndex;
            _gridMouseDownRowWasSelected =
                clickedRow.Selected
                && dgvJobs.CurrentCell != null
                && dgvJobs.CurrentCell.RowIndex == hit.RowIndex;

            var stage = GetStageByColumnIndex(hit.ColumnIndex);
            if (stage == 0)
                return;

            if (IsGroupOrderContainerRow(clickedRowTag, clickedOrder) && IsGroupContainerFileStageLocked(clickedOrder, stage))
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
            if (IsGridInputOverOrdersViewScrollBar())
            {
                StopGridHoverActivation();
                dgvJobs.Cursor = Cursors.Default;
                return;
            }

            if ((e.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                StopGridHoverActivation();
                CollapseDragSelectionToSingleRow(e.X, e.Y);
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
            if (IsGridInputOverOrdersViewScrollBar())
                return;

            if (e.Button == MouseButtons.Left)
                CollapseDragSelectionToSingleRow(e.X, e.Y);
        }

        private void CollapseDragSelectionToSingleRow(int mouseX, int mouseY)
        {
            if (dgvJobs.SelectedRows.Count <= 1)
                return;

            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
                return;

            var hit = dgvJobs.HitTest(mouseX, mouseY);
            var rowIndex = hit.RowIndex >= 0 ? hit.RowIndex : dgvJobs.CurrentCell?.RowIndex ?? -1;
            if (rowIndex < 0 || rowIndex >= dgvJobs.Rows.Count)
                return;

            var row = dgvJobs.Rows[rowIndex];
            if (row.IsNewRow)
                return;

            var columnIndex = hit.ColumnIndex >= 0 ? hit.ColumnIndex : dgvJobs.CurrentCell?.ColumnIndex ?? colStatus.Index;
            if (columnIndex < 0 || columnIndex >= dgvJobs.Columns.Count || columnIndex >= row.Cells.Count)
                columnIndex = colStatus.Index >= 0 && colStatus.Index < row.Cells.Count ? colStatus.Index : 0;

            var targetCell = row.Cells[columnIndex];
            if (dgvJobs.CurrentCell != targetCell || !row.Selected || dgvJobs.SelectedRows.Count > 1)
            {
                dgvJobs.ClearSelection();
                dgvJobs.CurrentCell = targetCell;
                row.Selected = true;
            }
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
                if (TryPaintStatusCell(e))
                {
                    e.Handled = true;
                    return;
                }

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
                using var backBrush = new SolidBrush(OrdersRowBaseBackColor);
                graphics.FillRectangle(backBrush, e.CellBounds);
                e.Paint(
                    e.CellBounds,
                    DataGridViewPaintParts.ContentForeground |
                    DataGridViewPaintParts.ErrorIcon |
                    DataGridViewPaintParts.Focus);
            }

            // Draw header separators with the same subtle color as table gridlines.
            using var gridPen = new Pen(OrdersGridLineColor);
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
            var rowTag = row.Tag?.ToString();
            var order = ResolveOrderFromRowTag(rowTag, e.RowIndex);
            var isGroupContainerRow = IsGroupOrderContainerRow(rowTag, order);
            var isGroupItemRow = IsItemTag(rowTag) && order != null && OrderTopologyService.IsMultiOrder(order);
            var isLockedGroupContainerCell = IsGroupOrderContainerRow(rowTag, order)
                && IsGroupContainerLockedColumn(e.ColumnIndex);
            var rowBackColor = row.Selected
                ? OrdersRowSelectedBackColor
                : (e.RowIndex == _hoveredRowIndex
                    ? OrdersRowHoverBackColor
                    : (e.RowIndex % 2 == 0 ? OrdersRowBaseBackColor : OrdersRowZebraBackColor));
            if (isGroupContainerRow)
            {
                rowBackColor = row.Selected
                    ? GroupOrderRowSelectedBackColor
                    : (e.RowIndex == _hoveredRowIndex ? GroupOrderRowHoverBackColor : GroupOrderRowBackColor);
            }
            else if (isGroupItemRow)
            {
                rowBackColor = row.Selected
                    ? GroupOrderItemRowSelectedBackColor
                    : (e.RowIndex == _hoveredRowIndex
                        ? GroupOrderItemRowHoverBackColor
                        : (e.RowIndex % 2 == 0 ? GroupOrderItemRowBaseBackColor : GroupOrderItemRowZebraBackColor));
            }
            var isAttachmentColumn = e.ColumnIndex == colSource.Index
                || e.ColumnIndex == colPrep.Index
                || e.ColumnIndex == colPrint.Index;
            var textValue = e.Value?.ToString();
            var hasAttachmentText = isAttachmentColumn
                && !string.IsNullOrWhiteSpace(textValue)
                && !string.Equals(textValue, "-", StringComparison.Ordinal)
                && !string.Equals(textValue, "...", StringComparison.Ordinal);
            var foreColor = hasAttachmentText ? OrdersLinkTextColor : Color.Black;
            if (isLockedGroupContainerCell)
                foreColor = Color.FromArgb(128, 128, 128);

            if (e.CellStyle == null)
                return;

            e.CellStyle.BackColor = rowBackColor;
            e.CellStyle.SelectionBackColor = isGroupContainerRow
                ? GroupOrderRowSelectedBackColor
                : (isGroupItemRow ? GroupOrderItemRowSelectedBackColor : OrdersRowSelectedBackColor);
            e.CellStyle.ForeColor = foreColor;
            e.CellStyle.SelectionForeColor = foreColor;
        }

        private void DgvJobs_CellMouseEnter(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            if (IsGridInputOverOrdersViewScrollBar())
            {
                dgvJobs.Cursor = Cursors.Default;
                StopGridHoverActivation();
                ClearGridHoverVisual();
                return;
            }

            var rowTag = dgvJobs.Rows[e.RowIndex].Tag?.ToString();
            var order = ResolveOrderFromRowTag(rowTag, e.RowIndex);
            var stage = GetStageByColumnIndex(e.ColumnIndex);
            var isItemRow = IsItemTag(rowTag);
            var isOrderRow = IsOrderTag(rowTag);
            var isLockedGroupContainer = isOrderRow && IsGroupContainerFileStageLocked(order, stage);
            var canInteractWithFileCell =
                stage != OrderStages.None
                && (isOrderRow || isItemRow)
                && !isLockedGroupContainer;
            dgvJobs.Cursor = canInteractWithFileCell ? Cursors.Hand : Cursors.Default;

            if (!isOrderRow && !isItemRow)
            {
                StopGridHoverActivation();
                ClearGridHoverVisual();
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
            ClearGridHoverVisual();
        }

        private void DgvJobs_MouseLeave(object? sender, EventArgs e)
        {
            StopGridHoverActivation();
        }

        private void DgvJobs_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (IsGridInputOverOrdersViewScrollBar())
                return;

            if (e.RowIndex < 0)
                return;

            var columnName = dgvJobs.Columns[e.ColumnIndex].Name;
            if (string.Equals(columnName, OrderGridColumnNames.PitStop, StringComparison.Ordinal)
                || string.Equals(columnName, OrderGridColumnNames.PitStopLegacy, StringComparison.Ordinal))
            {
                SelectPitStopActionFromGrid(e.RowIndex);
                return;
            }

            if (string.Equals(columnName, OrderGridColumnNames.HotImposing, StringComparison.Ordinal)
                || string.Equals(columnName, OrderGridColumnNames.ImposingLegacy, StringComparison.Ordinal))
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

            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
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

            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
        }

        private void DgvJobs_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            if (!string.Equals(dgvJobs.Columns[e.ColumnIndex].Name, OrderGridColumnNames.Status, StringComparison.Ordinal))
                return;

            var rowTag = dgvJobs.Rows[e.RowIndex].Tag?.ToString();
            if (!IsOrderTag(rowTag))
                return;

            var order = GetOrderByRowIndex(e.RowIndex);
            if (order == null)
                return;

            var statusText = order.Status ?? string.Empty;
            if (!string.Equals(NormalizeStatus(statusText), WorkflowStatusNames.Error, StringComparison.Ordinal))
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

            if (IsGridInputOverOrdersViewScrollBar())
            {
                ResetGridClickState();
                return;
            }

            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                ResetGridClickState();
                return;
            }

            // Ctrl/Shift clicks are used for multi-selection (Explorer-like behavior).
            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
            {
                ResetGridClickState();
                return;
            }

            var stage = GetStageByColumnIndex(e.ColumnIndex);
            var rowTag = dgvJobs.Rows[e.RowIndex].Tag?.ToString();
            var order = ResolveOrderFromRowTag(rowTag, e.RowIndex);
            if (order == null || string.IsNullOrWhiteSpace(rowTag))
            {
                ResetGridClickState();
                return;
            }

            if (stage == 0)
            {
                if (IsOrderTag(rowTag)
                    && OrderTopologyService.IsMultiOrder(order)
                    && ShouldToggleGroupOrderOnCellClick(e.RowIndex, e.ColumnIndex))
                {
                    ToggleOrderExpanded(order.InternalId);
                }

                ResetGridClickState();
                return;
            }

            if (IsGroupOrderContainerRow(rowTag, order) && IsGroupContainerFileStageLocked(order, stage))
            {
                SetBottomStatus("В group-order у контейнера файлы заполняются только в строках item");
                ResetGridClickState();
                return;
            }

            try
            {
                if (IsItemTag(rowTag))
                {
                    var itemId = ExtractItemIdFromTag(rowTag);
                    var item = order.Items?.FirstOrDefault(x => x != null && string.Equals(x.ItemId, itemId, StringComparison.Ordinal));
                    if (item == null)
                        return;

                    var itemPath = GetItemStagePath(item, stage);
                    if (HasExistingFile(itemPath))
                    {
                        OpenFileDefault(itemPath);
                        return;
                    }

                    await PickAndCopyFileForItemAsync(order, item, stage);
                    return;
                }

                if (!IsOrderTag(rowTag))
                    return;

                var orderPath = ResolveSingleOrderDisplayPath(order, stage);
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
            finally
            {
                ResetGridClickState();
            }
        }

        private void SetGridHoverActivationCandidate(int rowIndex)
        {
            if (IsGridInputOverOrdersViewScrollBar())
            {
                StopGridHoverActivation();
                return;
            }

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

            if (IsGridInputOverOrdersViewScrollBar())
                return;

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

        private void ClearGridHoverVisual()
        {
            if (_hoveredRowIndex == -1)
                return;

            var oldIndex = _hoveredRowIndex;
            _hoveredRowIndex = -1;
            if (oldIndex >= 0 && oldIndex < dgvJobs.Rows.Count)
                dgvJobs.InvalidateRow(oldIndex);
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

            if (IsGroupOrderContainerRow(rowTag, order) && IsGroupContainerFileStageLocked(order, stage))
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

                if (IsGroupOrderContainerRow(rowTag, order) && IsGroupContainerFileStageLocked(order, stage))
                {
                    SetBottomStatus("В group-order добавляйте файлы в строки item");
                    return;
                }

                if (!await AddFileToOrderAsync(order, sourceFile, stage))
                    return;

                PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось добавить файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось добавить файл: {ex.Message}", "Drag&Drop", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

    }
}
