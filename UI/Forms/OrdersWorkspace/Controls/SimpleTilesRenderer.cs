using System;
using System.Drawing;
using System.Windows.Forms;
using Manina.Windows.Forms;
using Manina.Windows.Forms.ImageListViewRenderers;

namespace Replica
{
    internal sealed class SimpleTilesRenderer : DefaultRenderer
    {
        private readonly Color _selectedBackColor;
        private readonly Color _selectedBorderColor;
        private readonly Color _hoverBackColor;
        private readonly Color _hoverBorderColor;
        private readonly Color _selectionRectFillColor;
        private readonly Color _selectionRectBorderColor;
        private readonly Color _textColor = Color.FromArgb(24, 28, 36);

        public SimpleTilesRenderer(Color selectedBackColor, Color hoverBackColor)
        {
            _selectedBackColor = selectedBackColor;
            _selectedBorderColor = ControlPaint.Dark(selectedBackColor, 0.12f);
            _hoverBackColor = hoverBackColor;
            _hoverBorderColor = ControlPaint.Dark(hoverBackColor, 0.08f);
            _selectionRectFillColor = Color.FromArgb(90, selectedBackColor);
            _selectionRectBorderColor = ControlPaint.Dark(selectedBackColor, 0.2f);
        }

        public override void DrawBackground(Graphics graphics, Rectangle bounds)
        {
            using var backBrush = new SolidBrush(ImageListView?.BackColor ?? Color.White);
            graphics.FillRectangle(backBrush, bounds);
        }

        public override void DrawBorder(Graphics graphics, Rectangle bounds)
        {
            // Keep border minimal.
        }

        public override void DrawSelectionRectangle(Graphics graphics, Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            using var fillBrush = new SolidBrush(_selectionRectFillColor);
            using var borderPen = new Pen(_selectionRectBorderColor, 1f);
            graphics.FillRectangle(fillBrush, bounds);
            graphics.DrawRectangle(borderPen, GetBorderRect(bounds));
        }

        public override void DrawItem(Graphics graphics, ImageListViewItem item, ItemState state, Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var isSelected = (state & ItemState.Selected) == ItemState.Selected;
            var isHovered = (state & ItemState.Hovered) == ItemState.Hovered;
            var itemBackColor = isSelected
                ? _selectedBackColor
                : (isHovered ? _hoverBackColor : (ImageListView?.BackColor ?? Color.White));
            using (var backBrush = new SolidBrush(itemBackColor))
                graphics.FillRectangle(backBrush, bounds);

            var contentRect = Rectangle.Inflate(bounds, -8, -8);
            if (contentRect.Width <= 0 || contentRect.Height <= 0)
                return;

            using var orderFont = new Font(ImageListView?.Font ?? Control.DefaultFont, FontStyle.Bold);
            var fileFont = ImageListView?.Font ?? Control.DefaultFont;
            var orderTextHeight = TextRenderer.MeasureText(graphics, "Hg", orderFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;
            var fileTextHeight = TextRenderer.MeasureText(graphics, "Hg", fileFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;
            const int textSpacing = 4;
            var textTotalHeight = orderTextHeight + textSpacing + fileTextHeight;

            var availablePreviewHeight = contentRect.Height - textTotalHeight - textSpacing;
            var frameSize = Math.Min(contentRect.Width, availablePreviewHeight);
            if (frameSize < 42)
                frameSize = Math.Max(42, Math.Min(contentRect.Width, contentRect.Height));

            var frameRect = new Rectangle(
                contentRect.Left + (contentRect.Width - frameSize) / 2,
                contentRect.Top,
                frameSize,
                frameSize);

            using (var frameBackBrush = new SolidBrush(Color.White))
                graphics.FillRectangle(frameBackBrush, frameRect);
            using (var frameBorderPen = new Pen(Color.FromArgb(195, 203, 216)))
                graphics.DrawRectangle(frameBorderPen, frameRect);

            var thumbnail = item.ThumbnailImage;
            if (thumbnail != null)
            {
                var imageBounds = Rectangle.Inflate(frameRect, -4, -4);
                var drawRect = FitImageToBounds(thumbnail.Size, imageBounds);
                graphics.DrawImage(thumbnail, drawRect);
            }

            var (orderNumber, fileName) = GetTexts(item);
            var textRectTop = frameRect.Bottom + textSpacing;
            var orderRect = new Rectangle(contentRect.Left, textRectTop, contentRect.Width, orderTextHeight);
            var fileRect = new Rectangle(contentRect.Left, orderRect.Bottom + textSpacing, contentRect.Width, fileTextHeight);
            const TextFormatFlags textFlags =
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine |
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter;

            TextRenderer.DrawText(graphics, orderNumber, orderFont, orderRect, _textColor, textFlags);
            TextRenderer.DrawText(graphics, fileName, fileFont, fileRect, _textColor, textFlags);

            if (isSelected)
            {
                using var borderPen = new Pen(_selectedBorderColor);
                graphics.DrawRectangle(borderPen, GetBorderRect(bounds));
            }
            else if (isHovered)
            {
                using var borderPen = new Pen(_hoverBorderColor);
                graphics.DrawRectangle(borderPen, GetBorderRect(bounds));
            }
        }

        private static (string OrderNumber, string FileName) GetTexts(ImageListViewItem item)
        {
            if (item.Tag is OrdersWorkspaceForm.PrintTileTag tag)
            {
                var order = string.IsNullOrWhiteSpace(tag.OrderNumber) ? "—" : tag.OrderNumber.Trim();
                var file = string.IsNullOrWhiteSpace(tag.PrintFileName) ? item.Text : tag.PrintFileName.Trim();
                return (order, file);
            }

            return ("—", string.IsNullOrWhiteSpace(item.Text) ? item.FileName : item.Text);
        }

        private static Rectangle GetBorderRect(Rectangle rect)
        {
            if (rect.Width <= 1 || rect.Height <= 1)
                return rect;

            return new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        }

        private static Rectangle FitImageToBounds(Size sourceSize, Rectangle targetBounds)
        {
            if (sourceSize.Width <= 0 || sourceSize.Height <= 0 || targetBounds.Width <= 0 || targetBounds.Height <= 0)
                return targetBounds;

            var widthRatio = targetBounds.Width / (double)sourceSize.Width;
            var heightRatio = targetBounds.Height / (double)sourceSize.Height;
            var scale = Math.Min(widthRatio, heightRatio);
            if (scale <= 0)
                scale = 1d;

            var drawWidth = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            var drawHeight = Math.Max(1, (int)Math.Round(sourceSize.Height * scale));
            var drawX = targetBounds.Left + (targetBounds.Width - drawWidth) / 2;
            var drawY = targetBounds.Top + (targetBounds.Height - drawHeight) / 2;

            return new Rectangle(drawX, drawY, drawWidth, drawHeight);
        }
    }
}

