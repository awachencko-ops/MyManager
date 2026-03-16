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

        private async Task<bool> AddFileToOrderAsync(OrderData order, string sourceFile, int stage)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                return false;

            if (stage == OrderStages.Print && !await EnsureSimpleOrderInfoForPrintAsync(order))
                return false;

            string targetName;
            if (stage == OrderStages.Print && !string.IsNullOrWhiteSpace(order.Id))
                targetName = $"{order.Id}{Path.GetExtension(cleanSource)}";
            else
                targetName = EnsureUniqueStageFileName(order, stage, Path.GetFileName(cleanSource));

            var newPath = stage == OrderStages.Print
                ? await CopyPrintFileAsync(order, cleanSource, targetName)
                : await CopyIntoStageAsync(order, stage, cleanSource, targetName);

            if (stage == OrderStages.Prepared)
                await EnsureSourceCopyAsync(order, cleanSource);

            UpdateOrderFilePath(order, stage, newPath);
            return true;
        }

        private async Task<bool> AddFileToItemAsync(OrderData order, OrderFileItem item, string sourceFile, int stage)
        {
            var cleanSource = CleanPath(sourceFile);
            if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
                return false;

            if (stage == OrderStages.Print && !await EnsureSimpleOrderInfoForPrintAsync(order))
                return false;

            if (string.IsNullOrWhiteSpace(item.ClientFileLabel))
                item.ClientFileLabel = Path.GetFileNameWithoutExtension(cleanSource);

            string newPath;
            if (stage == OrderStages.Print)
            {
                var printName = EnsureUniqueStageFileName(order, OrderStages.Print, BuildItemPrintFileName(order, item, cleanSource));
                newPath = await CopyPrintFileAsync(order, cleanSource, printName);
            }
            else
            {
                var targetName = EnsureUniqueStageFileName(order, stage, Path.GetFileName(cleanSource));
                newPath = await CopyIntoStageAsync(order, stage, cleanSource, targetName);
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
            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
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
            PersistGridChanges(OrderGridLogic.BuildItemTag(order.InternalId, item.ItemId));
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
            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
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
            PersistGridChanges(OrderGridLogic.BuildItemTag(order.InternalId, item.ItemId));
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

        private static void SetItemStagePath(OrderFileItem item, int stage, string path)
        {
            if (stage == OrderStages.Source)
                item.SourcePath = path;
            else if (stage == OrderStages.Prepared)
                item.PreparedPath = path;
            else if (stage == OrderStages.Print)
                item.PrintPath = path;
        }

        private void UpdateOrderFilePath(OrderData order, int stage, string path)
        {
            if (stage == OrderStages.Source)
                order.SourcePath = path;
            else if (stage == OrderStages.Prepared)
                order.PreparedPath = path;
            else if (stage == OrderStages.Print)
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
                if (stage == OrderStages.Source)
                    order.SourcePath = item.SourcePath;
                else if (stage == OrderStages.Prepared)
                    order.PreparedPath = item.PreparedPath;
                else if (stage == OrderStages.Print)
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
                SetOrderStatus(order, WorkflowStatusNames.Waiting, "file-sync", "empty-items", persistHistory: false, rebuildGrid: false);
                return;
            }

            var total = items.Count;
            var done = items.Count(x => HasExistingFile(x.PrintPath));
            var active = items.Count(x => HasExistingFile(x.SourcePath) || HasExistingFile(x.PreparedPath) || HasExistingFile(x.PrintPath));

            var statusValue = done == total
                ? WorkflowStatusNames.Completed
                : active > 0
                    ? WorkflowStatusNames.Processing
                    : WorkflowStatusNames.Waiting;

            SetOrderStatus(order, statusValue, "file-sync", "aggregate", persistHistory: false, rebuildGrid: false);
        }

        private static string ResolveWorkflowStatus(string? sourcePath, string? preparedPath, string? printPath)
        {
            if (HasExistingFile(printPath))
                return WorkflowStatusNames.Completed;

            if (HasExistingFile(preparedPath) || HasExistingFile(sourcePath))
                return WorkflowStatusNames.Processing;

            return WorkflowStatusNames.Waiting;
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
