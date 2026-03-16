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
        private void InitializeOrderRowContextMenu()
        {
            _gridMenu.OpenFolder = (stage) =>
            {
                if (TryGetContextOrderItem(out var itemOrder, out var item))
                {
                    OpenOrderStageFolder(itemOrder!, item!, stage);
                    return;
                }

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
                    OpenOrderLogForOrderOnly(GetContextOrder());
            };

            dgvJobs.CellMouseDown += DgvJobs_CellMouseDown;
        }

        private void DgvJobs_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var row = dgvJobs.Rows[e.RowIndex];
            var rowTag = row.Tag?.ToString();
            if (!IsOrderTag(rowTag) && !IsItemTag(rowTag))
                return;

            _ctxRow = e.RowIndex;
            _ctxCol = e.ColumnIndex;
            if (!TrySelectContextRow())
                return;

            var order = GetContextOrder();
            if (order == null)
                return;

            if (IsOrderTag(rowTag) && OrderTopologyService.IsMultiOrder(order))
            {
                ShowGroupOrderContextMenu(order);
                return;
            }

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
            if (!row.Selected)
                dgvJobs.ClearSelection();

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
            if (_ctxRow < 0 || _ctxRow >= dgvJobs.Rows.Count)
                return null;

            var rowTag = dgvJobs.Rows[_ctxRow].Tag?.ToString();
            var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return null;

            return FindOrderByInternalId(orderInternalId);
        }

        private bool TryGetContextOrderItem(out OrderData? order, out OrderFileItem? item)
        {
            order = null;
            item = null;

            if (_ctxRow < 0 || _ctxRow >= dgvJobs.Rows.Count)
                return false;

            var rowTag = dgvJobs.Rows[_ctxRow].Tag?.ToString();
            if (!IsItemTag(rowTag))
                return false;

            var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
            var itemId = ExtractItemIdFromTag(rowTag);
            if (string.IsNullOrWhiteSpace(orderInternalId) || string.IsNullOrWhiteSpace(itemId))
                return false;

            order = FindOrderByInternalId(orderInternalId);
            if (order?.Items == null)
                return false;

            item = order.Items.FirstOrDefault(x => x != null && string.Equals(x.ItemId, itemId, StringComparison.Ordinal));
            return item != null;
        }

        private void OpenOrderStageFolder(OrderData order, OrderFileItem item, int stage)
        {
            if (order == null || item == null)
                return;

            string targetPath;
            if (OrderStages.IsFileStage(stage))
            {
                var stageFilePath = GetItemStagePath(item, stage);
                if (HasExistingFile(stageFilePath))
                    targetPath = Path.GetDirectoryName(stageFilePath) ?? GetStageFolder(order, stage);
                else
                    targetPath = GetStageFolder(order, stage);
            }
            else
            {
                targetPath = GetPreferredOrderFolder(order);
            }

            OpenOrderFolderPath(targetPath);
        }

        private void ShowGroupOrderContextMenu(OrderData order)
        {
            _groupOrderContextMenu.Items.Clear();
            _groupOrderContextMenu.ShowItemToolTips = true;

            var isExpanded = _expandedOrderIds.Contains(order.InternalId);
            var expandCaption = isExpanded ? "Свернуть" : "Развернуть";
            AddGroupOrderMenuItem(expandCaption, () => ToggleOrderExpanded(order.InternalId));

            var hasCommonFolder = TryGetBrowseFolderPathForOrder(order, out var commonFolderPath, out var folderReason);
            AddGroupOrderMenuItem(
                "Открыть папку",
                hasCommonFolder ? () => OpenOrderFolderPath(commonFolderPath) : null,
                hasCommonFolder,
                folderReason);

            AddGroupOrderMenuItem("Открыть лог заказа", () => OpenOrderLogForOrderOnly(order));

            if (_groupOrderContextMenu.Items.Count == 0)
                return;

            _groupOrderContextMenu.Show(Cursor.Position);
        }

        private void AddGroupOrderMenuItem(string text, Action? action, bool enabled = true, string? toolTipText = null)
        {
            var menuItem = new ToolStripMenuItem(text)
            {
                Enabled = enabled
            };

            if (!string.IsNullOrWhiteSpace(toolTipText))
                menuItem.ToolTipText = toolTipText;

            if (enabled && action != null)
                menuItem.Click += (_, _) => action();

            _groupOrderContextMenu.Items.Add(menuItem);
        }

        private void OpenOrderLogForOrderOnly(OrderData? order)
        {
            if (order == null)
                return;

            AcknowledgeErrorNotifications();
            var orderLogPath = GetOrderLogFilePath(order);
            if (!File.Exists(orderLogPath))
            {
                SetBottomStatus("Лог заказа пока не создан");
                MessageBox.Show(this, "Лог заказа пока не создан.", "Лог", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var viewer = new OrderLogViewerForm(orderLogPath, GetOrderDisplayId(order));
            viewer.ShowDialog(this);
        }

        private async Task PickFileFromContextAsync(int stage)
        {
            if (!OrderStages.IsFileStage(stage))
                return;

            try
            {
                if (TryGetContextOrderItem(out var itemOrder, out var item))
                {
                    await PickAndCopyFileForItemAsync(itemOrder!, item!, stage);
                    return;
                }

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
            if (!OrderStages.IsFileStage(stage))
                return;

            if (TryGetContextOrderItem(out var itemOrder, out var item))
            {
                RemoveFileFromItem(itemOrder!, item!, stage);
                return;
            }

            var order = GetContextOrder();
            if (order == null)
                return;

            RemoveFileFromOrder(order, stage);
        }

        private void RenameFileFromContext(int stage)
        {
            if (!OrderStages.IsFileStage(stage))
                return;

            if (TryGetContextOrderItem(out var itemOrder, out var item))
            {
                RenameFileForItem(itemOrder!, item!, stage);
                return;
            }

            var order = GetContextOrder();
            if (order == null)
                return;

            RenameFileForOrder(order, stage);
        }

        private void CopyPathFromContextToClipboard(int stage)
        {
            if (!OrderStages.IsFileStage(stage))
                return;

            if (TryGetContextOrderItem(out _, out var item))
            {
                CopyPathToClipboard(item!, stage);
                return;
            }

            var order = GetContextOrder();
            if (order == null)
                return;

            CopyPathToClipboard(order, stage);
        }

        private async Task PastePathFromClipboardToContextAsync(int stage)
        {
            if (!OrderStages.IsFileStage(stage))
                return;

            try
            {
                if (TryGetContextOrderItem(out var itemOrder, out var item))
                {
                    await PasteFileFromClipboardAsync(itemOrder!, item!, stage);
                    return;
                }

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
            if (TryGetContextOrderItem(out var itemOrder, out var item))
            {
                ProcessWatermark(itemOrder!, item!, isVertical);
                return;
            }

            var order = GetContextOrder();
            if (order == null)
                return;

            ProcessWatermark(order, isVertical);
        }

        private async void CopyPrintFromContextToGrandpa()
        {
            try
            {
                if (TryGetContextOrderItem(out var itemOrder, out var item))
                {
                    await CopyToGrandpaAsync(itemOrder!, item!);
                    return;
                }

                var order = GetContextOrder();
                if (order == null)
                    return;

                await CopyToGrandpaAsync(order);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Ошибка копирования: {ex.Message}");
                MessageBox.Show(this, $"Не удалось скопировать файл: {ex.Message}", "Копирование", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemovePitStopActionFromContext()
        {
            if (TryGetContextOrderItem(out var itemOrder, out var item))
            {
                RemovePitStopAction(itemOrder!, item!);
                return;
            }

            var order = GetContextOrder();
            if (order == null)
                return;

            RemovePitStopAction(order);
        }

        private void RemoveImposingActionFromContext()
        {
            if (TryGetContextOrderItem(out var itemOrder, out var item))
            {
                RemoveImposingAction(itemOrder!, item!);
                return;
            }

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
                var printPath = ResolveSingleOrderDisplayPath(order, 3);
                if (!HasExistingFile(printPath))
                {
                    SetBottomStatus("Файл печати не найден");
                    MessageBox.Show(this, "Файл печати не найден.", "Водяной знак", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var originalPrintPath = order.PrintPath;
                try
                {
                    order.PrintPath = printPath;
                    PdfWatermark.Apply(order, isVertical);
                }
                finally
                {
                    order.PrintPath = originalPrintPath;
                }
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

            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
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

            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
            SetBottomStatus($"Imposing очищен для {GetOrderDisplayId(order)}");
        }

        private void RemovePitStopAction(OrderData order, OrderFileItem item)
        {
            item.PitStopAction = "-";
            PersistGridChanges(OrderGridLogic.BuildItemTag(order.InternalId, item.ItemId));
            SetBottomStatus($"PitStop очищен для item {item.ClientFileLabel}");
        }

        private void RemoveImposingAction(OrderData order, OrderFileItem item)
        {
            item.ImposingAction = "-";
            PersistGridChanges(OrderGridLogic.BuildItemTag(order.InternalId, item.ItemId));
            SetBottomStatus($"Imposing очищен для item {item.ClientFileLabel}");
        }

        private async Task<string> CopyToGrandpaAsync(OrderData order)
        {
            var sourcePath = ResolveSingleOrderDisplayPath(order, 3);
            if (!HasExistingFile(sourcePath))
            {
                SetBottomStatus("Файл печати не найден");
                MessageBox.Show(this, "Файл печати не найден.", "Копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return string.Empty;
            }

            var targetName = Path.GetFileName(sourcePath);
            return await CopyToGrandpaFromSourceAsync(sourcePath, targetName);
        }

        private async Task<string> CopyToGrandpaAsync(OrderData order, OrderFileItem item)
        {
            if (!HasExistingFile(item.PrintPath))
            {
                SetBottomStatus("Файл печати item не найден");
                MessageBox.Show(this, "Файл печати item не найден.", "Копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return string.Empty;
            }

            var sourcePath = item.PrintPath ?? string.Empty;
            var targetName = Path.GetFileName(sourcePath);
            return await CopyToGrandpaFromSourceAsync(sourcePath, targetName);
        }

    }
}
