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
        private void CopyPathToClipboard(OrderData order, int stage)
        {
            CopyExistingPathToClipboard(ResolveSingleOrderDisplayPath(order, stage));
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

            PersistGridChanges(OrderGridLogic.BuildOrderTag(order.InternalId));
            AppendOrderOperationLog(
                order,
                OrderOperationNames.PasteStageFile,
                $"scope=order | stage={GetStageLogKey(stage)} | source={Path.GetFileName(clipboardFilePath)}");
        }

        private async Task PasteFileFromClipboardAsync(OrderData order, OrderFileItem item, int stage)
        {
            var clipboardFilePath = TryGetClipboardFilePath();
            if (string.IsNullOrWhiteSpace(clipboardFilePath))
                return;

            if (!await AddFileToItemAsync(order, item, clipboardFilePath, stage))
                return;

            PersistGridChanges(OrderGridLogic.BuildItemTag(order.InternalId, item.ItemId));
            var itemLabel = string.IsNullOrWhiteSpace(item.ClientFileLabel) ? item.ItemId : item.ClientFileLabel;
            AppendOrderOperationLog(
                order,
                OrderOperationNames.PasteStageFile,
                $"scope=item | item={itemLabel} | stage={GetStageLogKey(stage)} | source={Path.GetFileName(clipboardFilePath)}");
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
            var columnIndex = _dragSourceColumnIndex >= 0
                ? _dragSourceColumnIndex
                : dgvJobs.CurrentCell?.ColumnIndex ?? -1;

            if (stage == 0)
                return string.Empty;

            if (!TryResolveGridFileCell(rowIndex, columnIndex, requireExistingFile: true, out var cell))
                return string.Empty;

            if (cell.Stage != stage)
                return string.Empty;

            return cell.Path;
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

    }
}

