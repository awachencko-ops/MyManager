using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace MyManager
{
    public partial class MainForm
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

        private void InitializeStatusCellVisuals()
        {
            DisposeStatusCellVisuals();

            RegisterStatusCellVisual(
                status: "Завершено",
                icon: LoadStatusCellIcon("check_24dp_1F1F1F_FILL1_wght400_GRAD0_opsz24.png"),
                iconBackgroundColor: Color.FromArgb(198, 234, 198),
                textColor: Color.Black);

            RegisterStatusCellVisual(
                status: "Напечатано",
                icon: LoadStatusCellIcon("check_24dp_1F1F1F_FILL1_wght400_GRAD0_opsz24.png"),
                iconBackgroundColor: Color.FromArgb(198, 234, 198),
                textColor: Color.Black);
        }

        private void RegisterStatusCellVisual(string status, Image? icon, Color iconBackgroundColor, Color textColor)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                icon?.Dispose();
                return;
            }

            if (_statusCellVisuals.TryGetValue(status, out var existing))
                existing.Icon?.Dispose();

            _statusCellVisuals[status] = new StatusCellVisual(status.Trim(), icon, iconBackgroundColor, textColor);
        }

        private void DisposeStatusCellVisuals()
        {
            foreach (var visual in _statusCellVisuals.Values)
                visual.Icon?.Dispose();

            _statusCellVisuals.Clear();
        }

        private static Image? LoadStatusCellIcon(string fileName)
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Icons", "check", fileName),
                Path.Combine(AppContext.BaseDirectory, "check", fileName),
                Path.Combine(Environment.CurrentDirectory, "Icons", "check", fileName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Icons", "check", fileName)
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(candidate);
                    if (!File.Exists(fullPath))
                        continue;

                    using var memory = new MemoryStream(File.ReadAllBytes(fullPath));
                    using var loaded = Image.FromStream(memory);
                    return new Bitmap(loaded);
                }
                catch
                {
                    // Игнорируем невалидные/недоступные кандидаты.
                }
            }

            return null;
        }

        private bool TryPaintStatusCell(DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != colStatus.Index || e.Graphics == null)
                return false;

            var rawStatus = e.FormattedValue?.ToString() ?? e.Value?.ToString() ?? string.Empty;
            if (!TryGetStatusCellVisual(rawStatus, out var visual))
                return false;

            var paintParts = e.PaintParts
                & ~DataGridViewPaintParts.ContentForeground
                & ~DataGridViewPaintParts.Focus;
            e.Paint(e.CellBounds, paintParts);

            var contentBounds = Rectangle.Inflate(e.CellBounds, -1, -1);
            var iconBackWidth = Math.Min(40, Math.Max(28, contentBounds.Width / 4));
            var iconBackRect = new Rectangle(contentBounds.Left, contentBounds.Top, iconBackWidth, contentBounds.Height);
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
