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
        }

        private async Task PasteFileFromClipboardAsync(OrderData order, OrderFileItem item, int stage)
        {
            var clipboardFilePath = TryGetClipboardFilePath();
            if (string.IsNullOrWhiteSpace(clipboardFilePath))
                return;

            if (!await AddFileToItemAsync(order, item, clipboardFilePath, stage))
                return;

            PersistGridChanges(OrderGridLogic.BuildItemTag(order.InternalId, item.ItemId));
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

            if (OrderTopologyService.IsMultiOrder(order))
                return string.Empty;

            return ResolveSingleOrderDisplayPath(order, stage);
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
