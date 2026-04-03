using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private sealed class StatusCellVisual
        {
            public StatusCellVisual(string status, Image? icon, Color iconBackgroundColor, Color textColor)
            {
                Status = status;
                Icon = icon;
                IconBackgroundColor = iconBackgroundColor;
                TextColor = textColor;
            }

            public string Status { get; }
            public Image? Icon { get; }
            public Color IconBackgroundColor { get; }
            public Color TextColor { get; }
        }

        private readonly Dictionary<string, StatusCellVisual> _statusCellVisuals = new(StringComparer.OrdinalIgnoreCase);
        private Image? _groupOrderCellIcon;

        private void InitializeStatusCellVisuals()
        {
            try
            {
                DisposeStatusCellVisuals();

                var iconBackCompleted = Color.FromArgb(198, 234, 198);
                var iconBackProcessed = Color.FromArgb(255, 232, 205);
                var iconBackArchive = Color.FromArgb(255, 255, 255);
                var iconBackBuilding = Color.FromArgb(255, 255, 255);
                var iconBackProcessing = Color.FromArgb(255, 248, 205);
                var iconBackWaiting = Color.FromArgb(255, 255, 255);
                var iconBackCancelled = Color.FromArgb(255, 255, 255);
                var iconBackError = Color.FromArgb(255, 204, 204);

                RegisterStatusCellVisual(
                    status: WorkflowStatusNames.Processed,
                    icon: LoadStatusCellIcon("file export", "file_export"),
                    iconBackgroundColor: iconBackProcessed,
                    textColor: Color.Black);

                RegisterStatusCellVisual(
                    status: WorkflowStatusNames.Archived,
                    icon: LoadStatusCellIcon("archive", "archive"),
                    iconBackgroundColor: iconBackArchive,
                    textColor: Color.Black);

                RegisterStatusCellVisual(
                    status: WorkflowStatusNames.Building,
                    icon: LoadStatusCellIcon("cards", "cards"),
                    iconBackgroundColor: iconBackBuilding,
                    textColor: Color.Black);

                RegisterStatusCellVisual(
                    status: WorkflowStatusNames.Processing,
                    icon: LoadStatusCellIcon("upload", "upload"),
                    iconBackgroundColor: iconBackProcessing,
                    textColor: Color.Black);

                RegisterStatusCellVisual(
                    status: WorkflowStatusNames.Waiting,
                    icon: LoadStatusCellIcon("file export", "file_export"),
                    iconBackgroundColor: iconBackWaiting,
                    textColor: Color.Black);

                RegisterStatusCellVisual(
                    status: WorkflowStatusNames.Cancelled,
                    icon: LoadStatusCellIcon("action", "block", ("action", "cancel")),
                    iconBackgroundColor: iconBackCancelled,
                    textColor: Color.Black);

                RegisterStatusCellVisual(
                    status: WorkflowStatusNames.Error,
                    icon: LoadStatusCellIcon("error", "error"),
                    iconBackgroundColor: iconBackError,
                    textColor: Color.Black);

                RegisterStatusCellVisual(
                    status: WorkflowStatusNames.Completed,
                    icon: LoadStatusCellIcon("check", "check"),
                    iconBackgroundColor: iconBackCompleted,
                    textColor: Color.Black);

                RegisterStatusCellVisual(
                    status: WorkflowStatusNames.Printed,
                    icon: LoadStatusCellIcon("check", "check"),
                    iconBackgroundColor: iconBackCompleted,
                    textColor: Color.Black);

                _groupOrderCellIcon?.Dispose();
                _groupOrderCellIcon = LoadStatusCellIcon("file", "folder_open", ("files", "folder_open"), ("file", "folder"));
            }
            catch (Exception ex)
            {
                Logger.Warn($"Не удалось инициализировать иконки статусов: {ex.Message}");
            }
        }

        private void RegisterStatusCellVisual(string status, Image? icon, Color iconBackgroundColor, Color textColor)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                icon?.Dispose();
                return;
            }

            var normalizedStatus = status.Trim();
            if (_statusCellVisuals.TryGetValue(normalizedStatus, out var existing))
                existing.Icon?.Dispose();

            _statusCellVisuals[normalizedStatus] = new StatusCellVisual(normalizedStatus, icon, iconBackgroundColor, textColor);
        }

        private void DisposeStatusCellVisuals()
        {
            foreach (var visual in _statusCellVisuals.Values)
                visual.Icon?.Dispose();

            _statusCellVisuals.Clear();
            _groupOrderCellIcon?.Dispose();
            _groupOrderCellIcon = null;
        }

        private static Image? LoadStatusCellIcon(string iconFolder, string fileNameHint, params (string Folder, string FileNameHint)[] fallbacks)
        {
            return OrdersWorkspaceIconCatalog.LoadIcon(iconFolder, fileNameHint, size: 16, fallbacks);
        }

        private bool TryPaintStatusCell(DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != colStatus.Index || e.Graphics == null)
                return false;

            var rawStatus = e.FormattedValue?.ToString() ?? e.Value?.ToString() ?? string.Empty;
            if (!TryGetStatusCellVisualForRow(e.RowIndex, rawStatus, out var visual))
                return false;

            var paintParts = e.PaintParts
                & ~DataGridViewPaintParts.ContentForeground
                & ~DataGridViewPaintParts.Focus;
            e.Paint(e.CellBounds, paintParts);

            const int markerWidth = 3;
            var contentBounds = Rectangle.Inflate(e.CellBounds, -1, -1);
            var markerRect = new Rectangle(contentBounds.Left, contentBounds.Top, markerWidth, contentBounds.Height);
            if (e.RowIndex >= 0 && dgvJobs.Rows[e.RowIndex].Selected)
            {
                using var markerBrush = new SolidBrush(OrdersActiveMarkerColor);
                e.Graphics.FillRectangle(markerBrush, markerRect);
            }

            var iconBackWidth = Math.Min(40, Math.Max(28, contentBounds.Width / 4));
            var iconBackRect = new Rectangle(
                markerRect.Right,
                contentBounds.Top,
                iconBackWidth,
                contentBounds.Height);
            using (var iconBackBrush = new SolidBrush(visual.IconBackgroundColor))
            {
                e.Graphics.FillRectangle(iconBackBrush, iconBackRect);
            }

            if (visual.Icon != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var iconSize = Math.Max(12, Math.Min(18, iconBackRect.Height - 8));
                var iconRect = new Rectangle(
                    iconBackRect.Left + (iconBackRect.Width - iconSize) / 2,
                    iconBackRect.Top + (iconBackRect.Height - iconSize) / 2,
                    iconSize,
                    iconSize);
                e.Graphics.DrawImage(visual.Icon, iconRect);
            }

            var textBounds = new Rectangle(
                iconBackRect.Right + 8,
                contentBounds.Top,
                Math.Max(0, contentBounds.Right - (iconBackRect.Right + 10)),
                contentBounds.Height);

            TextRenderer.DrawText(
                e.Graphics,
                rawStatus,
                e.CellStyle?.Font ?? dgvJobs.Font,
                textBounds,
                visual.TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            return true;
        }

        private bool TryGetStatusCellVisualForRow(int rowIndex, string rawStatus, out StatusCellVisual visual)
        {
            var rowTag = dgvJobs.Rows[rowIndex].Tag?.ToString();
            if (IsOrderTag(rowTag))
            {
                var order = GetOrderByRowIndex(rowIndex);
                if (order != null && OrderTopologyService.IsMultiOrder(order))
                {
                    visual = new StatusCellVisual("group-order", _groupOrderCellIcon, GroupOrderIconBackColor, Color.Black);
                    return true;
                }
            }

            return TryGetStatusCellVisual(rawStatus, out visual);
        }

        private bool TryGetStatusCellVisual(string? rawStatus, out StatusCellVisual visual)
        {
            if (!string.IsNullOrWhiteSpace(rawStatus))
            {
                var exact = rawStatus.Trim();
                if (_statusCellVisuals.TryGetValue(exact, out var exactVisual) && exactVisual != null)
                {
                    visual = exactVisual;
                    return true;
                }
            }

            var normalizedStatus = NormalizeStatus(rawStatus);
            if (!string.IsNullOrWhiteSpace(normalizedStatus)
                && _statusCellVisuals.TryGetValue(normalizedStatus, out var normalizedVisual)
                && normalizedVisual != null)
            {
                visual = normalizedVisual;
                return true;
            }

            visual = null!;
            return false;
        }
    }
}

