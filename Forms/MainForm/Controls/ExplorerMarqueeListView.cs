using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyManager
{
    internal sealed class ExplorerMarqueeListView : ListView
    {
        private bool _isMarqueeSelecting;
        private Point _marqueeStartPoint;
        private Rectangle _marqueeClientRect = Rectangle.Empty;
        private Rectangle _marqueeScreenRect = Rectangle.Empty;

        public Color MarqueeColor { get; set; } = Color.FromArgb(235, 240, 250);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !HasSelectionModifiers() && HitTest(e.Location).Item == null)
            {
                BeginMarqueeSelection(e.Location);
                return;
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_isMarqueeSelecting)
            {
                base.OnMouseMove(e);
                return;
            }

            if ((Control.MouseButtons & MouseButtons.Left) != MouseButtons.Left)
            {
                EndMarqueeSelection();
                return;
            }

            UpdateMarqueeSelection(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_isMarqueeSelecting && e.Button == MouseButtons.Left)
            {
                EndMarqueeSelection();
                return;
            }

            base.OnMouseUp(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            EndMarqueeSelection();
            base.OnLostFocus(e);
        }

        protected override void Dispose(bool disposing)
        {
            EndMarqueeSelection();
            base.Dispose(disposing);
        }

        private void BeginMarqueeSelection(Point clientPoint)
        {
            EndMarqueeSelection();

            _isMarqueeSelecting = true;
            _marqueeStartPoint = ClampToClient(clientPoint);
            _marqueeClientRect = Rectangle.Empty;
            _marqueeScreenRect = Rectangle.Empty;
            Capture = true;

            BeginUpdate();
            try
            {
                SelectedItems.Clear();
                FocusedItem = null;
            }
            finally
            {
                EndUpdate();
            }
        }

        private void UpdateMarqueeSelection(Point clientPoint)
        {
            if (!_isMarqueeSelecting)
                return;

            DrawReversibleMarquee(_marqueeScreenRect);

            var clampedPoint = ClampToClient(clientPoint);
            var clientRect = GetNormalizedRect(_marqueeStartPoint, clampedPoint);
            _marqueeClientRect = clientRect;
            _marqueeScreenRect = RectangleToScreen(clientRect);

            DrawReversibleMarquee(_marqueeScreenRect);
            ApplyMarqueeSelection(clientRect);
        }

        private void EndMarqueeSelection()
        {
            if (!_isMarqueeSelecting)
                return;

            DrawReversibleMarquee(_marqueeScreenRect);
            _marqueeScreenRect = Rectangle.Empty;
            _marqueeClientRect = Rectangle.Empty;
            _isMarqueeSelecting = false;
            Capture = false;
        }

        private void ApplyMarqueeSelection(Rectangle clientRect)
        {
            BeginUpdate();
            try
            {
                ListViewItem? firstSelected = null;

                foreach (ListViewItem item in Items)
                {
                    var isSelected = item.Bounds.IntersectsWith(clientRect);
                    if (item.Selected != isSelected)
                        item.Selected = isSelected;

                    if (isSelected && firstSelected == null)
                        firstSelected = item;
                }

                if (firstSelected != null)
                    firstSelected.Focused = true;
            }
            finally
            {
                EndUpdate();
            }
        }

        private void DrawReversibleMarquee(Rectangle screenRect)
        {
            if (screenRect.Width <= 0 || screenRect.Height <= 0)
                return;

            var fillColor = Color.FromArgb(160, MarqueeColor);
            var borderColor = ControlPaint.Dark(MarqueeColor, 0.2f);
            ControlPaint.FillReversibleRectangle(screenRect, fillColor);
            ControlPaint.DrawReversibleFrame(screenRect, borderColor, FrameStyle.Thick);
        }

        private Point ClampToClient(Point point)
        {
            var maxX = Math.Max(0, ClientSize.Width - 1);
            var maxY = Math.Max(0, ClientSize.Height - 1);
            var clampedX = Math.Max(0, Math.Min(maxX, point.X));
            var clampedY = Math.Max(0, Math.Min(maxY, point.Y));
            return new Point(clampedX, clampedY);
        }

        private static bool HasSelectionModifiers()
        {
            return (ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None;
        }

        private static Rectangle GetNormalizedRect(Point start, Point end)
        {
            var left = Math.Min(start.X, end.X);
            var top = Math.Min(start.Y, end.Y);
            var width = Math.Abs(end.X - start.X);
            var height = Math.Abs(end.Y - start.Y);
            return new Rectangle(left, top, width, height);
        }
    }
}
