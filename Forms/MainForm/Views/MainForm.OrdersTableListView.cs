using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BrightIdeasSoftware;

namespace MyManager
{
    public partial class MainForm
    {
        private sealed class OrdersTableRowModel
        {
            public string OrderInternalId { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public string OrderNumber { get; init; } = string.Empty;
            public string TaskTitle { get; init; } = string.Empty;
            public string FileCheck { get; init; } = string.Empty;
            public string StripRun { get; init; } = string.Empty;
            public string ReadyToPrint { get; init; } = string.Empty;
            public string ReceivedDate { get; init; } = string.Empty;
            public string PrepressDate { get; init; } = string.Empty;
        }

        private void InitializeOrdersTableListView()
        {
            _olvOrdersTable.Dock = DockStyle.Fill;
            _olvOrdersTable.Margin = dgvJobs.Margin;
            _olvOrdersTable.BackColor = dgvJobs.BackgroundColor;
            _olvOrdersTable.BorderStyle = BorderStyle.None;
            _olvOrdersTable.View = View.Details;
            _olvOrdersTable.FullRowSelect = true;
            _olvOrdersTable.MultiSelect = true;
            _olvOrdersTable.HideSelection = false;
            _olvOrdersTable.ShowGroups = false;
            _olvOrdersTable.CellEditActivation = ObjectListView.CellEditActivateMode.None;
            _olvOrdersTable.UseFiltering = false;
            _olvOrdersTable.UseAlternatingBackColors = false;
            _olvOrdersTable.UseExplorerTheme = true;
            _olvOrdersTable.GridLines = true;
            _olvOrdersTable.RowHeight = 34;
            _olvOrdersTable.SelectedBackColor = OrdersRowSelectedBackColor;
            _olvOrdersTable.SelectedForeColor = Color.Black;
            _olvOrdersTable.UnfocusedSelectedBackColor = OrdersRowSelectedBackColor;
            _olvOrdersTable.UnfocusedSelectedForeColor = Color.Black;
            _olvOrdersTable.Visible = false;

            _olvOrdersTable.AllColumns.Clear();
            _olvOrdersTable.Columns.Clear();

            var columns = new[]
            {
                CreateOrdersTableColumn("Состояние", nameof(OrdersTableRowModel.Status), 190),
                CreateOrdersTableColumn("№ заказа", nameof(OrdersTableRowModel.OrderNumber), 150),
                CreateOrdersTableColumn("Заголовок задания", nameof(OrdersTableRowModel.TaskTitle), 240),
                CreateOrdersTableColumn("Проверка файлов", nameof(OrdersTableRowModel.FileCheck), 210),
                CreateOrdersTableColumn("Спуск полос", nameof(OrdersTableRowModel.StripRun), 210),
                CreateOrdersTableColumn("Готов к печати", nameof(OrdersTableRowModel.ReadyToPrint), 180),
                CreateOrdersTableColumn("Заказ принят", nameof(OrdersTableRowModel.ReceivedDate), 160),
                CreateOrdersTableColumn("В препрессе", nameof(OrdersTableRowModel.PrepressDate), 160)
            };

            columns[^1].FillsFreeSpace = true;
            foreach (var column in columns)
                _olvOrdersTable.AllColumns.Add(column);

            _olvOrdersTable.RebuildColumns();
            _olvOrdersTable.SelectedIndexChanged += OlvOrdersTable_SelectedIndexChanged;
            _olvOrdersTable.MouseDown += OlvOrdersTable_MouseDown;

            tableLayoutPanel1.Controls.Add(_olvOrdersTable, 0, 2);
            _olvOrdersTable.BringToFront();
        }

        private static OLVColumn CreateOrdersTableColumn(string title, string aspectName, int width)
        {
            return new OLVColumn(title, aspectName)
            {
                Width = width,
                IsEditable = false,
                FillsFreeSpace = false,
                TextAlign = HorizontalAlignment.Left
            };
        }

        private void OlvOrdersTable_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
                return;

            var hit = _olvOrdersTable.HitTest(e.Location);
            if (hit.Item != null)
                return;

            _isSyncingOrdersTableSelection = true;
            try
            {
                _olvOrdersTable.DeselectAll();
                _olvOrdersTable.FocusedObject = null;
            }
            finally
            {
                _isSyncingOrdersTableSelection = false;
            }

            ClearGridSelection();
            SyncTilesSelectionWithGrid();
            UpdateActionButtonsState();
            UpdateTrayStatsIndicator();
        }

        private void OlvOrdersTable_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isSyncingOrdersTableSelection)
                return;

            var selectedOrderIds = GetSelectedOrderInternalIdsFromOrdersTable();
            var preferredOrderInternalId = GetFocusedOrderInternalIdFromOrdersTable();
            if (string.IsNullOrWhiteSpace(preferredOrderInternalId))
                preferredOrderInternalId = selectedOrderIds.FirstOrDefault();

            ApplyGridSelectionByOrderInternalIds(selectedOrderIds, preferredOrderInternalId);
            SyncTilesSelectionWithGrid();
            UpdateActionButtonsState();
            UpdateTrayStatsIndicator();
        }

        private void RefreshOrdersTableFromVisibleRows()
        {
            var selectedOrderInternalIds = _ordersViewMode == OrdersViewMode.List
                ? GetSelectedOrderInternalIdsFromOrdersTable()
                : GetSelectedOrderInternalIdsFromGrid();

            if (selectedOrderInternalIds.Count == 0)
                selectedOrderInternalIds = GetSelectedOrderInternalIdsFromGrid();

            var preferredOrderInternalId = GetFocusedOrderInternalIdFromOrdersTable();
            if (string.IsNullOrWhiteSpace(preferredOrderInternalId))
                preferredOrderInternalId = ExtractOrderInternalIdFromTag(dgvJobs.CurrentRow?.Tag?.ToString());

            var models = new List<OrdersTableRowModel>();
            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow || !row.Visible)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (!IsOrderTag(rowTag))
                    continue;

                var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
                if (string.IsNullOrWhiteSpace(orderInternalId))
                    continue;

                models.Add(new OrdersTableRowModel
                {
                    OrderInternalId = orderInternalId,
                    Status = row.Cells[colStatus.Index].Value?.ToString() ?? string.Empty,
                    OrderNumber = row.Cells[colOrderNumber.Index].Value?.ToString() ?? string.Empty,
                    TaskTitle = row.Cells[colPrep.Index].Value?.ToString() ?? string.Empty,
                    FileCheck = row.Cells[colPitstop.Index].Value?.ToString() ?? string.Empty,
                    StripRun = row.Cells[colHotimposing.Index].Value?.ToString() ?? string.Empty,
                    ReadyToPrint = row.Cells[colPrint.Index].Value?.ToString() ?? string.Empty,
                    ReceivedDate = row.Cells[colReceived.Index].Value?.ToString() ?? string.Empty,
                    PrepressDate = row.Cells[colCreated.Index].Value?.ToString() ?? string.Empty
                });
            }

            _isSyncingOrdersTableSelection = true;
            _olvOrdersTable.BeginUpdate();
            try
            {
                _olvOrdersTable.SetObjects(models, preserveState: false);
            }
            finally
            {
                _olvOrdersTable.EndUpdate();
                _isSyncingOrdersTableSelection = false;
            }

            ApplyOrdersTableSelectionByOrderInternalIds(
                selectedOrderInternalIds,
                preferredOrderInternalId,
                ensureVisible: _ordersViewMode == OrdersViewMode.List);
        }

        private HashSet<string> GetSelectedOrderInternalIdsFromOrdersTable()
        {
            var selectedOrderIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var selectedObject in _olvOrdersTable.SelectedObjects)
            {
                if (selectedObject is not OrdersTableRowModel model)
                    continue;

                if (!string.IsNullOrWhiteSpace(model.OrderInternalId))
                    selectedOrderIds.Add(model.OrderInternalId);
            }

            return selectedOrderIds;
        }

        private string? GetFocusedOrderInternalIdFromOrdersTable()
        {
            return _olvOrdersTable.FocusedObject is OrdersTableRowModel model
                ? model.OrderInternalId
                : null;
        }

        private void SyncOrdersTableSelectionWithGrid()
        {
            if (_isSyncingOrdersTableSelection)
                return;

            var selectedOrderIds = GetSelectedOrderInternalIdsFromGrid();
            var preferredOrderInternalId = ExtractOrderInternalIdFromTag(dgvJobs.CurrentRow?.Tag?.ToString());
            ApplyOrdersTableSelectionByOrderInternalIds(
                selectedOrderIds,
                preferredOrderInternalId,
                ensureVisible: _ordersViewMode == OrdersViewMode.List);
        }

        private void ApplyOrdersTableSelectionByOrderInternalIds(
            ISet<string> selectedOrderInternalIds,
            string? preferredOrderInternalId,
            bool ensureVisible)
        {
            _isSyncingOrdersTableSelection = true;
            try
            {
                _olvOrdersTable.DeselectAll();
                _olvOrdersTable.FocusedObject = null;

                if (selectedOrderInternalIds.Count == 0)
                    return;

                var modelsToSelect = new List<object>();
                OrdersTableRowModel? firstSelectedModel = null;
                OrdersTableRowModel? preferredSelectedModel = null;

                foreach (var rowObject in _olvOrdersTable.Objects)
                {
                    if (rowObject is not OrdersTableRowModel model)
                        continue;

                    if (!selectedOrderInternalIds.Contains(model.OrderInternalId))
                        continue;

                    modelsToSelect.Add(model);
                    firstSelectedModel ??= model;

                    if (!string.IsNullOrWhiteSpace(preferredOrderInternalId)
                        && string.Equals(model.OrderInternalId, preferredOrderInternalId, StringComparison.Ordinal))
                    {
                        preferredSelectedModel = model;
                    }
                }

                _olvOrdersTable.SelectObjects(modelsToSelect);
                var modelToFocus = preferredSelectedModel ?? firstSelectedModel;
                if (modelToFocus == null)
                    return;

                _olvOrdersTable.FocusedObject = modelToFocus;
                if (ensureVisible)
                    _olvOrdersTable.EnsureModelVisible(modelToFocus);
            }
            finally
            {
                _isSyncingOrdersTableSelection = false;
            }
        }
    }
}
