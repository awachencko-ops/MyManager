using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MyManager
{
    internal sealed class HoverStateVScrollBar : Control
    {
        private const int MinThumbHeight = 40;
        private const int ThumbWidth = 8;
        private const int ArrowZoneHeight = 18;
        private const int ArrowGlyphHalfSize = 4;

        private int _minimum;
        private int _maximum;
        private int _largeChange = 1;
        private int _smallChange = 1;
        private int _value;

        private bool _isHovered;
        private bool _isDraggingThumb;
        private int _dragStartY;
        private int _dragStartValue;

        private Rectangle _trackRect = Rectangle.Empty;
        private Rectangle _thumbRect = Rectangle.Empty;
        private Rectangle _upArrowRect = Rectangle.Empty;
        private Rectangle _downArrowRect = Rectangle.Empty;

        public HoverStateVScrollBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);

            Width = 16;
            TabStop = false;
        }

        public event EventHandler? ValueChanged;

        public int Minimum => _minimum;
        public int Maximum => _maximum;
        public int LargeChange => _largeChange;
        public int SmallChange => _smallChange;
        public int Value => _value;

        private bool HasScrollableRange => _maximum > _minimum;

        public void SetState(int minimum, int maximum, int largeChange, int smallChange, int value)
        {
            var normalizedMin = minimum;
            var normalizedMax = Math.Max(minimum, maximum);
            var normalizedLarge = Math.Max(1, largeChange);
            var normalizedSmall = Math.Max(1, smallChange);
            var normalizedValue = Math.Clamp(value, normalizedMin, normalizedMax);

            var changed =
                _minimum != normalizedMin
                || _maximum != normalizedMax
                || _largeChange != normalizedLarge
                || _smallChange != normalizedSmall
                || _value != normalizedValue;

            _minimum = normalizedMin;
            _maximum = normalizedMax;
            _largeChange = normalizedLarge;
            _smallChange = normalizedSmall;
            _value = normalizedValue;

            if (changed)
                Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var trackBackColor = Color.FromArgb(242, 242, 242);
            var borderColor = Color.FromArgb(215, 215, 215);
            var thumbColor =
                _isDraggingThumb
                    ? Color.FromArgb(132, 132, 132)
                    : (_isHovered ? Color.FromArgb(153, 153, 153) : Color.FromArgb(170, 170, 170));

            using (var backBrush = new SolidBrush(trackBackColor))
            {
                graphics.FillRectangle(backBrush, ClientRectangle);
            }

            using (var borderPen = new Pen(borderColor))
            {
                graphics.DrawRectangle(borderPen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            }

            RecalculateGeometry();

            if (_thumbRect != Rectangle.Empty)
            {
                using var thumbBrush = new SolidBrush(thumbColor);
                using var thumbPath = CreateRoundedRectanglePath(_thumbRect, _thumbRect.Width);
                graphics.FillPath(thumbBrush, thumbPath);
            }

            DrawArrowGlyph(graphics, _upArrowRect, isUpDirection: true);
            DrawArrowGlyph(graphics, _downArrowRect, isUpDirection: false);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (_isDraggingThumb)
                return;

            if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
                return;

            _isHovered = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left)
                return;

            if (!HasScrollableRange)
            {
                Invalidate();
                return;
            }

            RecalculateGeometry();

            if (_thumbRect.Contains(e.Location))
            {
                _isDraggingThumb = true;
                _dragStartY = e.Y;
                _dragStartValue = _value;
                Capture = true;
                Invalidate();
                return;
            }

            if (_upArrowRect.Contains(e.Location))
            {
                ChangeValueBy(-_smallChange, raiseEvent: true);
                return;
            }

            if (_downArrowRect.Contains(e.Location))
            {
                ChangeValueBy(_smallChange, raiseEvent: true);
                return;
            }

            if (e.Y < _thumbRect.Top)
            {
                ChangeValueBy(-Math.Max(1, _largeChange - 1), raiseEvent: true);
            }
            else if (e.Y > _thumbRect.Bottom)
            {
                ChangeValueBy(Math.Max(1, _largeChange - 1), raiseEvent: true);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_isDraggingThumb || !HasScrollableRange)
                return;

            RecalculateGeometry();

            var thumbTrackHeight = Math.Max(1, _trackRect.Height - _thumbRect.Height);
            var valueRange = Math.Max(1, _maximum - _minimum);
            var deltaY = e.Y - _dragStartY;
            var deltaValue = (int)Math.Round((double)deltaY * valueRange / thumbTrackHeight);
            SetValueInternal(_dragStartValue + deltaValue, raiseEvent: true);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Left)
                return;

            if (_isDraggingThumb)
            {
                _isDraggingThumb = false;
                Capture = false;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if (!HasScrollableRange)
                return;

            var wheelStep = Math.Max(_smallChange * 3, 1);
            ChangeValueBy(e.Delta > 0 ? -wheelStep : wheelStep, raiseEvent: true);
        }

        private void ChangeValueBy(int delta, bool raiseEvent)
        {
            SetValueInternal(_value + delta, raiseEvent);
        }

        private void SetValueInternal(int nextValue, bool raiseEvent)
        {
            var clamped = Math.Clamp(nextValue, _minimum, _maximum);
            if (clamped == _value)
                return;

            _value = clamped;
            Invalidate();

            if (raiseEvent)
                ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RecalculateGeometry()
        {
            var inner = Rectangle.Inflate(ClientRectangle, -1, -1);
            if (inner.Width <= 0 || inner.Height <= 0)
            {
                _trackRect = Rectangle.Empty;
                _thumbRect = Rectangle.Empty;
                _upArrowRect = Rectangle.Empty;
                _downArrowRect = Rectangle.Empty;
                return;
            }

            var arrowHeight = Math.Min(ArrowZoneHeight, Math.Max(0, inner.Height / 5));
            _upArrowRect = arrowHeight > 0
                ? new Rectangle(inner.Left, inner.Top, inner.Width, arrowHeight)
                : Rectangle.Empty;
            _downArrowRect = arrowHeight > 0
                ? new Rectangle(inner.Left, inner.Bottom - arrowHeight, inner.Width, arrowHeight)
                : Rectangle.Empty;

            _trackRect = Rectangle.FromLTRB(inner.Left, _upArrowRect.Bottom, inner.Right, _downArrowRect.Top);
            if (_trackRect.Height <= 0)
            {
                _thumbRect = Rectangle.Empty;
                return;
            }

            if (!HasScrollableRange)
            {
                // Keep a visible static thumb even when scrolling is not required.
                var staticThumbHeight = Math.Max(
                    MinThumbHeight,
                    Math.Min(Math.Max(0, _trackRect.Height - 2), _trackRect.Height / 3));
                staticThumbHeight = Math.Min(_trackRect.Height, staticThumbHeight);
                var staticThumbTop = _trackRect.Top + 1;
                if (staticThumbTop + staticThumbHeight > _trackRect.Bottom)
                    staticThumbTop = _trackRect.Bottom - staticThumbHeight;
                var staticThumbLeft = _trackRect.Left + ((_trackRect.Width - ThumbWidth) / 2);
                _thumbRect = new Rectangle(staticThumbLeft, staticThumbTop, ThumbWidth, staticThumbHeight);
                return;
            }

            var totalRange = Math.Max(1, _maximum - _minimum + _largeChange);
            var thumbHeight = Math.Max(
                MinThumbHeight,
                (int)Math.Round((double)_largeChange / totalRange * _trackRect.Height));
            thumbHeight = Math.Min(_trackRect.Height, thumbHeight);

            var thumbTravel = Math.Max(0, _trackRect.Height - thumbHeight);
            var progress = _maximum == _minimum
                ? 0d
                : (double)(_value - _minimum) / (_maximum - _minimum);
            var thumbTop = _trackRect.Top + (int)Math.Round(thumbTravel * progress);
            var thumbLeft = _trackRect.Left + ((_trackRect.Width - ThumbWidth) / 2);

            _thumbRect = new Rectangle(thumbLeft, thumbTop, ThumbWidth, thumbHeight);
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return path;

            var clampedRadius = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height)));
            var diameter = clampedRadius;

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawArrowGlyph(Graphics graphics, Rectangle area, bool isUpDirection)
        {
            if (area == Rectangle.Empty)
                return;

            var centerX = area.Left + (area.Width / 2f);
            var centerY = area.Top + (area.Height / 2f);

            PointF[] points = isUpDirection
                ?
                [
                    new PointF(centerX - ArrowGlyphHalfSize, centerY + 1),
                    new PointF(centerX + ArrowGlyphHalfSize, centerY + 1),
                    new PointF(centerX, centerY - ArrowGlyphHalfSize + 1)
                ]
                :
                [
                    new PointF(centerX - ArrowGlyphHalfSize, centerY - 1),
                    new PointF(centerX + ArrowGlyphHalfSize, centerY - 1),
                    new PointF(centerX, centerY + ArrowGlyphHalfSize - 1)
                ];

            using var brush = new SolidBrush(Color.FromArgb(142, 142, 142));
            graphics.FillPolygon(brush, points);
        }
    }
}
