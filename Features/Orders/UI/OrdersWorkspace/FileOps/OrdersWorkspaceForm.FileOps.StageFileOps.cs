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
    public partial class OrdersWorkspaceForm
    {
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

            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
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

            PersistGridChanges(OrderGridLogic.BuildItemTag(order.InternalId, item.ItemId));
            SetBottomStatus("Файл добавлен в item");
        }

        private bool TryGetSelectedOrderContainer(out OrderData? order)
        {
            order = null;

            var currentRow = dgvJobs.CurrentRow;
            if (currentRow != null && !currentRow.IsNewRow)
            {
                var currentTag = currentRow.Tag?.ToString();
                if (IsOrderTag(currentTag))
                {
                    order = FindOrderByInternalId(ExtractOrderInternalIdFromTag(currentTag));
                    if (order != null)
                        return true;
                }
            }

            foreach (var selectedRow in dgvJobs.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(x => !x.IsNewRow)
                .OrderBy(x => x.Index))
            {
                var rowTag = selectedRow.Tag?.ToString();
                if (!IsOrderTag(rowTag))
                    continue;

                order = FindOrderByInternalId(ExtractOrderInternalIdFromTag(rowTag));
                if (order != null)
                    return true;
            }

            return false;
        }

        private async Task AddFileToSelectedOrderAsync()
        {
            if (!EnsureServerWriteAllowed("Добавление файла"))
                return;

            if (!TryGetSelectedOrderContainer(out var order) || order == null)
            {
                SetBottomStatus("Выберите строку заказа");
                MessageBox.Show(this, "Выберите строку заказа (single-order или group-order).", "Добавление файла", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!HasAtLeastOneOrderFile(order))
            {
                SetBottomStatus("Сначала добавьте первый файл в строку заказа");
                MessageBox.Show(
                    this,
                    "Сначала добавьте первый файл через строку заказа (ячейка Источник или drag-and-drop). После этого станет доступно добавление в группу.",
                    "Добавление в группу",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var sourceStageFolder = GetStageFolder(order, OrderStages.Source);
            Directory.CreateDirectory(sourceStageFolder);

            using var ofd = new OpenFileDialog
            {
                Filter = "PDF|*.pdf|Все файлы|*.*",
                InitialDirectory = sourceStageFolder,
                RestoreDirectory = false
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            var addPreparation = _orderApplicationService.PrepareAddItem(
                order,
                ofd.FileName,
                NormalizeAction(order.PitStopAction),
                NormalizeAction(order.ImposingAction));

            LogTopologyIssues(
                order,
                addPreparation.TopologyChangedBeforeAdd,
                addPreparation.TopologyIssuesBeforeAdd);

            var sourcePath = addPreparation.SourcePath;
            var newItem = addPreparation.Item;
            var addSucceeded = false;
            try
            {
                addSucceeded = await AddFileToItemAsync(order, newItem, sourcePath, OrderStages.Source);
            }
            catch (Exception ex)
            {
                _orderApplicationService.RollbackPreparedItem(order, newItem);
                SetBottomStatus($"Не удалось добавить файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось добавить файл: {ex.Message}", "Добавление файла", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!addSucceeded)
            {
                _orderApplicationService.RollbackPreparedItem(order, newItem);
                SetBottomStatus("Файл не добавлен");
                return;
            }

            var demotedToSingleOrderAfterAdd = NormalizeOrderTopologyAfterItemMutation(
                order,
                addPreparation.WasMultiOrderBeforeMutation,
                $"add-item: {Path.GetFileName(sourcePath)}");
            var isMultiOrderNow = OrderTopologyService.IsMultiOrder(order);
            if (isMultiOrderNow)
                _expandedOrderIds.Add(order.InternalId);

            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));

            var addedFileName = Path.GetFileName(sourcePath);
            if (!demotedToSingleOrderAfterAdd && !addPreparation.WasMultiOrderBeforeMutation && isMultiOrderNow)
            {
                AppendOrderOperationLog(order, OrderOperationNames.Topology, $"single-order -> group-order | add-item: {addedFileName}");
                SetBottomStatus("Файл добавлен. single-order преобразован в group-order");
                return;
            }

            AppendOrderOperationLog(order, OrderOperationNames.AddItem, $"Добавлен файл: {addedFileName}");
            SetBottomStatus("Файл добавлен в заказ");
        }

        private async Task<bool> AddFileToOrderAsync(OrderData order, string sourceFile, int stage)
        {
            if (!EnsureServerWriteAllowed("Добавление файла"))
                return false;

            if (stage == OrderStages.Print && !await EnsureSimpleOrderInfoForPrintAsync(order))
                return false;

            if (!_orderApplicationService.TryPrepareOrderFileAdd(
                    order,
                    sourceFile,
                    stage,
                    (targetStage, fileName) => EnsureUniqueStageFileName(order, targetStage, fileName),
                    out var plan))
            {
                return false;
            }

            var previousPath = GetOrderStagePath(order, stage);
            var newPath = plan.UsePrintCopy
                ? await CopyPrintFileAsync(order, plan.CleanSourcePath, plan.TargetFileName)
                : await CopyIntoStageAsync(order, stage, plan.CleanSourcePath, plan.TargetFileName);

            if (plan.EnsureSourceCopy)
                await EnsureSourceCopyAsync(order, plan.CleanSourcePath);

            UpdateOrderFilePath(order, stage, newPath);
            AppendOrderOperationLog(
                order,
                OrderOperationNames.SetStageFile,
                $"scope=order | stage={GetStageLogKey(stage)} | src={Path.GetFileName(plan.CleanSourcePath)} | prev={Path.GetFileName(previousPath)} | now={Path.GetFileName(newPath)} | mode={(plan.UsePrintCopy ? "copy-print" : "copy-stage")}");
            return true;
        }

        private async Task<bool> AddFileToItemAsync(OrderData order, OrderFileItem item, string sourceFile, int stage)
        {
            if (!EnsureServerWriteAllowed("Добавление файла"))
                return false;

            if (stage == OrderStages.Print && !await EnsureSimpleOrderInfoForPrintAsync(order))
                return false;

            if (!_orderApplicationService.TryPrepareItemFileAdd(
                    order,
                    item,
                    sourceFile,
                    stage,
                    (targetStage, fileName) => EnsureUniqueStageFileName(order, targetStage, fileName),
                    cleanSource => BuildItemPrintFileName(order, item, cleanSource),
                    out var plan))
            {
                return false;
            }

            var previousPath = GetItemStagePath(item, stage);
            var newPath = plan.UsePrintCopy
                ? await CopyPrintFileAsync(order, plan.CleanSourcePath, plan.TargetFileName)
                : await CopyIntoStageAsync(order, stage, plan.CleanSourcePath, plan.TargetFileName);

            UpdateItemFilePath(order, item, stage, newPath);
            var itemLabel = string.IsNullOrWhiteSpace(item.ClientFileLabel) ? item.ItemId : item.ClientFileLabel;
            AppendOrderOperationLog(
                order,
                OrderOperationNames.SetStageFile,
                $"scope=item | item={itemLabel} | stage={GetStageLogKey(stage)} | src={Path.GetFileName(plan.CleanSourcePath)} | prev={Path.GetFileName(previousPath)} | now={Path.GetFileName(newPath)} | mode={(plan.UsePrintCopy ? "copy-print" : "copy-stage")}");
            TrySyncLanOrderItemUpsert(order, item, $"add-item-file-stage-{stage}");
            return true;
        }

        private void PersistGridChanges(string selectedTag)
        {
            ApplyPostGridMutationPlan(_orderApplicationService.BuildPostGridMutationUiPlan(), selectedTag);
        }

        private void ApplyPostGridMutationPlan(OrderGridMutationUiPlan plan, string selectedTag)
        {
            if (plan.ShouldSaveHistory)
                SaveHistory();

            if (plan.RefreshMode == OrderGridRefreshMode.FastRowsThenRebuild && TryRefreshGridRowsWithoutRebuild(selectedTag))
            {
                if (plan.ShouldUpdateActionButtons)
                    UpdateActionButtonsState();
                return;
            }

            if (plan.RefreshMode != OrderGridRefreshMode.None)
            {
                RebuildOrdersGrid();
                if (!string.IsNullOrWhiteSpace(selectedTag))
                    TryRestoreSelectedRowByTag(selectedTag);
            }

            if (plan.ShouldUpdateActionButtons)
                UpdateActionButtonsState();
        }

        private int GetStageByColumnIndex(int columnIndex)
        {
            var columnName = dgvJobs.Columns[columnIndex].Name;
            return OrderGridColumnNames.ResolveStage(columnName);
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
                OrderStages.Source => order.SourcePath ?? string.Empty,
                OrderStages.Prepared => order.PreparedPath ?? string.Empty,
                OrderStages.Print => order.PrintPath ?? string.Empty,
                _ => string.Empty
            };
        }

        private static string GetItemStagePath(OrderFileItem item, int stage)
        {
            return stage switch
            {
                OrderStages.Source => item.SourcePath ?? string.Empty,
                OrderStages.Prepared => item.PreparedPath ?? string.Empty,
                OrderStages.Print => item.PrintPath ?? string.Empty,
                _ => string.Empty
            };
        }

        private void RemoveFileFromOrder(OrderData order, int stage)
        {
            if (!EnsureServerWriteAllowed("Удаление файла"))
                return;

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

            var removedFileName = Path.GetFileName(currentPath);
            var statusUpdate = _orderApplicationService.ApplyOrderFileRemoved(order, stage);
            SetOrderStatus(
                order,
                statusUpdate.Status,
                OrderStatusSourceNames.FileSync,
                statusUpdate.Reason,
                persistHistory: false,
                rebuildGrid: false);
            TrySyncLanSingleOrderItemFromOrder(order, $"remove-order-file-stage-{stage}");
            AppendOrderOperationLog(
                order,
                OrderOperationNames.RemoveStageFile,
                $"scope=order | stage={GetStageLogKey(stage)} | removed={removedFileName}");
            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
            SetBottomStatus("Файл удален");
        }

        private void RemoveFileFromItem(OrderData order, OrderFileItem item, int stage)
        {
            if (!EnsureServerWriteAllowed("Удаление файла"))
                return;

            var currentPath = GetItemStagePath(item, stage);
            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            var wasMultiOrderBeforeMutation = OrderTopologyService.IsMultiOrder(order);

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

            var removedFileName = Path.GetFileName(currentPath);
            var itemLabel = string.IsNullOrWhiteSpace(item.ClientFileLabel) ? item.ItemId : item.ClientFileLabel;
            AppendOrderOperationLog(
                order,
                OrderOperationNames.RemoveStageFile,
                $"scope=item | item={itemLabel} | stage={GetStageLogKey(stage)} | removed={removedFileName}");
            var removeOutcome = _orderApplicationService.ApplyItemFileRemoved(
                order,
                item,
                stage,
                wasMultiOrderBeforeMutation);
            SetOrderStatus(
                order,
                removeOutcome.StatusUpdate.Status,
                OrderStatusSourceNames.FileSync,
                removeOutcome.StatusUpdate.Reason,
                persistHistory: false,
                rebuildGrid: false);

            if (removeOutcome.ItemRemovedFromOrder)
            {
                AppendOrderOperationLog(
                    order,
                    OrderOperationNames.RemoveItem,
                    $"Удален пустой item после удаления файла: {removedFileName} | stage-{stage}");
                TrySyncLanOrderItemDelete(order, item, $"remove-file-stage-{stage}");
            }
            else
            {
                TrySyncLanOrderItemUpsert(order, item, $"remove-file-stage-{stage}");
            }

            var demotedToSingleOrder = ApplyTopologyMutationResult(
                order,
                removeOutcome.TopologyMutation,
                $"remove-file: {removedFileName} | stage-{stage}");

            var selectionTag = removeOutcome.CanRestoreItemSelection
                ? OrderGridLogic.BuildItemTag(order.InternalId, item.ItemId)
                : OrderGridLogic.BuildOrderTag(order.InternalId);

            PersistGridChanges(selectionTag);
            if (demotedToSingleOrder)
            {
                SetBottomStatus("Файл удален. group-order преобразован в single-order");
                return;
            }

            if (removeOutcome.ItemRemovedFromOrder)
            {
                SetBottomStatus("Файл удален. item исключен из группы");
                return;
            }

            SetBottomStatus("Файл item удален");
        }

        private bool NormalizeOrderTopologyAfterItemMutation(OrderData order, bool wasMultiOrderBeforeMutation, string details)
        {
            if (order == null)
                return false;

            var result = _orderApplicationService.ApplyTopologyAfterItemMutation(order, wasMultiOrderBeforeMutation);
            return ApplyTopologyMutationResult(order, result, details);
        }

        private bool ApplyTopologyMutationResult(OrderData order, OrderItemTopologyMutationResult result, string details)
        {
            if (order == null || result == null)
                return false;

            LogTopologyIssues(order, result.Normalization.Changed, result.Normalization.Issues);
            if (!result.DemotedToSingleOrder)
                return false;

            _expandedOrderIds.Remove(order.InternalId);
            AppendOrderOperationLog(
                order,
                OrderOperationNames.Topology,
                $"group-order -> single-order | {details ?? string.Empty}");
            return true;
        }

        private bool ContainsOrderItem(OrderData order, string? itemId)
        {
            return _orderApplicationService.ContainsOrderItem(order, itemId);
        }

        private static bool RemoveItemIfEmpty(OrderData order, OrderFileItem item)
        {
            return new OrderItemMutationService().RemoveItemIfEmpty(order, item);
        }

        private void LogTopologyIssues(OrderData order, bool topologyChanged, IReadOnlyList<string> issues)
        {
            if (!topologyChanged || issues == null || issues.Count == 0)
                return;

            foreach (var issue in issues)
                Logger.Warn($"TOPOLOGY | order={GetOrderDisplayId(order)} | {issue}");
        }

        private void RenameFileForOrder(OrderData order, int stage)
        {
            if (!EnsureServerWriteAllowed("Переименование файла"))
                return;

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

            var statusUpdate = _orderApplicationService.ApplyOrderFileRenamed(order, stage, renamedPath);
            SetOrderStatus(
                order,
                statusUpdate.Status,
                OrderStatusSourceNames.FileSync,
                statusUpdate.Reason,
                persistHistory: false,
                rebuildGrid: false);
            TrySyncLanSingleOrderItemFromOrder(order, $"rename-order-file-stage-{stage}");
            AppendOrderOperationLog(
                order,
                OrderOperationNames.RenameStageFile,
                $"scope=order | stage={GetStageLogKey(stage)} | from={Path.GetFileName(currentPath)} | to={Path.GetFileName(renamedPath)}");
            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
            SetBottomStatus("Файл переименован");
        }

        private void RenameFileForItem(OrderData order, OrderFileItem item, int stage)
        {
            if (!EnsureServerWriteAllowed("Переименование файла"))
                return;

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

            var statusUpdate = _orderApplicationService.ApplyItemFileRenamed(order, item, stage, renamedPath);
            SetOrderStatus(
                order,
                statusUpdate.Status,
                OrderStatusSourceNames.FileSync,
                statusUpdate.Reason,
                persistHistory: false,
                rebuildGrid: false);
            var itemLabel = string.IsNullOrWhiteSpace(item.ClientFileLabel) ? item.ItemId : item.ClientFileLabel;
            AppendOrderOperationLog(
                order,
                OrderOperationNames.RenameStageFile,
                $"scope=item | item={itemLabel} | stage={GetStageLogKey(stage)} | from={Path.GetFileName(currentPath)} | to={Path.GetFileName(renamedPath)}");
            TrySyncLanOrderItemUpsert(order, item, $"rename-item-file-stage-{stage}");
            PersistGridChanges(OrderGridLogic.BuildItemTag(order.InternalId, item.ItemId));
            SetBottomStatus("Файл item переименован");
        }

        private bool TryBuildRenamedPath(string currentPath, out string renamedPath)
        {
            renamedPath = string.Empty;
            if (!HasExistingFile(currentPath))
                return false;

            var oldName = Path.GetFileNameWithoutExtension(currentPath);
            var nextName = ShowInputDialog("Переименование", "Введите новое имя файла:", oldName);
            var buildResult = _orderApplicationService.TryBuildRenamedPath(currentPath, nextName);
            if (buildResult.Status == RenamePathBuildStatus.TargetExists)
            {
                SetBottomStatus("Файл с таким именем уже существует");
                MessageBox.Show(this, "Файл с таким именем уже существует.", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (!buildResult.IsSuccess)
                return false;

            renamedPath = buildResult.RenamedPath;
            return !string.IsNullOrWhiteSpace(renamedPath);
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

        private void UpdateOrderFilePath(OrderData order, int stage, string path)
        {
            var statusUpdate = _orderApplicationService.ApplyOrderFilePath(order, stage, path);
            SetOrderStatus(
                order,
                statusUpdate.Status,
                OrderStatusSourceNames.FileSync,
                statusUpdate.Reason,
                persistHistory: false,
                rebuildGrid: false);
            TrySyncLanSingleOrderItemFromOrder(order, $"update-order-file-stage-{stage}");
        }

        private void TrySyncLanSingleOrderItemFromOrder(OrderData order, string reason)
        {
            if (!ShouldUseLanRunApi() || order == null || OrderTopologyService.IsMultiOrder(order))
                return;

            var primaryItem = EnsureSingleOrderPrimaryItemForLanSync(order);
            if (primaryItem == null || string.IsNullOrWhiteSpace(primaryItem.ItemId))
                return;

            TrySyncLanOrderItemUpsert(order, primaryItem, reason);
        }

        private OrderFileItem? EnsureSingleOrderPrimaryItemForLanSync(OrderData order)
        {
            var primaryItem = GetPrimaryItem(order);
            if (primaryItem != null && !string.IsNullOrWhiteSpace(primaryItem.ItemId))
            {
                MirrorSingleOrderOrderFieldsToPrimaryItem(order, primaryItem);
                return primaryItem;
            }

            var hasAnyOrderFiles =
                !string.IsNullOrWhiteSpace(order.SourcePath)
                || !string.IsNullOrWhiteSpace(order.PreparedPath)
                || !string.IsNullOrWhiteSpace(order.PrintPath);
            if (!hasAnyOrderFiles)
                return null;

            order.Items ??= new List<OrderFileItem>();
            if (order.Items.Count > 0)
                return GetPrimaryItem(order);

            var normalizedOrderStatus = NormalizeStatus(order.Status);
            if (string.IsNullOrWhiteSpace(normalizedOrderStatus))
                normalizedOrderStatus = ResolveWorkflowStatus(order.SourcePath, order.PreparedPath, order.PrintPath);

            var bootstrapItem = new OrderFileItem
            {
                ItemId = Guid.NewGuid().ToString("N"),
                StorageVersion = 0,
                SequenceNo = 0,
                ClientFileLabel = string.IsNullOrWhiteSpace(order.Id) ? string.Empty : order.Id.Trim(),
                Variant = string.Empty,
                SourcePath = order.SourcePath ?? string.Empty,
                SourceFileSizeBytes = order.SourceFileSizeBytes,
                SourceFileHash = order.SourceFileHash ?? string.Empty,
                PreparedPath = order.PreparedPath ?? string.Empty,
                PreparedFileSizeBytes = order.PreparedFileSizeBytes,
                PreparedFileHash = order.PreparedFileHash ?? string.Empty,
                PrintPath = order.PrintPath ?? string.Empty,
                PrintFileSizeBytes = order.PrintFileSizeBytes,
                PrintFileHash = order.PrintFileHash ?? string.Empty,
                FileStatus = normalizedOrderStatus,
                LastReason = order.LastStatusReason ?? string.Empty,
                UpdatedAt = DateTime.Now,
                PitStopAction = NormalizeAction(order.PitStopAction),
                ImposingAction = NormalizeAction(order.ImposingAction)
            };

            order.Items.Add(bootstrapItem);
            Logger.Info(
                $"LAN-API | single-order-item-bootstrap | order={GetOrderDisplayId(order)} | reason=missing-primary-item | src={Path.GetFileName(bootstrapItem.SourcePath)} | prep={Path.GetFileName(bootstrapItem.PreparedPath)} | print={Path.GetFileName(bootstrapItem.PrintPath)}");
            MirrorSingleOrderOrderFieldsToPrimaryItem(order, bootstrapItem);
            return bootstrapItem;
        }

        private static void MirrorSingleOrderOrderFieldsToPrimaryItem(OrderData order, OrderFileItem primaryItem)
        {
            if (order == null || primaryItem == null)
                return;

            primaryItem.SourcePath = order.SourcePath ?? string.Empty;
            primaryItem.SourceFileSizeBytes = order.SourceFileSizeBytes;
            primaryItem.SourceFileHash = order.SourceFileHash ?? string.Empty;
            primaryItem.PreparedPath = order.PreparedPath ?? string.Empty;
            primaryItem.PreparedFileSizeBytes = order.PreparedFileSizeBytes;
            primaryItem.PreparedFileHash = order.PreparedFileHash ?? string.Empty;
            primaryItem.PrintPath = order.PrintPath ?? string.Empty;
            primaryItem.PrintFileSizeBytes = order.PrintFileSizeBytes;
            primaryItem.PrintFileHash = order.PrintFileHash ?? string.Empty;
            primaryItem.PitStopAction = NormalizeAction(order.PitStopAction);
            primaryItem.ImposingAction = NormalizeAction(order.ImposingAction);
            primaryItem.FileStatus = string.IsNullOrWhiteSpace(order.Status)
                ? ResolveWorkflowStatus(order.SourcePath, order.PreparedPath, order.PrintPath)
                : order.Status.Trim();
            primaryItem.LastReason = order.LastStatusReason ?? string.Empty;
            primaryItem.UpdatedAt = DateTime.Now;
        }

        private void UpdateItemFilePath(OrderData order, OrderFileItem item, int stage, string path)
        {
            var statusUpdate = _orderApplicationService.ApplyItemFilePath(order, item, stage, path);
            SetOrderStatus(
                order,
                statusUpdate.Status,
                OrderStatusSourceNames.FileSync,
                statusUpdate.Reason,
                persistHistory: false,
                rebuildGrid: false);
        }

        private void RefreshOrderStatusFromItems(OrderData order)
        {
            var statusUpdate = _orderApplicationService.CalculateOrderStatusFromItems(order);
            SetOrderStatus(
                order,
                statusUpdate.Status,
                OrderStatusSourceNames.FileSync,
                statusUpdate.Reason,
                persistHistory: false,
                rebuildGrid: false);
        }

        private static string ResolveWorkflowStatus(string? sourcePath, string? preparedPath, string? printPath)
        {
            return OrderFilePathMutationService.ResolveWorkflowStatus(sourcePath, preparedPath, printPath);
        }

        private static string DescribeStageReason(int stage)
        {
            return OrderFilePathMutationService.DescribeStageReason(stage);
        }

        private string GetStageFolder(OrderData order, int stage)
        {
            if (stage == OrderStages.Print && HasExistingFile(order.PrintPath))
                return Path.GetDirectoryName(order.PrintPath) ?? GetTempStageFolder(stage);

            if (string.IsNullOrWhiteSpace(order.FolderName))
                return GetTempStageFolder(stage);

            var sub = stage switch
            {
                OrderStages.Source => "1. исходные",
                OrderStages.Prepared => "2. подготовка",
                OrderStages.Print => "3. печать",
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
                OrderStages.Source => "in",
                OrderStages.Prepared => "prepress",
                OrderStages.Print => "print",
                _ => string.Empty
            };

            var root = string.IsNullOrWhiteSpace(_tempRootPath)
                ? Path.Combine(_ordersRootPath, "_temp")
                : _tempRootPath;
            var path = Path.Combine(root, sub);
            Directory.CreateDirectory(path);
            return path;
        }

        private async Task<string> CopyIntoStageAsync(OrderData order, int stage, string sourceFile, string? targetName = null)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                throw new FileNotFoundException("Файл для копирования не найден.", cleanSource);

            var stageFolder = GetStageFolder(order, stage);
            Directory.CreateDirectory(stageFolder);

            var destinationFileName = string.IsNullOrWhiteSpace(targetName) ? Path.GetFileName(cleanSource) : targetName;
            var destination = Path.Combine(stageFolder, destinationFileName);

            if (PathsEqual(cleanSource, destination))
                return destination;

            if (File.Exists(destination))
                return destination;

            var stageName = GetStageDisplayName(stage);
            var sourceFileName = Path.GetFileName(cleanSource);
            await CopyFileWithTrayProgressAsync(
                cleanSource,
                destination,
                $"Копирование в {stageName}: {sourceFileName}");
            return destination;
        }

        private async Task<string> CopyPrintFileAsync(OrderData order, string sourceFile, string targetName)
        {
            if (!UsesOrderFolderStorage(order))
                return await CopyToGrandpaFromSourceAsync(sourceFile, targetName);

            return await CopyIntoStageAsync(order, OrderStages.Print, sourceFile, targetName);
        }

        private static bool UsesOrderFolderStorage(OrderData order)
        {
            return !string.IsNullOrWhiteSpace(order.FolderName);
        }

        private async Task<string> CopyToGrandpaFromSourceAsync(string sourceFile, string targetName)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                throw new FileNotFoundException("Файл для копирования не найден.", cleanSource);

            var destinationRoot = string.IsNullOrWhiteSpace(_grandpaFolder)
                ? GetTempStageFolder(OrderStages.Print)
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

            await CopyFileWithTrayProgressAsync(
                cleanSource,
                destination,
                $"Копирование в Дедушку: {Path.GetFileName(cleanSource)}");
            TrySetClipboardText(destination);
            SetBottomStatus("Скопировано в Дедушку");
            return destination;
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

        private async Task EnsureSourceCopyAsync(OrderData order, string sourceFile)
        {
            if (!string.IsNullOrWhiteSpace(order.SourcePath) && HasExistingFile(order.SourcePath))
                return;

            var newPath = await CopyIntoStageAsync(order, OrderStages.Source, sourceFile);
            UpdateOrderFilePath(order, OrderStages.Source, newPath);
        }

        private async Task CopyFileWithTrayProgressAsync(string sourcePath, string destinationPath, string statusText)
        {
            const int bufferSize = 1024 * 1024;
            BeginFileTransferStatus(statusText);

            try
            {
                using var sourceStream = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var destinationStream = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                var totalBytes = sourceStream.Length;
                var copiedBytes = 0L;
                var lastReportedPercent = -1;
                var buffer = new byte[bufferSize];

                ReportFileTransferStatus(statusText, 0, totalBytes);

                int readBytes;
                while ((readBytes = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await destinationStream.WriteAsync(buffer, 0, readBytes);
                    copiedBytes += readBytes;

                    if (totalBytes <= 0)
                    {
                        ReportFileTransferStatus(statusText, copiedBytes, totalBytes);
                        continue;
                    }

                    var nextPercent = Math.Clamp((int)Math.Round((double)copiedBytes * 100d / totalBytes), 0, 100);
                    if (nextPercent <= lastReportedPercent)
                        continue;

                    lastReportedPercent = nextPercent;
                    ReportFileTransferStatus(statusText, copiedBytes, totalBytes);
                }

                await destinationStream.FlushAsync();
                ReportFileTransferStatus(statusText, totalBytes, totalBytes);
            }
            finally
            {
                EndFileTransferStatus();
            }
        }

        private static string GetStageDisplayName(int stage)
        {
            return stage switch
            {
                OrderStages.Source => "\"1. исходные\"",
                OrderStages.Prepared => "\"2. подготовка\"",
                OrderStages.Print => "\"3. печать\"",
                _ => "этап"
            };
        }

        private static string GetStageLogKey(int stage)
        {
            return stage switch
            {
                OrderStages.Source => "source",
                OrderStages.Prepared => "prepared",
                OrderStages.Print => "print",
                _ => "unknown"
            };
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

